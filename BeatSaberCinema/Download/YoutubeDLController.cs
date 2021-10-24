using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using IPA.Utilities;

namespace BeatSaberCinema
{
	public abstract class YoutubeDLController
	{
		private readonly string _youtubeDLFilepath = Path.Combine(UnityGame.LibraryPath, "yt-dlp.exe");
		private readonly string _ffmpegFilepath = Path.Combine(UnityGame.LibraryPath, "ffmpeg.exe");
		private readonly string _youtubeDLConfigFilepath = Path.Combine(UnityGame.UserDataPath, "youtube-dl.conf");

		private bool? _librariesAvailable;
		public bool LibrariesAvailable()
		{
			if (_librariesAvailable != null)
			{
				return _librariesAvailable.Value;
			}

			_librariesAvailable = File.Exists(_youtubeDLFilepath) && File.Exists(_ffmpegFilepath);
			return _librariesAvailable.Value;
		}

		private static string GetConfigFileArgument(string path)
		{
			return !File.Exists(path) ? " --ignore-config" : $" --config-location \"{path}\"";
		}

		protected static void DisposeProcess(Process? process)
		{
			if (process == null)
			{
				return;
			}

			Log.Debug("Cleaning up process");

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
					Log.Warn(exception);
				}
			}
			try
			{
				process.Dispose();
			}
			catch (Exception exception)
			{
				Log.Warn(exception);
			}
		}

		protected Process CreateProcess(string arguments, string? workingDirectory = null)
		{
			//Use config file in UserData instead of the global yt-dl one
			arguments += GetConfigFileArgument(_youtubeDLConfigFilepath);

			var process = new Process
			{
				StartInfo =
				{
					FileName = _youtubeDLFilepath,
					Arguments = arguments,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true,
					WorkingDirectory = workingDirectory ?? string.Empty
				},
				EnableRaisingEvents = true,
				PriorityBoostEnabled = true
			};
			process.Disposed += OnProcessDisposed;

			return process;
		}

		public static void StartProcessThreaded(Process process)
		{
			void WorkDelegate()
			{
				var timer = new Stopwatch();
				timer.Start();
				var ret = process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				Log.Debug($"Starting thread took {timer.Elapsed.TotalMilliseconds}ms");
				if (!ret)
				{
					Log.Error("Failed to start thread");
				}
			}

			Thread thread = new Thread((ThreadStart) WorkDelegate);
			thread.Start();
		}

		public static bool IsProcessRunning(Process? process)
		{
			try
			{
				return process != null && !process.HasExited;
			}
			catch (Exception e)
			{
				if (!(e is InvalidOperationException))
				{
					Log.Warn(e);
				}
			}

			return false;
		}

		private void OnProcessDisposed(object sender, EventArgs eventArgs)
		{
			Log.Debug("Process disposed event fired");
		}
	}
}