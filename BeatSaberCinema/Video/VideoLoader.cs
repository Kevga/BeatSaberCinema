using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatmapEditor3D.DataModels;
using IPA.Utilities;
using IPA.Utilities.Async;
using Newtonsoft.Json;
using SongCore;
using UnityEngine;

namespace BeatSaberCinema
{
	public static class VideoLoader
	{
		private const string OST_DIRECTORY_NAME = "CinemaOSTVideos";
		internal const string WIP_DIRECTORY_NAME = "CinemaWIPVideos";
		internal const string WIP_MAPS_FOLDER = "CustomWIPLevels";
		private const string CONFIG_FILENAME = "cinema-video.json";
		private const string CONFIG_FILENAME_MVP = "video.json";
		private const string MOD_ID_MVP = "Music Video Player";

		private static FileSystemWatcher? _fileSystemWatcher;
		public static event Action<VideoConfig?>? ConfigChanged;
		private static string? _ignoreNextEventForPath;

		private static AudioClipAsyncLoader? _audioClipAsyncLoader;

		//This should ideally be a HashSet, but there is no concurrent version of it. We also don't need the value, so use the smallest possible type.
		internal static readonly ConcurrentDictionary<string, byte> MapsWithVideo = new ConcurrentDictionary<string, byte>();
		private static readonly ConcurrentDictionary<string, VideoConfig> CachedConfigs = new ConcurrentDictionary<string, VideoConfig>();
		private static readonly ConcurrentDictionary<string, VideoConfig> BundledConfigs = new ConcurrentDictionary<string, VideoConfig>();

		private static BeatmapLevelsModel? _beatmapLevelsModel;

		private static BeatmapLevelsModel? BeatmapLevelsModel
		{
			get
			{
				if (_beatmapLevelsModel == null)
				{
					_beatmapLevelsModel = Resources.FindObjectsOfTypeAll<BeatmapLevelsModel>().FirstOrDefault(x => x.name.Contains("(Clone)"));
					if (_beatmapLevelsModel == null)
					{
						Log.Error("Failed to get a reference to BeatmapLevelsModel");
					}
				}

				return _beatmapLevelsModel;
			}
		}
		private static AdditionalContentModel? _additionalContentModel;
		private static AdditionalContentModel? AdditionalContentModel
		{
			get
			{
				if (_additionalContentModel == null)
				{
					//The game has instances for AdditionalContentModels for each platform. The "true" one has (Clone) in its name.
					_additionalContentModel = BeatmapLevelsModel!.GetField<AdditionalContentModel, BeatmapLevelsModel>("_additionalContentModel");
					if (!_additionalContentModel)
					{
						Log.Error("Failed to get the AdditionalContentModel from BeatmapLevelsModel");
					}
				}

				return _additionalContentModel;
			}
		}

		private static AudioClipAsyncLoader? AudioClipAsyncLoader
		{
			get
			{
				if (_audioClipAsyncLoader == null)
				{
					_audioClipAsyncLoader = BeatmapLevelsModel!.GetField<AudioClipAsyncLoader, BeatmapLevelsModel>("_audioClipAsyncLoader");
					if (_audioClipAsyncLoader == null)
					{
						Log.Error("Failed to get a reference to AudioClipAsyncLoader");
					}
				}

				return _audioClipAsyncLoader;
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

		internal static async void IndexMaps(Loader? loader = null, ConcurrentDictionary<string, CustomPreviewBeatmapLevel>? customPreviewBeatmapLevels = null)
		{
			Log.Debug("Indexing maps...");
			var stopwatch = new Stopwatch();
			stopwatch.Start();

			var officialMaps = GetOfficialMaps();

			void Action()
			{
				var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, (Environment.ProcessorCount / 2) - 1) };
				Parallel.ForEach(Loader.CustomLevels, options, IndexMap);
				if (officialMaps.Count > 0)
				{
					Parallel.ForEach(officialMaps, options, IndexMap);
				}
			}

			var loadingTask = new Task((Action) Action, CancellationToken.None);
			var loadingAwaiter = loadingTask.ConfigureAwait(false);
			loadingTask.Start();
			await loadingAwaiter;

			Log.Debug($"Indexing took {stopwatch.ElapsedMilliseconds} ms");
		}

		private static List<IPreviewBeatmapLevel> GetOfficialMaps()
		{
			var officialMaps = new List<IPreviewBeatmapLevel>();

			if (BeatmapLevelsModel == null)
			{
				return officialMaps;
			}

			void AddOfficialPackCollection(IBeatmapLevelPackCollection packCollection)
			{
				officialMaps.AddRange(packCollection.beatmapLevelPacks.SelectMany(pack => pack.beatmapLevelCollection.beatmapLevels));
			}

			AddOfficialPackCollection(BeatmapLevelsModel.ostAndExtrasPackCollection);
			AddOfficialPackCollection(BeatmapLevelsModel.dlcBeatmapLevelPackCollection);

			return officialMaps;
		}

		private static void IndexMap(KeyValuePair<string, CustomPreviewBeatmapLevel> levelKeyValuePair)
		{
			IndexMap(levelKeyValuePair.Value);
		}

		private static void IndexMap(IPreviewBeatmapLevel level)
		{
			var configPath = GetConfigPath(level);
			if (File.Exists(configPath))
			{
				MapsWithVideo.TryAdd(level.levelID, 0);
			}
		}

		public static string GetConfigPath(IPreviewBeatmapLevel level)
		{
			var levelPath = GetLevelPath(level);
			return Path.Combine(levelPath, CONFIG_FILENAME);
		}

		public static string GetConfigPath(string levelPath)
		{
			return Path.Combine(levelPath, CONFIG_FILENAME);
		}

		public static void AddConfigToCache(VideoConfig config, IPreviewBeatmapLevel level)
		{
			var success = CachedConfigs.TryAdd(level.levelID, config);
			MapsWithVideo.TryAdd(level.levelID, 0);
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

		public static void SetupFileSystemWatcher(IPreviewBeatmapLevel level)
		{
			var levelPath = GetLevelPath(level);
			ListenForConfigChanges(levelPath);
		}

		public static void SetupFileSystemWatcher(string path)
		{
			ListenForConfigChanges(path);
		}

		private static void ListenForConfigChanges(string levelPath)
		{
			_fileSystemWatcher?.Dispose();
			if (!Directory.Exists(levelPath))
			{
				if (File.Exists(levelPath))
				{
					levelPath = Path.GetDirectoryName(levelPath)!;
				}
				else
				{
					Log.Debug($"Level directory {levelPath} does not exist");
					return;
				}
			}

			Log.Debug($"Setting up FileSystemWatcher for {levelPath}");

			_fileSystemWatcher = new FileSystemWatcher();
			var configPath = GetConfigPath(levelPath);
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
			if (_ignoreNextEventForPath == e.FullPath && !Util.IsInEditor())
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

		public static async Task<AudioClip?> GetAudioClipForLevel(IPreviewBeatmapLevel level)
		{
			if (!IsDlcSong(level) || BeatmapLevelAsyncCache == null)
			{
				return await LoadAudioClipAsync(level);
			}

			var levelData = await BeatmapLevelAsyncCache[level.levelID];
			if (levelData != null)
			{
				Log.Debug("Getting audio clip from async cache");
				return levelData.beatmapLevelData.audioClip;
			}

			return await LoadAudioClipAsync(level);
		}

		private static async Task<AudioClip?> LoadAudioClipAsync(IPreviewBeatmapLevel level)
		{
			var loaderTask = AudioClipAsyncLoader?.LoadPreview(level);
			if (loaderTask == null)
			{
				Log.Error("AudioClipAsyncLoader.LoadPreview() failed");
				return null;
			}

			return await loaderTask;
		}

		public static async Task<AdditionalContentModel.EntitlementStatus> GetEntitlementForLevel(IPreviewBeatmapLevel level)
		{
			if (AdditionalContentModel != null)
			{
				return await AdditionalContentModel.GetLevelEntitlementStatusAsync(level.levelID, CancellationToken.None);
			}

			return AdditionalContentModel.EntitlementStatus.Owned;
		}

		public static VideoConfig? GetConfigForEditorLevel(IBeatmapDataModel _, string originalPath)
		{
			if (!Directory.Exists(originalPath))
			{
				Log.Debug($"Path does not exist: {originalPath}");
				return null;
			}

			var configPath = GetConfigPath(originalPath);
			var videoConfig = LoadConfig(configPath);

			return videoConfig;
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

			var videoConfig = LoadConfig(GetConfigPath(levelPath));
			if (videoConfig == null && !Util.IsModInstalled(MOD_ID_MVP))
			{
				//Back compatiblity with MVP configs, but only if MVP is not installed
				videoConfig = LoadConfig(Path.Combine(levelPath, CONFIG_FILENAME_MVP));
			}

			if (videoConfig != null)
			{
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
			if (videoConfig.LevelDir == null || videoConfig.ConfigPath == null || !Directory.Exists(videoConfig.LevelDir))
			{
				Log.Warn("Failed to save video. Path "+videoConfig.LevelDir+" does not exist.");
				return;
			}

			if (videoConfig.IsWIPLevel)
			{
				videoConfig.configByMapper = true;
			}

			var configPath = videoConfig.ConfigPath;
			SaveVideoConfigToPath(videoConfig, configPath);
		}

		public static void SaveVideoConfigToPath(VideoConfig config, string configPath)
		{
			_ignoreNextEventForPath = configPath;
			Log.Info($"Saving video config to {configPath}");

			try
			{
				File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
				config.NeedsToSave = false;
			}
			catch (Exception e)
			{
				Log.Error("Failed to save level data: ");
				Log.Error(e);
			}

			if (!File.Exists(configPath))
			{
				Log.Error("Config file doesn't exist after saving!");
			}
			else
			{
				Log.Debug("Config save successful");
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
				var cinemaConfigPath = GetConfigPath(videoConfig.LevelDir);
				if (File.Exists(cinemaConfigPath))
				{
					File.Delete(cinemaConfigPath);
				}

				var mvpConfigPath = Path.Combine(videoConfig.LevelDir, CONFIG_FILENAME_MVP);
				if (File.Exists(mvpConfigPath))
				{
					File.Delete(mvpConfigPath);
				}

				MapsWithVideo.TryRemove(level.levelID, out _);
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
				Log.Warn($"Config file {configPath} does not exist");
				return null;
			}

			VideoConfig? videoConfig;
			try
			{
				var json = File.ReadAllText(configPath);
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

			// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
			if (videoConfig != null)
			{
				videoConfig.LevelDir = Path.GetDirectoryName(configPath);
				videoConfig.UpdateDownloadState();
			}
			else
			{
				Log.Warn($"Deserializing video config at {configPath} failed");
			}

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
			var buffer = BeatSaberMarkupLanguage.Utilities.GetResource(Assembly.GetExecutingAssembly(), "BeatSaberCinema.Resources.configs.json");
			var jsonString = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
			var configs = JsonConvert.DeserializeObject<BundledConfig[]>(jsonString);
			return configs;
		}
	}

	[Serializable]
	internal class BundledConfig
	{
		public string levelID = null!;
		public VideoConfig config = null!;
	}
}