using System;
using System.IO;

namespace BeatSaberCinema
{
	public static class NothingToSeeHere
	{
		public const string LEVEL_ID = "custom_level_103D39B43966277C5E4167AB086F404E0943891F";
		private static DownloadController? _downloadController;
		private static bool _downloadAttempted;

		public static void Init(DownloadController downloadController)
		{
			_downloadController = downloadController;
		}

		private static bool ItsHappening
		{
			get
			{
				var time = IPA.Utilities.Utils.CanUseDateTimeNowSafely ? DateTime.Now : DateTime.UtcNow;
				return time.Month == 4 && time.Day == 1;
			}
		}

		public static string GetPath()
		{
			return Path.Combine(Environment.CurrentDirectory, "Beat Saber_Data", "CustomLevels", VideoLoader.OST_DIRECTORY_NAME, LEVEL_ID);
		}

		public static void MultiplayerLobbyJoined()
		{
			if (!ItsHappening || _downloadAttempted)
			{
				return;
			}

			var config = VideoLoader.GetConfigFromBundledConfigs(LEVEL_ID);
			if (config == null)
			{
				return;
			}

			config.LevelDir = GetPath();
			config.videoFile = config.title + ".mp4";
			config.UpdateDownloadState();
			if (config.IsPlayable)
			{
				return;
			}
			_downloadController?.StartDownload(config, VideoQuality.Mode.Q480P);
			_downloadAttempted = true;
		}
	}
}