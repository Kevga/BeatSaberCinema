using System;
using System.Collections;
using System.Collections.Generic;
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
		private readonly Dictionary<VideoConfig, Process> _downloadProcesses = new Dictionary<VideoConfig, Process>();
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

		private static bool IsDownloadInProgress(Process? process)
		{
			try
			{
				return process != null && !process.HasExited;
			}
			catch (Exception e)
			{
				Log.Debug(e);
			}

			return false;
		}

		public void StartDownload(VideoConfig video, VideoQuality.Mode quality)
		{
			SharedCoroutineStarter.instance.StartCoroutine(DownloadVideoCoroutine(video, quality));
		}

		private IEnumerator DownloadVideoCoroutine(VideoConfig video, VideoQuality.Mode quality)
		{
			Log.Info($"Starting download of {video.title}");

			_downloadLog = "";

			var downloadProcess = StartDownloadProcess(video, quality);
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
				UnityMainThreadTaskScheduler.Factory.StartNew(delegate { DownloadProcessExited(((Process) sender).ExitCode, video); });

			yield return downloadProcess.Start();

			downloadProcess.BeginOutputReadLine();
			downloadProcess.BeginErrorReadLine();

			yield return new WaitUntil(() => !IsDownloadInProgress(downloadProcess) || timeout.HasTimedOut);
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
			_downloadProcesses.Remove(video);
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

		private void DownloadProcessExited(int exitCode, VideoConfig video)
		{
			if (exitCode != 0)
			{
				Log.Warn(_downloadLog.Length > 0 ? _downloadLog : "Empty youtube-dl log");

				video.DownloadState = DownloadState.Cancelled;
			}
			Log.Info($"Download process exited with code {exitCode}");
			if (exitCode == -1073741515)
			{
				Log.Error("youtube-dl did not run. Possibly missing vc++ 2010 redist: https://www.microsoft.com/en-US/download/details.aspx?id=5555");
			}

			if (video.DownloadState == DownloadState.Cancelled)
			{
				Log.Info("Cancelled download");
				VideoLoader.DeleteVideo(video);
				DownloadFinished?.Invoke(video);
			}
			else
			{
				video.DownloadState = DownloadState.Downloaded;
				video.NeedsToSave = true;
				SharedCoroutineStarter.instance.StartCoroutine(WaitForDownloadToFinishCoroutine(video));
				Log.Info("Download finished");
			}
		}

		private static void ParseDownloadProgress(VideoConfig video, DataReceivedEventArgs dataReceivedEventArgs)
		{
			if (dataReceivedEventArgs.Data == null)
			{
				return;
			}

			Regex rx = new Regex(@"(\d*).\d%+");
			Match match = rx.Match(dataReceivedEventArgs.Data);
			if (!match.Success)
			{
				if (dataReceivedEventArgs.Data.Contains("Converting video"))
				{
					video.DownloadState = DownloadState.Converting;
				}
				return;
			}

			CultureInfo ci = (CultureInfo)CultureInfo.CurrentCulture.Clone();
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

		private Process? StartDownloadProcess(VideoConfig video, VideoQuality.Mode quality)
		{
			if (video.LevelDir == null)
			{
				throw new Exception("LevelDir was null during download");
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
			videoFormat = videoFormat == null ? "" : $" -f \"{videoFormat}\"";

			var downloadProcessArguments = videoUrl +
			                               videoFormat +
			                               " --no-cache-dir" + // Don't use temp storage
			                               $" -o \"{videoFileName}.%(ext)s\"" +
			                               " --no-playlist" + // Don't download playlists, only the first video
			                               " --no-part" + // Don't store download in parts, write directly to file
			                               " --recode-video mp4" + //Re-encode to mp4 (will be skipped most of the time, since it's already in an mp4 container)
			                               " --no-mtime" + //Video last modified will be when it was downloaded, not when it was uploaded to youtube
			                               " --socket-timeout 10"; //Retry if no response in 10 seconds Note: Not if download takes more than 10 seconds but if the time between any 2 messages from the server is 10 seconds

			var process = StartProcess(downloadProcessArguments, video.LevelDir);
			_downloadProcesses.Add(video, process);
			return process;
		}

		public void CancelDownload(VideoConfig video)
		{
			Log.Debug("Cancelling download");
			var success = _downloadProcesses.TryGetValue(video, out var process);
			if (success)
			{
				DisposeProcess(process);
				_downloadProcesses.Remove(video);
			}

			video.DownloadState = DownloadState.Cancelled;
			DownloadFinished?.Invoke(video);
			VideoLoader.DeleteVideo(video);
		}

		private bool UrlInWhitelist(string url)
		{
			return _videoHosterWhitelist.Any(url.StartsWith);
		}
	}
}