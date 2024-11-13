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

		private static FileSystemWatcher? _fileSystemWatcher;
		public static event Action<VideoConfig?>? ConfigChanged;
		private static string? _ignoreNextEventForPath;

		//This should ideally be a HashSet, but there is no concurrent version of it. We also don't need the value, so use the smallest possible type.
		internal static readonly ConcurrentDictionary<string, byte> MapsWithVideo = new ConcurrentDictionary<string, byte>();
		private static readonly ConcurrentDictionary<string, VideoConfig> CachedConfigs = new ConcurrentDictionary<string, VideoConfig>();
		private static readonly ConcurrentDictionary<string, VideoConfig> BundledConfigs = new ConcurrentDictionary<string, VideoConfig>();

		private static BeatmapLevelsModel? _beatmapLevelsModel;

		public static BeatmapLevelsModel BeatmapLevelsModel
		{
			get
			{
				if (_beatmapLevelsModel == null)
				{
					_beatmapLevelsModel = Plugin.menuContainer.Resolve<BeatmapLevelsModel>();
				}

				return _beatmapLevelsModel;
			}
		}
		private static BeatmapLevelsEntitlementModel? _beatmapLevelsEntitlementModel;
		private static BeatmapLevelsEntitlementModel BeatmapLevelsEntitlementModel
		{
			get
			{
				if (_beatmapLevelsEntitlementModel == null)
				{
					_beatmapLevelsEntitlementModel = BeatmapLevelsModel._entitlements;
				}

				return _beatmapLevelsEntitlementModel;
			}
		}

		private static AudioClipAsyncLoader AudioClipAsyncLoader
		{
			get
			{
				if (_audioClipAsyncLoader == null)
				{
					_audioClipAsyncLoader = Plugin.menuContainer.Resolve<AudioClipAsyncLoader>();
				}

				return _audioClipAsyncLoader;
			}
		}

		private static AudioClipAsyncLoader? _audioClipAsyncLoader;

		public static void Init()
		{
			var configs = LoadBundledConfigs();
			foreach (var config in configs)
			{
				BundledConfigs.TryAdd(config.levelID, config.config);
			}
		}

		internal static async void IndexMaps(Loader? loader = null, ConcurrentDictionary<string, BeatmapLevel>? beatmapLevels = null)
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

		private static List<BeatmapLevel> GetOfficialMaps()
		{
			var officialMaps = new List<BeatmapLevel>();

			void AddOfficialPackCollection(BeatmapLevelsRepository beatmapLevelsRepository)
			{
				officialMaps.AddRange(beatmapLevelsRepository.beatmapLevelPacks.SelectMany(pack => pack._beatmapLevels));
			}

			AddOfficialPackCollection(BeatmapLevelsModel.ostAndExtrasBeatmapLevelsRepository);
			AddOfficialPackCollection(BeatmapLevelsModel.dlcBeatmapLevelsRepository);

			return officialMaps;
		}

		private static void IndexMap(KeyValuePair<string, BeatmapLevel> levelKeyValuePair)
		{
			IndexMap(levelKeyValuePair.Value);
		}

		private static void IndexMap(BeatmapLevel level)
		{
			var configPath = GetConfigPath(level);
			if (File.Exists(configPath))
			{
				MapsWithVideo.TryAdd(level.levelID, 0);
			}
		}

		public static string GetConfigPath(BeatmapLevel level)
		{
			var levelPath = GetLevelPath(level);
			return Path.Combine(levelPath, CONFIG_FILENAME);
		}

		public static string GetConfigPath(string levelPath)
		{
			return Path.Combine(levelPath, CONFIG_FILENAME);
		}

		public static void AddConfigToCache(VideoConfig config, BeatmapLevel level)
		{
			var success = CachedConfigs.TryAdd(level.levelID, config);
			MapsWithVideo.TryAdd(level.levelID, 0);
			if (success)
			{
				Log.Debug($"Adding config for {level.levelID} to cache");
			}
		}

		public static void RemoveConfigFromCache(BeatmapLevel level)
		{
			var success = CachedConfigs.TryRemove(level.levelID, out _);
			if (success)
			{
				Log.Debug($"Removing config for {level.levelID} from cache");
			}
		}

		private static VideoConfig? GetConfigFromCache(BeatmapLevel level)
		{
			var success = CachedConfigs.TryGetValue(level.levelID, out var config);
			if (success)
			{
				Log.Debug($"Loading config for {level.levelID} from cache");
			}
			return config;
		}

		private static VideoConfig? GetConfigFromBundledConfigs(BeatmapLevel level)
		{
			var levelID = !level.hasPrecalculatedData ? level.levelID : Util.ReplaceIllegalFilesystemChars(level.songName.Trim());
			BundledConfigs.TryGetValue(levelID, out var config);

			if (config == null)
			{
				Log.Debug($"No bundled config found for {levelID}");
				return null;
			}

			config.LevelDir = GetLevelPath(level);
			config.bundledConfig = true;
			Log.Debug("Loaded from bundled configs");
			return config;
		}

		public static void StopFileSystemWatcher()
		{
			Log.Debug("Disposing FileSystemWatcher");
			_fileSystemWatcher?.Dispose();
		}

		public static void SetupFileSystemWatcher(BeatmapLevel level)
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
			CoroutineStarter.Instance.StartCoroutine(WaitForConfigWriteCoroutine(e));
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

		public static bool IsDlcSong(BeatmapLevel level)
		{
			return level.GetType() == typeof(BeatmapLevelSO);
		}

		public static async Task<AudioClip?> GetAudioClipForLevel(BeatmapLevel level)
		{
			if (!IsDlcSong(level))
			{
				return await LoadAudioClipAsync(level);
			}

			var beatmapLevelLoader = (BeatmapLevelLoader)BeatmapLevelsModel.levelLoader;
			if (beatmapLevelLoader._loadedBeatmapLevelDataCache.TryGetFromCache(level.levelID, out var beatmapLevelData))
			{
				Log.Debug("Getting audio clip from async cache");
				return await _audioClipAsyncLoader.LoadSong(beatmapLevelData);
			}

			return await LoadAudioClipAsync(level);
		}

		private static async Task<AudioClip?> LoadAudioClipAsync(BeatmapLevel level)
		{
			var loaderTask = AudioClipAsyncLoader?.LoadPreview(level);
			if (loaderTask == null)
			{
				Log.Error("AudioClipAsyncLoader.LoadPreview() failed");
				return null;
			}

			return await loaderTask;
		}

		public static async Task<EntitlementStatus> GetEntitlementForLevel(BeatmapLevel level)
		{
			return await BeatmapLevelsEntitlementModel.GetLevelEntitlementStatusAsync(level.levelID, CancellationToken.None);
		}

		public static VideoConfig? GetConfigForEditorLevel(BeatmapDataModel _, string originalPath)
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

		public static VideoConfig? GetConfigForLevel(BeatmapLevel? level)
		{
			if (InstalledMods.BeatSaberPlaylistsLib)
			{
				level = level.GetLevelFromPlaylistIfAvailable();
			}

			if (level == null)
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

			VideoConfig? videoConfig = null;
			var levelPath = GetLevelPath(level);
			if (Directory.Exists(levelPath))
			{
				videoConfig = LoadConfig(GetConfigPath(levelPath));
				if (videoConfig == null && !InstalledMods.MusicVideoPlayer)
				{
					//Back compatiblity with MVP configs, but only if MVP is not installed
					videoConfig = LoadConfig(Path.Combine(levelPath, CONFIG_FILENAME_MVP));
				}
			}
			else
			{
				Log.Debug($"Path does not exist: {levelPath}");
			}

			if (InstalledMods.BeatSaberPlaylistsLib && videoConfig == null && level.TryGetPlaylistLevelConfig(levelPath, out var playlistConfig))
			{
				videoConfig = playlistConfig;
			}

			return videoConfig ?? GetConfigFromBundledConfigs(level);
		}

		[Obsolete("Obsolete")]
		public static string GetLevelPath(BeatmapLevel level)
		{
			if (!level.hasPrecalculatedData)
			{
				return Collections.GetCustomLevelPath(level.levelID);
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

		public static bool DeleteConfig(VideoConfig videoConfig, BeatmapLevel level)
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
					if (videoConfigListBackCompat == null)
					{
						Log.Warn($"Deserializing video config at {configPath} failed");
						return null;
					}
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

		private static IEnumerable<BundledConfig> LoadBundledConfigs()
		{
			var buffer = BeatSaberMarkupLanguage.Utilities.GetResource(Assembly.GetExecutingAssembly(), "BeatSaberCinema.Resources.configs.json");
			var jsonString = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
			var configs = JsonConvert.DeserializeObject<BundledConfig[]>(jsonString);
			if (configs == null)
			{
				Log.Error("Failed to deserialize bundled configs");
				return Enumerable.Empty<BundledConfig>();
			}
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