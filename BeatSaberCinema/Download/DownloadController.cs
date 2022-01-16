using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IPA.Utilities.Async;
using UnityEngine;

namespace BeatSaberCinema
{
	public class DownloadController: YoutubeDLController
	{
		private readonly ConcurrentDictionary<VideoConfig, Process> _downloadProcesses = new ConcurrentDictionary<VideoConfig, Process>();
		private string _downloadLog = "";

		public event Action<VideoConfig>? DownloadProgress;
		public event Action<VideoConfig>? DownloadFinished;

		private readonly string[] _videoHosterWhitelist = {
			"https://www.youtube.com/",
			"https://www.dailymotion.com/",
			"https://www.facebook.com/",
			"https://www.bilibili.com/",
			"https://vimeo.com/"
		};

		public void StartDownload(VideoConfig video, VideoQuality.Mode quality)
		{
			SharedCoroutineStarter.instance.StartCoroutine(DownloadVideoCoroutine(video, quality));
		}

		private IEnumerator DownloadVideoCoroutine(VideoConfig video, VideoQuality.Mode quality)
		{
			Log.Info($"Starting download of {video.title}");

			_downloadLog = "";

			var downloadProcess = CreateDownloadProcess(video, quality);
			if (downloadProcess == null)
			{
				yield break;
			}

			video.DownloadState = DownloadState.Downloading;
			DownloadProgress?.Invoke(video);

			Log.Info(
				$"youtube-dl command: \"{downloadProcess.StartInfo.FileName}\" {downloadProcess.StartInfo.Arguments}");

			var timeout = new Timeout(5 * 60);

			downloadProcess.OutputDataReceived += (sender, e) =>
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { DownloadOutputDataReceived(e, video); });

			downloadProcess.ErrorDataReceived += (sender, e) =>
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { DownloadErrorDataReceived(e); });

			downloadProcess.Exited += (sender, e) =>
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { DownloadProcessExited((Process) sender, video); });

			downloadProcess.Disposed += (sender, e) =>
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { DownloadProcessDisposed((Process) sender, e); });

			StartProcessThreaded(downloadProcess);
			var startProcessTimeout = new Timeout(10);
			yield return new WaitUntil(() => IsProcessRunning(downloadProcess) || startProcessTimeout.HasTimedOut);
			startProcessTimeout.Stop();

			yield return new WaitUntil(() => !IsProcessRunning(downloadProcess)|| timeout.HasTimedOut);
			if (timeout.HasTimedOut)
			{
				Log.Warn("Timeout reached, disposing download process");
			}
			else
			{
				//When the download is finished, wait for process to exit instead of immediately killing it
				yield return new WaitForSeconds(2f);
			}

			timeout.Stop();
			_downloadLog = "";
			DisposeProcess(downloadProcess);
		}

		private void DownloadOutputDataReceived(DataReceivedEventArgs eventArgs, VideoConfig video)
		{
			_downloadLog += eventArgs.Data+"\r\n";
			Log.Debug(eventArgs.Data);
			ParseDownloadProgress(video, eventArgs);
			DownloadProgress?.Invoke(video);
		}

		private static void DownloadErrorDataReceived(DataReceivedEventArgs eventArgs)
		{
			Log.Error(eventArgs.Data);
		}

		private void DownloadProcessExited(Process process, VideoConfig video)
		{
			var exitCode = process.ExitCode;
			if (exitCode != 0)
			{
				Log.Warn(_downloadLog.Length > 0 ? _downloadLog : "Empty youtube-dl log");

				video.DownloadState = DownloadState.Cancelled;
			}
			Log.Info($"Download process exited with code {exitCode}");

			if (video.DownloadState == DownloadState.Cancelled)
			{
				Log.Info("Cancelled download");
				VideoLoader.DeleteVideo(video);
				DownloadFinished?.Invoke(video);
			}
			else
			{
				process.Disposed -= DownloadProcessDisposed;
				_downloadProcesses.TryRemove(video, out _);
				video.DownloadState = DownloadState.Downloaded;
				video.NeedsToSave = true;
				SharedCoroutineStarter.instance.StartCoroutine(WaitForDownloadToFinishCoroutine(video));
				Log.Info("Download finished");
			}
		}

		private void DownloadProcessDisposed(object sender, EventArgs eventArgs)
		{
			var disposedProcess = (Process) sender;
			foreach(var dictionaryEntry in _downloadProcesses.Where(keyValuePair => keyValuePair.Value == disposedProcess).ToList())
			{
				var video = dictionaryEntry.Key;
				var success = _downloadProcesses.TryRemove(dictionaryEntry.Key, out _);
				if (!success)
				{
					Log.Error("Failed to remove disposed process from list of processes!");
				}
				else
				{
					video.DownloadState = DownloadState.NotDownloaded;
					DownloadFinished?.Invoke(video);
				}
			}
		}

		private static void ParseDownloadProgress(VideoConfig video, DataReceivedEventArgs dataReceivedEventArgs)
		{
			if (dataReceivedEventArgs.Data == null)
			{
				return;
			}

			var rx = new Regex(@"(\d*).\d%+");
			var match = rx.Match(dataReceivedEventArgs.Data);
			if (!match.Success)
			{
				if (dataReceivedEventArgs.Data.Contains("Converting video"))
				{
					video.DownloadState = DownloadState.Converting;
				}
				return;
			}

			var ci = (CultureInfo)CultureInfo.CurrentCulture.Clone();
			ci.NumberFormat.NumberDecimalSeparator = ".";

			video.DownloadProgress =
				float.Parse(match.Value.Substring(0, match.Value.Length - 1), ci) / 100;
		}

		private IEnumerator WaitForDownloadToFinishCoroutine(VideoConfig video)
		{
			var timeout = new Timeout(3);
			yield return new WaitUntil(() => timeout.HasTimedOut || File.Exists(video.VideoPath));

			DownloadFinished?.Invoke(video);
		}

		private Process? CreateDownloadProcess(VideoConfig video, VideoQuality.Mode quality)
		{
			if (video.LevelDir == null)
			{
				Log.Error("LevelDir was null during download");
				return null;
			}

			var success = _downloadProcesses.TryGetValue(video, out _);
			if (success)
			{
				Log.Warn("Existing process not cleaned up yet. Cancelling download attempt.");
				return null;
			}

			if (!Directory.Exists(video.LevelDir))
			{
				//Needed for OST videos
				Directory.CreateDirectory(video.LevelDir);
			}

			var videoFileName = Util.ReplaceIllegalFilesystemChars(video.title ?? video.videoID ?? "video");
			videoFileName = Util.ShortenFilename(video.LevelDir, videoFileName);
			video.videoFile = videoFileName + ".mp4";

			string videoUrl;
			if (video.videoUrl != null)
			{
				if (UrlInWhitelist(video.videoUrl))
				{
					videoUrl = video.videoUrl;
				}
				else
				{
					Log.Error($"Video hoster for {video.videoUrl} is not allowed");
					return null;
				}
			}
			else if (video.videoID != null)
			{
				videoUrl = $"https://www.youtube.com/watch?v={video.videoID}";
			}
			else
			{
				Log.Error("Video config has neither videoID or videoUrl set");
				return null;
			}

			var videoFormat = VideoQuality.ToYoutubeDLFormat(video, quality);
			videoFormat = $" -f \"{videoFormat}\"";

			var downloadProcessArguments = videoUrl +
			                               videoFormat +
			                               " --no-cache-dir" + // Don't use temp storage
			                               $" -o \"{videoFileName}.%(ext)s\"" +
			                               " --no-playlist" + // Don't download playlists, only the first video
			                               " --no-part" + // Don't store download in parts, write directly to file
			                               " --recode-video mp4" + //Re-encode to mp4 (will be skipped most of the time, since it's already in an mp4 container)
			                               " --no-mtime" + //Video last modified will be when it was downloaded, not when it was uploaded to youtube
			                               " --socket-timeout 10"; //Retry if no response in 10 seconds Note: Not if download takes more than 10 seconds but if the time between any 2 messages from the server is 10 seconds

			var process = CreateProcess(downloadProcessArguments, video.LevelDir);
			_downloadProcesses.TryAdd(video, process);
			return process;
		}

		public void CancelDownload(VideoConfig video)
		{
			Log.Debug("Cancelling download");
			video.DownloadState = DownloadState.Cancelled;
			DownloadProgress?.Invoke(video);

			var success = _downloadProcesses.TryGetValue(video, out var process);
			if (success)
			{
				DisposeProcess(process);
			}
			VideoLoader.DeleteVideo(video);
		}

		private bool UrlInWhitelist(string url)
		{
			return _videoHosterWhitelist.Any(url.StartsWith);
		}
	}
}