using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BeatSaberCinema
{
	public class DownloadController
	{
		private readonly string _youtubeDLFilepath = Environment.CurrentDirectory + "/Libs/youtube-dl.exe";
		private readonly string _ffmpegFilepath = Environment.CurrentDirectory + "/Libs/ffmpeg.exe";
		public readonly List<YTResult> SearchResults = new List<YTResult>();
		private Process? _searchProcess;
		private Process? _downloadProcess;
		private bool SearchInProgress
		{
			get
			{
				try
				{
					return _searchProcess != null && !_searchProcess.HasExited;
				}
				catch (Exception e)
				{
					if (!e.Message.Contains("No process is associated with this object."))
					{
						Plugin.Logger.Warn(e);
					}
				}

				return false;
			}
		}

		private bool DownloadInProgress => _downloadProcess != null && !_downloadProcess.HasExited;
		public event Action<VideoConfig>? DownloadProgress;
		public event Action<VideoConfig>? DownloadFinished;
		public event Action<YTResult>? SearchProgress;

		public bool LibrariesAvailable()
		{
			return File.Exists(_youtubeDLFilepath) && File.Exists(_ffmpegFilepath);
		}

		public void Search(string query)
		{
			// ReSharper disable once NotResolvedInText
			SharedCoroutineStarter.instance.StopCoroutine("SearchCoroutine");
			SharedCoroutineStarter.instance.StartCoroutine(SearchCoroutine(query));
		}

		private IEnumerator SearchCoroutine(string query, int expectedResultCount = 10)
		{
			if (SearchInProgress)
			{
				DisposeProcess(_searchProcess);
			}

			SearchResults.Clear();
			Plugin.Logger.Debug($"Starting search with query {query}");

			_searchProcess = new Process
			{
				StartInfo =
				{
					FileName = _youtubeDLFilepath,
					Arguments = $"\"ytsearch{expectedResultCount}:{query}\" -j -i",
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				},
				EnableRaisingEvents = true,
				PriorityBoostEnabled = true
			};

			_searchProcess.OutputDataReceived += SearchProcessDataReceived;
			_searchProcess.ErrorDataReceived += SearchProcessErrorDataReceived;
			_searchProcess.Exited += SearchProcessExited;

			Plugin.Logger.Info($"Starting youtube-dl process with arguments: \"{_searchProcess.StartInfo.FileName}\" {_searchProcess.StartInfo.Arguments}");
			yield return _searchProcess.Start();

			var timeout = new Timeout(15);
			_searchProcess.BeginErrorReadLine();
			_searchProcess.BeginOutputReadLine();
			// var outputs = searchProcess.StandardOutput.ReadToEnd().Split('\n');
			yield return new WaitUntil(() => !SearchInProgress || timeout.HasTimedOut);
			timeout.Stop();

			DisposeProcess(_searchProcess);
		}

		private void SearchProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				return;
			}

			Plugin.Logger.Error("youtube-dl process error:");
			Plugin.Logger.Error(e.Data);
		}

		private void SearchProcessDataReceived(object sender, DataReceivedEventArgs e)
		{
			var output = e.Data.Trim();
			if (string.IsNullOrWhiteSpace(output))
			{
				return;
			}

			if (output.Contains("yt command exited"))
			{
				Plugin.Logger.Debug("Done with Youtube Search, Processing...");
				return;
			}

			if (output.Contains("yt command"))
			{
				Plugin.Logger.Debug($"Running with {output}");
				return;
			}

			var trimmedLine = output;
			var ytResult = ParseSearchResult(trimmedLine);
			if (ytResult == null)
			{
				return;
			}

			SearchResults.Add(ytResult);
			SearchProgress?.Invoke(ytResult);
		}

		private YTResult? ParseSearchResult(string searchResultJson)
		{
			if (!(JsonConvert.DeserializeObject(searchResultJson) is JObject result))
			{
				Plugin.Logger.Error("Failed to deserialize "+searchResultJson);
				return null;
			}

			if (result["id"] == null)
			{
				Plugin.Logger.Warn("YT search result had no ID, skipping");
				return null;
			}

			var duration = double.Parse(result["duration"]?.ToString() ?? "0");

			YTResult ytResult = new YTResult(
				result["id"]!.ToString(),
				result["title"]?.ToString() ?? "Untitled Video",
				result["uploader"]?.ToString() ?? "Unknown Author",
				Convert.ToInt32(duration));

			return ytResult;
		}

		private void SearchProcessExited(object sender, EventArgs e)
		{
			Plugin.Logger.Info($"Search process exited with exitcode {((Process) sender).ExitCode}");

			DisposeProcess(_searchProcess);
			_searchProcess = null;
		}

		private void DisposeProcess(Process? process)
		{
			if (process == null)
			{
				return;
			}

			Plugin.Logger.Debug("Cleaning up process");

			try
			{
				if (!process.HasExited)
				{
					process.Kill();
				}
			}
			catch (Exception exception)
			{
				if (!exception.Message.Contains("The operation completed successfully") &&
				    !exception.Message.Contains("No process is associated with this object."))
				{
					Plugin.Logger.Warn(exception);
				}
			}
			try
			{
				process.Dispose();
			}
			catch (Exception exception)
			{
				Plugin.Logger.Warn(exception);
			}
		}

		public void StartDownload(VideoConfig video)
		{
			DisposeProcess(_searchProcess);
			SharedCoroutineStarter.instance.StartCoroutine(DownloadVideoCoroutine(video));
		}

		private IEnumerator DownloadVideoCoroutine(VideoConfig video)
		{
			Plugin.Logger.Info($"Starting download of {video.title}");
			if (video.DownloadState == DownloadState.Cancelled)
			{
				Plugin.Logger.Debug("Download cancelled");
				yield break;
			}

			video.DownloadState = DownloadState.Downloading;
			DownloadProgress?.Invoke(video);

			Process localDownloader = StartDownloadProcess(video);

			Plugin.Logger.Info(
				$"youtube-dl command: \"{localDownloader.StartInfo.FileName}\" {localDownloader.StartInfo.Arguments}");

			var timeout = new Timeout(3 * 60);
			var timer = new Timer(250);
			timer.Elapsed += (sender, args) => DownloadProgress?.Invoke(video);
			localDownloader.OutputDataReceived += (sender, e) => DownloadOutputDataReceived(sender, e, video);
			localDownloader.ErrorDataReceived += (sender, e) => DownloadErrorDataReceived(sender, e, video);
			localDownloader.Exited += (sender, e) => DownloadProcessExited(sender, video, timer);
			timer.Start();
			yield return localDownloader.Start();

			localDownloader.BeginOutputReadLine();
			localDownloader.BeginErrorReadLine();

			yield return new WaitUntil(() => !DownloadInProgress || timeout.HasTimedOut);
			timeout.Stop();

			DisposeProcess(_searchProcess);
		}

		private void DownloadOutputDataReceived(object sender, DataReceivedEventArgs eventArgs, VideoConfig video)
		{
			if (video.DownloadState == DownloadState.Cancelled)
			{
				DisposeProcess((Process) sender);
				_downloadProcess = null;
				Plugin.Logger.Info("Download cancelled");
				VideoLoader.DeleteVideo(video);
			}

			ParseDownloadProgress(video, eventArgs);
		}

		private void DownloadErrorDataReceived(object sender, DataReceivedEventArgs eventArgs, VideoConfig video)
		{
			Plugin.Logger.Error(eventArgs.Data);
			video.DownloadState = DownloadState.Cancelled;
			DownloadProgress?.Invoke(video);
			if (video.DownloadState == DownloadState.Cancelled || eventArgs.Data.Contains("Unable to extract video data"))
			{
				DisposeProcess((Process) sender);
			}
		}

		private void DownloadProcessExited(object sender, VideoConfig video, Timer timer)
		{
			timer.Stop();
			timer.Close();

			Plugin.Logger.Info("Download process exited with code "+((Process) sender).ExitCode);

			if (video.DownloadState == DownloadState.Cancelled)
			{
				Plugin.Logger.Info("Cancelled download");
				VideoLoader.DeleteVideo(video);
			}
			else
			{
				video.DownloadState = DownloadState.Downloaded;
				VideoLoader.SaveVideoConfig(video);
				SharedCoroutineStarter.instance.StartCoroutine(WaitForDownloadToFinishCoroutine(video));
				Plugin.Logger.Info("Download finished");
			}

			DisposeProcess(_downloadProcess);

			_downloadProcess = null;
		}

		private void ParseDownloadProgress(VideoConfig video, DataReceivedEventArgs dataReceivedEventArgs)
		{
			if (dataReceivedEventArgs.Data == null)
			{
				return;
			}

			Regex rx = new Regex(@"(\d*).\d%+");
			Match match = rx.Match(dataReceivedEventArgs.Data);
			if (match.Success)
			{
				video.DownloadProgress =
					float.Parse(match.Value.Substring(0, match.Value.Length - 1)) / 100;
			}
		}

		private IEnumerator WaitForDownloadToFinishCoroutine(VideoConfig video)
		{
			var timeout = new Timeout(1);
			yield return new WaitUntil(() => timeout.HasTimedOut || File.Exists(video.VideoPath));

			DownloadProgress?.Invoke(video);
			DownloadFinished?.Invoke(video);
			VideoMenu.instance.SetupVideoDetails();
		}

		private Process StartDownloadProcess(VideoConfig video)
		{
			string levelPath = VideoLoader.GetLevelPath(video.Level);
			if (!Directory.Exists(levelPath))
			{
				//Mostly for OST videos
				Directory.CreateDirectory(levelPath);
			}

			string videoFileName = Util.ReplaceIllegalFilesystemChars(video.title ?? video.videoID ?? "video");
			video.videoFile = videoFileName + ".mp4";

			var downloadProcess = new Process
			{
				StartInfo =
				{
					FileName = Environment.CurrentDirectory + "/Libs/youtube-dl.exe",
					Arguments = "https://www.youtube.com/watch?v=" + video.videoID +
					            $" -f \"{VideoQuality.ToYoutubeDLFormat(SettingsStore.Instance.QualityMode)}\"" + // Formats
					            " --no-cache-dir" + // Don't use temp storage
					            $" -o \"{videoFileName}.%(ext)s\"" +
					            " --no-playlist" + // Don't download playlists, only the first video
					            " --no-part" + // Don't store download in parts, write directly to file
						        " --recode-video mp4" + //Re-encode to mp4 (will be skipped most of the time, since it's already in an mp4 container)
					            " --no-mtime" + //Video last modified will be when it was downloaded, not when it was uploaded to youtube
					            " --socket-timeout 10" + //Retry if no response in 10 seconds Note: Not if download takes more than 10 seconds but if the time between any 2 messages from the server is 10 seconds
					            " --no-continue" //overwrite existing file and force re-download
					,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true,
					WorkingDirectory = levelPath
				},
				EnableRaisingEvents = true,
				//I think these are added only after Process Started
				//PriorityClass = ProcessPriorityClass.RealTime,
				PriorityBoostEnabled = true
			};
			_downloadProcess = downloadProcess;
			return downloadProcess;
		}

		public void CancelDownload(VideoConfig video)
		{
			DisposeProcess(_downloadProcess);
			video.DownloadState = DownloadState.Cancelled;
			DownloadProgress?.Invoke(video);
			VideoLoader.DeleteVideo(video);

		}

		// ReSharper disable once InconsistentNaming (YtResult looks bad)
		public class YTResult
		{
			public readonly string ID;
			public readonly string Title;
			public readonly string Author;
			public readonly int Duration;

			public YTResult(string id, string title, string author, int duration)
			{
				ID = id;
				Title = title;
				Author = author;
				Duration = duration;
			}

			public new string ToString()
			{
				return $"[{ID}] {Title} by {Author} ({Duration})";
			}
		}
	}
}