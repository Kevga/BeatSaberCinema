using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IPA.Utilities;
using IPA.Utilities.Async;
using UnityEngine;
using Newtonsoft.Json;

namespace BeatSaberCinema
{
	public static class VideoLoader
	{
		public const string OST_DIRECTORY_NAME = "CinemaOSTVideos";
		private const string WIP_DIRECTORY_NAME = "CustomWIPLevels";
		private const string CONFIG_FILENAME = "cinema-video.json";
		private const string CONFIG_FILENAME_MVP = "video.json";
		private const string MOD_ID_MVP = "Music Video Player";

		private static FileSystemWatcher? _fileSystemWatcher;
		public static event Action<VideoConfig?>? ConfigChanged;
		private static string? _ignoreNextEventForPath;

		private static readonly ConcurrentDictionary<string, VideoConfig> CachedConfigs = new ConcurrentDictionary<string, VideoConfig>();
		private static readonly ConcurrentDictionary<string, VideoConfig> BundledConfigs = new ConcurrentDictionary<string, VideoConfig>();

		private static AdditionalContentModel? _additionalContentModel;
		private static AdditionalContentModel? AdditionalContentModel
		{
			get
			{
				if (_additionalContentModel == null)
				{
					//The game has instances for AdditionalContentModels for each platform. The "true" one has (Clone) in its name.
					_additionalContentModel = Resources.FindObjectsOfTypeAll<AdditionalContentModel>().FirstOrDefault(x => x.name.Contains("(Clone)"));
				}

				return _additionalContentModel;
			}
		}

		private static AsyncCache<string, IBeatmapLevel>? BeatmapLevelAsyncCache
		{
			get
			{
				var levelDataLoader = Resources.FindObjectsOfTypeAll<BeatmapLevelDataLoaderSO>().FirstOrDefault();
				if (levelDataLoader != null)
				{
					_beatmapLevelAsyncCache = levelDataLoader.GetField<AsyncCache<string, IBeatmapLevel>, BeatmapLevelDataLoaderSO>("_beatmapLevelsAsyncCache");
				}

				return _beatmapLevelAsyncCache;
			}
		}

		private static AsyncCache<string, IBeatmapLevel>? _beatmapLevelAsyncCache;

		public static void Init()
		{
			var configs = LoadBundledConfigs();
			foreach (var config in configs)
			{
				BundledConfigs.TryAdd(config.levelID, config.config);
			}
		}

		public static void AddConfigToCache(VideoConfig config, IPreviewBeatmapLevel level)
		{
			var success = CachedConfigs.TryAdd(level.levelID, config);
			if (success)
			{
				Log.Debug($"Adding config for {level.levelID} to cache");
			}
		}

		public static void RemoveConfigFromCache(IPreviewBeatmapLevel level)
		{
			var success = CachedConfigs.TryRemove(level.levelID, out _);
			if (success)
			{
				Log.Debug($"Removing config for {level.levelID} from cache");
			}
		}

		private static VideoConfig? GetConfigFromCache(IPreviewBeatmapLevel level)
		{
			var success = CachedConfigs.TryGetValue(level.levelID, out var config);
			if (success)
			{
				Log.Debug($"Loading config for {level.levelID} from cache");
			}
			return config;
		}

		private static VideoConfig? GetConfigFromBundledConfigs(IPreviewBeatmapLevel level)
		{
			BundledConfigs.TryGetValue(level.levelID, out var config);

			if (config == null)
			{
				return config;
			}

			config.LevelDir = GetLevelPath(level);
			config.bundledConfig = true;
			return config;
		}

		public static void StopFileSystemWatcher()
		{
			Log.Debug("Disposing FileSystemWatcher");
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

			_fileSystemWatcher.Changed += OnConfigChanged;
			_fileSystemWatcher.Created += OnConfigChanged;
			_fileSystemWatcher.Deleted += OnConfigChanged;
			_fileSystemWatcher.Renamed += OnConfigChanged;
		}

		private static void OnConfigChanged(object _, FileSystemEventArgs e)
		{
			UnityMainThreadTaskScheduler.Factory.StartNew(delegate { OnConfigChangedMainThread(e); });
		}

		private static void OnConfigChangedMainThread(FileSystemEventArgs e)
		{
			Log.Debug("Config "+e.ChangeType+" detected: "+e.FullPath);
			if (_ignoreNextEventForPath == e.FullPath)
			{
				Log.Debug("Ignoring event after saving");
				_ignoreNextEventForPath = null;
				return;
			}
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

		public static bool IsDlcSong(IPreviewBeatmapLevel level)
		{
			return level.GetType() == typeof(PreviewBeatmapLevelSO);
		}

		public static async Task<AudioClip> GetAudioClipForLevel(IPreviewBeatmapLevel level)
		{
			if (!IsDlcSong(level) || BeatmapLevelAsyncCache == null)
			{
				return await level.GetPreviewAudioClipAsync(CancellationToken.None);
			}

			Log.Debug("Getting audio clip from async cache");
			var levelData = await BeatmapLevelAsyncCache[level.levelID];
			if (levelData != null)
			{
				return levelData.beatmapLevelData.audioClip;
			}

			return await level.GetPreviewAudioClipAsync(CancellationToken.None);
		}

		public static async Task<AdditionalContentModel.EntitlementStatus> GetEntitlementForLevel(IPreviewBeatmapLevel level)
		{
			if (AdditionalContentModel != null)
			{
				return await AdditionalContentModel.GetLevelEntitlementStatusAsync(level.levelID, CancellationToken.None);
			}

			return AdditionalContentModel.EntitlementStatus.Owned;
		}

		public static VideoConfig? GetConfigForLevel(IPreviewBeatmapLevel? level, bool isPlaylistSong = false)
		{
			var playlistSong = level;
			if (isPlaylistSong)
			{
				level = GetBeatmapLevelFromPlaylistSong(level);
			}

			if (playlistSong == null || level == null)
			{
				return null;
			}

			var cachedConfig = GetConfigFromCache(level);
			if (cachedConfig != null)
			{
				if (cachedConfig.DownloadState == DownloadState.Downloaded)
				{
					RemoveConfigFromCache(level);
				}
				return cachedConfig;
			}

			var levelPath = GetLevelPath(level);
			if (!Directory.Exists(levelPath))
			{
				Log.Debug($"Path does not exist: {levelPath}");
				return null;
			}

			VideoConfig? videoConfig;
			var results = Directory.GetFiles(levelPath, CONFIG_FILENAME, SearchOption.AllDirectories);
			if (results.Length == 0 && !Util.IsModInstalled(MOD_ID_MVP))
			{
				//Back compatiblity with MVP configs, but only if MVP is not installed
				results = Directory.GetFiles(levelPath, CONFIG_FILENAME_MVP, SearchOption.AllDirectories);
			}

			if (results.Length != 0)
			{
				videoConfig = LoadConfig(results[0]);

				//Update bundled configs with new environmentName parameter to fix broken configs
				var bundledConfig = GetConfigFromBundledConfigs(level);
				if (bundledConfig != null && videoConfig?.videoID == bundledConfig.videoID && bundledConfig.environmentName != null)
				{
					Log.Info($"Updating existing config for video {videoConfig?.title}");
					bundledConfig.videoFile = videoConfig?.videoFile;
					bundledConfig.UpdateDownloadState();
					bundledConfig.NeedsToSave = true;
					videoConfig = bundledConfig;
				}
			}
			else if (isPlaylistSong && PlaylistSongHasConfig(playlistSong))
			{
				videoConfig = LoadConfigFromPlaylistSong(playlistSong, levelPath);
			}
			else
			{
				videoConfig = GetConfigFromBundledConfigs(level);
				if (videoConfig == null)
				{
					return videoConfig;
				}
				Log.Debug("Loaded from bundled configs");
			}

			return videoConfig;
		}

		private static bool PlaylistSongHasConfig(IPreviewBeatmapLevel level)
		{
			var playlistSong = level as BeatSaberPlaylistsLib.Types.IPlaylistSong;
			return playlistSong?.TryGetCustomData("cinema", out _) ?? false;
		}

		public static IPreviewBeatmapLevel? GetBeatmapLevelFromPlaylistSong(IPreviewBeatmapLevel? level)
		{
			IPreviewBeatmapLevel? unwrappedLevel = null!;
			if (level is BeatSaberPlaylistsLib.Types.IPlaylistSong playlistSong)
			{
				unwrappedLevel = playlistSong.PreviewBeatmapLevel;
			}

			return unwrappedLevel ?? level;
		}

		public static string GetLevelPath(IPreviewBeatmapLevel level)
		{
			if (level is CustomPreviewBeatmapLevel customlevel)
			{
				return customlevel.customLevelPath;
			}

			var songName = level.songName.Trim();
			songName = Util.ReplaceIllegalFilesystemChars(songName);
			return Path.Combine(Environment.CurrentDirectory, "Beat Saber_Data", "CustomLevels", OST_DIRECTORY_NAME, songName);
		}

		public static void SaveVideoConfig(VideoConfig videoConfig)
		{
			if (videoConfig.LevelDir == null || !Directory.Exists(videoConfig.LevelDir))
			{
				Log.Warn("Failed to save video. Path "+videoConfig.LevelDir+" does not exist.");
				return;
			}

			if (videoConfig.IsWIPLevel)
			{
				videoConfig.configByMapper = true;
			}

			var videoJsonPath = Path.Combine(videoConfig.LevelDir, CONFIG_FILENAME);
			_ignoreNextEventForPath = videoJsonPath;
			Log.Info($"Saving video config to {videoJsonPath}");

			try
			{
				File.WriteAllText(videoJsonPath, JsonConvert.SerializeObject(videoConfig, Formatting.Indented));
			}
			catch (Exception e)
			{
				Log.Error("Failed to save level data: ");
				Log.Error(e);
			}
		}

		public static void DeleteVideo(VideoConfig videoConfig)
		{
			if (videoConfig.VideoPath == null)
			{
				Log.Warn("Tried to delete video, but its path was null");
				return;
			}

			try
			{
				File.Delete(videoConfig.VideoPath);
				Log.Info("Deleted video at "+videoConfig.VideoPath);
				if (videoConfig.DownloadState != DownloadState.Cancelled)
				{
					videoConfig.DownloadState = DownloadState.NotDownloaded;
				}

				videoConfig.videoFile = null;
			}
			catch (Exception e)
			{
				Log.Error("Failed to delete video at "+videoConfig.VideoPath);
				Log.Error(e);
			}
		}

		public static bool DeleteConfig(VideoConfig videoConfig, IPreviewBeatmapLevel level)
		{
			if (videoConfig.LevelDir == null)
			{
				Log.Error("LevelDir was null when trying to delete config");
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
				Log.Error("Failed to delete video config:");
				Log.Error(e);
			}

			RemoveConfigFromCache(level);
			Log.Info("Deleted video config");

			return true;
		}

		private static VideoConfig? LoadConfig(string configPath)
		{
			if (!File.Exists(configPath))
			{
				Log.Warn("Config file "+configPath+" does not exist");
				return null;
			}

			VideoConfig? videoConfig;
			try
			{
				string json = File.ReadAllText(configPath);
				if (configPath.EndsWith("\\" + CONFIG_FILENAME_MVP))
				{
					//Back compatiblity with MVP configs
					var videoConfigListBackCompat = JsonConvert.DeserializeObject<VideoConfigListBackCompat>(json);
					videoConfig = new VideoConfig(videoConfigListBackCompat);
				}
				else
				{
					videoConfig = JsonConvert.DeserializeObject<VideoConfig>(json);
				}
			}
			catch (Exception e)
			{
				Log.Error($"Error parsing video json {configPath}:");
				Log.Error(e);
				return null;
			}

			videoConfig.LevelDir = Path.GetDirectoryName(configPath);
			videoConfig.UpdateDownloadState();

			return videoConfig;
		}

		private static VideoConfig? LoadConfigFromPlaylistSong(IPreviewBeatmapLevel previewBeatmapLevel, string levelPath)
		{
			if (!(previewBeatmapLevel is BeatSaberPlaylistsLib.Types.IPlaylistSong playlistSong))
			{
				return null;
			}

			if (playlistSong.TryGetCustomData("cinema", out var cinemaData))
			{
				VideoConfig? videoConfig;
				try
				{
					var json = JsonConvert.SerializeObject(cinemaData);
					videoConfig = JsonConvert.DeserializeObject<VideoConfig>(json);
				}
				catch (Exception e)
				{
					Log.Error($"Error parsing video json {playlistSong.Name}:");
					Log.Error(e);
					return null;
				}
				videoConfig.LevelDir = levelPath;
				videoConfig.UpdateDownloadState();

				return videoConfig;
			}

			Log.Error($"No config exists for {playlistSong.Name}:");
			return null;
		}

		private static IEnumerable<BundledConfig> LoadBundledConfigs()
		{
			var buffer = BS_Utils.Utilities.UIUtilities.GetResource(Assembly.GetExecutingAssembly(), "BeatSaberCinema.Resources.configs.json");
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