using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BS_Utils.Utilities;
using UnityEngine;
using Newtonsoft.Json;

namespace BeatSaberCinema
{
	public static class VideoLoader
	{
		private const string OST_DIRECTORY_NAME = "CinemaOSTVideos";
		private const string WIP_DIRECTORY_NAME = "CustomWIPLevels";
		private const string CONFIG_FILENAME = "cinema-video.json";
		private const string CONFIG_FILENAME_MVP = "video.json";

		private static FileSystemWatcher? _fileSystemWatcher;
		public static event Action<VideoConfig?>? ConfigChanged;

		private static readonly ConcurrentDictionary<string, VideoConfig> BundledConfigs = new ConcurrentDictionary<string, VideoConfig>();

		// ReSharper disable once InconsistentNaming
		public static BeatmapLevelsModel? BeatmapLevelsModelSO
		{
			get
			{
				if (_beatmapLevelsModel == null)
				{
					_beatmapLevelsModel = Resources.FindObjectsOfTypeAll<BeatmapLevelsModel>().FirstOrDefault();
				}

				return _beatmapLevelsModel;
			}
		}

		private static BeatmapLevelsModel? _beatmapLevelsModel;

		public static void Init()
		{
			var configs = LoadBundledConfigs();
			foreach (var config in configs)
			{
				BundledConfigs.TryAdd(config.levelID, config.config);
			}
		}

		private static VideoConfig? GetConfigFromBundledConfigs(IPreviewBeatmapLevel level)
		{
			BundledConfigs.TryGetValue(level.levelID, out var config);
			return config;
		}

		public static void StopFileSystemWatcher()
		{
			Plugin.Logger.Debug("Disposing FileSystemWatcher");
			_fileSystemWatcher?.Dispose();
		}

		public static void ListenForConfigChanges(IPreviewBeatmapLevel level)
		{
			_fileSystemWatcher?.Dispose();

			var levelPath = GetLevelPath(level);
			if (!Directory.Exists(levelPath))
			{
				return;
			}

			_fileSystemWatcher = new FileSystemWatcher();
			var configPath = Path.Combine(levelPath, CONFIG_FILENAME);
			_fileSystemWatcher.Path = Path.GetDirectoryName(configPath);
			_fileSystemWatcher.Filter = Path.GetFileName(configPath);
			_fileSystemWatcher.EnableRaisingEvents = true;

			_fileSystemWatcher.Changed += ChangeHandlerDelegate;
			_fileSystemWatcher.Created += ChangeHandlerDelegate;
			_fileSystemWatcher.Deleted += ChangeHandlerDelegate;
			_fileSystemWatcher.Renamed += ChangeHandlerDelegate;
		}

		private static void ChangeHandlerDelegate(object source, FileSystemEventArgs e)
		{
			OnConfigChanged(e);
		}

		private static void OnConfigChanged(FileSystemEventArgs e)
		{
			Plugin.Logger.Debug("Config "+e.ChangeType+" detected: "+e.FullPath);
			SharedCoroutineStarter.instance.StartCoroutine(WaitForConfigWriteCoroutine(e));
		}

		private static IEnumerator WaitForConfigWriteCoroutine(FileSystemEventArgs e)
		{
			if (e.ChangeType == WatcherChangeTypes.Deleted)
			{
				ConfigChanged?.Invoke(null);
				yield break;
			}

			var configPath = e.FullPath;
			var configFileInfo = new FileInfo(configPath);
			var timeout = new Timeout(3f);
			yield return new WaitUntil(() =>
				!Util.IsFileLocked(configFileInfo) || timeout.HasTimedOut);
			var config = LoadConfig(configPath);
			ConfigChanged?.Invoke(config);
		}

		public static VideoConfig? GetConfigForLevel(IPreviewBeatmapLevel level)
		{
			if (level.GetType() == typeof(PreviewBeatmapLevelSO))
			{
				//DLC songs currently not supported.
				//TODO look into BeatmapLevelsModelSO.GetBeatmapLevelAsync(). In previous attempts the async task never finished.
				return null;
			}

			var levelPath = GetLevelPath(level);
			if (!Directory.Exists(levelPath))
			{
				Plugin.Logger.Debug($"Path does not exist: {levelPath}");
				return null;
			}

			VideoConfig? videoConfig;
			var results = Directory.GetFiles(levelPath, CONFIG_FILENAME, SearchOption.AllDirectories);
			if (results.Length == 0)
			{
				//Back compatiblity with MVP configs
				results = Directory.GetFiles(levelPath, CONFIG_FILENAME_MVP, SearchOption.AllDirectories);
			}

			if (results.Length != 0)
			{
				videoConfig = LoadConfig(results[0]);
			}
			else
			{
				Plugin.Logger.Debug("Trying to load from bundled configs...");
				videoConfig = GetConfigFromBundledConfigs(level);
				if (videoConfig != null)
				{
					videoConfig.LevelDir = GetLevelPath(level);
					videoConfig.NeedsToSave = true;
					Plugin.Logger.Debug("Success");
				}
			}

			return videoConfig;
		}

		public static string GetLevelPath(IPreviewBeatmapLevel level)
		{
			if (level is CustomPreviewBeatmapLevel customlevel)
			{
				return customlevel.customLevelPath;
			}

			var songName = level.songName;
			songName = Util.ReplaceIllegalFilesystemChars(songName);
			return Path.Combine(Environment.CurrentDirectory, "Beat Saber_Data", "CustomLevels", OST_DIRECTORY_NAME, songName);
		}

		public static void SaveVideoConfig(VideoConfig videoConfig)
		{
			if (videoConfig.LevelDir == null || !Directory.Exists(videoConfig.LevelDir))
			{
				Plugin.Logger.Warn("Failed to save video. Path "+videoConfig.LevelDir+" does not exist.");
				return;
			}

			if (videoConfig.LevelDir.Contains(WIP_DIRECTORY_NAME))
			{
				videoConfig.configByMapper = true;
			}

			var videoJsonPath = Path.Combine(videoConfig.LevelDir, CONFIG_FILENAME);
			Plugin.Logger.Info($"Saving video config to {videoJsonPath}");

			try
			{
				File.WriteAllText(videoJsonPath, JsonConvert.SerializeObject(videoConfig, Formatting.Indented));
			}
			catch (Exception e)
			{
				Plugin.Logger.Error("Failed to save level data: ");
				Plugin.Logger.Error(e);
			}
		}

		public static void DeleteVideo(VideoConfig videoConfig)
		{
			if (videoConfig.VideoPath == null)
			{
				Plugin.Logger.Warn("Tried to delete video, but its path was null");
				return;
			}

			try
			{
				File.Delete(videoConfig.VideoPath);
				Plugin.Logger.Info("Deleted video at "+videoConfig.VideoPath);
				videoConfig.DownloadState = DownloadState.NotDownloaded;
				videoConfig.videoFile = null;
			}
			catch (Exception e)
			{
				Plugin.Logger.Error("Failed to delete video at "+videoConfig.VideoPath);
				Plugin.Logger.Error(e);
			}
		}

		public static bool DeleteConfig(VideoConfig videoConfig)
		{
			if (videoConfig.LevelDir == null)
			{
				Plugin.Logger.Error("LevelDir was null when trying to delete config");
				return false;
			}

			try
			{
				var cinemaConfigPath = Path.Combine(videoConfig.LevelDir, CONFIG_FILENAME);
				if (File.Exists(cinemaConfigPath))
				{
					File.Delete(cinemaConfigPath);
				}

				var mvpConfigPath = Path.Combine(videoConfig.LevelDir, CONFIG_FILENAME_MVP);
				if (File.Exists(mvpConfigPath))
				{
					File.Delete(mvpConfigPath);
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.Error("Failed to delete video config:");
				Plugin.Logger.Error(e);
			}

			Plugin.Logger.Info("Deleted video config");

			return true;
		}

		private static VideoConfig? LoadConfig(string configPath)
		{
			if (!File.Exists(configPath))
			{
				Plugin.Logger.Warn("Config file "+configPath+" does not exist");
				return null;
			}

			string json;
			try
			{
				json = File.ReadAllText(configPath);
			}
			catch (Exception e)
			{
				Plugin.Logger.Error(e);
				return null;
			}

			VideoConfig? videoConfig;
			try
			{
				if (configPath.EndsWith("\\" + CONFIG_FILENAME))
				{
					videoConfig = JsonConvert.DeserializeObject<VideoConfig>(json);
				} else if (configPath.EndsWith("\\" + CONFIG_FILENAME_MVP))
				{
					//Back compatiblity with MVP configs
					var videoConfigListBackCompat = JsonConvert.DeserializeObject<VideoConfigListBackCompat>(json);
					videoConfig = new VideoConfig(videoConfigListBackCompat);
				}
				else
				{
					Plugin.Logger.Error($"jsonPath {configPath} did not match Cinema or MVP formats");
					return null;
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.Error($"Error parsing video json {configPath}:");
				Plugin.Logger.Error(e);
				return null;
			}

			videoConfig.LevelDir = Path.GetDirectoryName(configPath);
			videoConfig.UpdateDownloadState();

			return videoConfig;
		}

		private static BundledConfig[] LoadBundledConfigs()
		{
			var buffer = UIUtilities.GetResource(Assembly.GetExecutingAssembly(), "BeatSaberCinema.Resources.configs.json");
			string jsonString = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
			var configs = JsonConvert.DeserializeObject<BundledConfig[]>(jsonString);
			return configs;
		}
	}

	[Serializable]
	internal class BundledConfig
	{
		public string levelID = null!;
		public VideoConfig config = null!;

		public BundledConfig()
		{
			//Intentionally empty
		}
	}
}