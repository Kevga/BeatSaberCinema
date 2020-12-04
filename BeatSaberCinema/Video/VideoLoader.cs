using System;
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

		private static readonly ConcurrentDictionary<string, VideoConfig> CachedConfigs = new ConcurrentDictionary<string, VideoConfig>();
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
				Plugin.Logger.Debug($"Adding config for level {config.levelID}: {config.config.videoFile}");
			}
		}

		public static void AddConfigToCache(VideoConfig config)
		{
			CachedConfigs.TryAdd(config.Level.levelID, config);
		}

		private static void RemoveConfigFromCache(VideoConfig config)
		{
			CachedConfigs.TryRemove(config.Level.levelID, out _);
		}

		private static VideoConfig? GetConfigFromCache(IPreviewBeatmapLevel level)
		{
			CachedConfigs.TryGetValue(level.levelID, out var config);
			return config;
		}

		private static VideoConfig? GetConfigFromBundledConfigs(IPreviewBeatmapLevel level)
		{
			BundledConfigs.TryGetValue(level.levelID, out var config);
			return config;
		}

		public static VideoConfig? GetConfigForLevel(IPreviewBeatmapLevel level)
		{
			var cachedConfig = GetConfigFromCache(level);
			if (cachedConfig != null)
			{
				return cachedConfig;
			}

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
				videoConfig = LoadConfig(results[0], levelPath, level);
				if (videoConfig != null)
				{
					AddConfigToCache(videoConfig);
				}
			}
			else
			{
				Plugin.Logger.Debug("Trying to load from bundled configs...");
				videoConfig = GetConfigFromBundledConfigs(level);
				if (videoConfig != null)
				{
					videoConfig.Level = level;
					videoConfig.NeedsToSave = true;
					AddConfigToCache(videoConfig);
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
			var levelPath = GetLevelPath(videoConfig.Level);
			if (!Directory.Exists(levelPath))
			{
				Plugin.Logger.Warn("Failed to save video. Path "+levelPath+" does not exist.");
				return;
			}

			if (levelPath.Contains(WIP_DIRECTORY_NAME))
			{
				videoConfig.configByMapper = true;
			}

			var videoJsonPath = Path.Combine(levelPath, CONFIG_FILENAME);
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

			RemoveConfigFromCache(videoConfig);
			Plugin.Logger.Info("Deleted video config");

			return true;
		}

		private static VideoConfig? LoadConfig(string jsonPath, string levelDir, IPreviewBeatmapLevel level)
		{
			string json;
			try
			{
				json = File.ReadAllText(jsonPath);
			}
			catch (Exception e)
			{
				Plugin.Logger.Error(e);
				return null;
			}

			VideoConfig? videoConfig;
			try
			{
				if (jsonPath.EndsWith("\\" + CONFIG_FILENAME))
				{
					videoConfig = JsonConvert.DeserializeObject<VideoConfig>(json);
				} else if (jsonPath.EndsWith("\\" + CONFIG_FILENAME_MVP))
				{
					//Back compatiblity with MVP configs
					var videoConfigListBackCompat = JsonConvert.DeserializeObject<VideoConfigListBackCompat>(json);
					videoConfig = new VideoConfig(videoConfigListBackCompat);
				}
				else
				{
					Plugin.Logger.Error($"jsonPath {jsonPath} did not match Cinema or MVP formats");
					return null;
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.Error($"Error parsing video json {jsonPath}:");
				Plugin.Logger.Error(e);
				return null;
			}

			if (videoConfig.videoID == null)
			{
				Plugin.Logger.Debug("Video ID is null for "+jsonPath);
				return null;
			}

			videoConfig.Level = level;
			videoConfig.UpdateDownloadState();
			AddConfigToCache(videoConfig);

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