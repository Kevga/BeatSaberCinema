﻿using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SongCore.Data;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema
{
	public enum DownloadState { NotDownloaded, Preparing, Downloading, DownloadingVideo, DownloadingAudio, Converting, Downloaded, Cancelled }

	[Serializable]
	[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
	public class VideoConfig
	{
		public string? videoID;
		public string? videoUrl;
		public string? title;
		public string? author;
		public string? videoFile;
		public int duration; //s
		public int offset; //ms
		public float? playbackSpeed; //percent
		public bool? loop;
		public float? endVideoAt;
		public bool? configByMapper;
		public bool? bundledConfig;
		[JsonIgnore] public bool IsOfficialConfig => configByMapper is true;

		public bool? transparency;
		[JsonIgnore] public bool TransparencyEnabled => ((transparency == null && !SettingsStore.Instance.TransparencyEnabled) ||
		                                          (transparency != null && !transparency.Value));
		[JsonIgnore] public bool IsDownloading => DownloadState == DownloadState.Preparing ||
		                                          DownloadState == DownloadState.Downloading ||
		                                          DownloadState == DownloadState.DownloadingVideo ||
		                                          DownloadState == DownloadState.DownloadingAudio;

		public SerializableVector3? screenPosition;
		public SerializableVector3? screenRotation;
		public float? screenHeight;
		public float? screenCurvature;
		public bool? curveYAxis;
		public int? screenSubsurfaces;
		public float? bloom;
		public bool? disableDefaultModifications;
		public string? environmentName;
		public bool? forceEnvironmentModifications;
		public bool? allowCustomPlatform;
		public bool? mergePropGroups;

		public ColorCorrection? colorCorrection;
		public Vignette? vignette;
		public UserSettings? userSettings;
		public EnvironmentModification[]? environment;
		public ScreenConfig[]? additionalScreens;

		[JsonIgnore, NonSerialized] public DownloadState DownloadState;
		[JsonIgnore, NonSerialized] public string? ErrorMessage;
		[JsonIgnore, NonSerialized] public bool BackCompat;
		[JsonIgnore, NonSerialized] public bool NeedsToSave;
		[JsonIgnore, NonSerialized] public bool PlaybackDisabledByMissingSuggestion;
		[JsonIgnore, NonSerialized] public float DownloadProgress;
		[JsonIgnore, NonSerialized] public string? LevelDir;
		[JsonIgnore] public string? VideoPath
		{
			get
			{
				videoFile ??= GetVideoFileName();

				if (LevelDir != null && IsWIPLevel)
				{
					var path = Path.Combine(LevelDir, @"..\");
					path = Path.GetFullPath(path);
					var mapFolderName = new DirectoryInfo(LevelDir).Name;
					path = Path.Combine(path, VideoLoader.WIP_DIRECTORY_NAME, mapFolderName!, videoFile);
					return path;
				}

				if (IsLocal && LevelDir != null)
				{
					try
					{
						return Path.Combine(LevelDir, videoFile);
					}
					catch (Exception e)
					{
						Log.Error($"Failed to combine video path for {videoFile}: {e.Message}");
						return null;
					}
				}

				if (IsStreamable)
				{
					return videoFile;
				}

				Log.Debug("VideoPath is null");
				return null;
			}
		}

		[JsonIgnore] public string? ConfigPath => LevelDir != null ? VideoLoader.GetConfigPath(LevelDir) : null;

		[JsonIgnore] public bool IsStreamable => videoFile != null && (videoFile.StartsWith("http://") || videoFile.StartsWith("https://"));
		[JsonIgnore] public bool IsLocal => videoFile != null && !IsStreamable;
		[JsonIgnore] public bool IsPlayable => (DownloadState == DownloadState.Downloaded || IsStreamable) && !PlaybackDisabledByMissingSuggestion;
		[JsonIgnore] public bool IsWIPLevel =>
			LevelDir != null &&
			(LevelDir.Contains(VideoLoader.WIP_MAPS_FOLDER) ||
			 SongCore.Loader.SeperateSongFolders.Any(folder =>
			 {
				 var isWIP = (folder.SongFolderEntry.Pack == FolderLevelPack.CustomWIPLevels || folder.SongFolderEntry.WIP) && LevelDir.Contains(new DirectoryInfo(folder.SongFolderEntry.Path).Name);
				 return isWIP;
			 })
			);

		[JsonIgnore] public bool EnvironmentModified => (environment != null && environment.Length > 0) || screenPosition != null || screenHeight != null;
		[JsonIgnore] public float PlaybackSpeed => playbackSpeed ?? 1;


		private static Regex _regexParseID = new Regex(@"\/watch\?v=([a-z0-9_-]*)",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public VideoConfig()
		{
			//Intentionally empty. Used as ctor for JSON deserializer
		}

		public VideoConfig(VideoConfigListBackCompat configListBackCompat)
		{
			var configBackCompat = configListBackCompat.videos?[configListBackCompat.activeVideo];
			if (configBackCompat == null)
			{
				throw new ArgumentException("Json file was not a video config list");
			}

			title = configBackCompat.title;
			author = configBackCompat.author;
			loop = configBackCompat.loop;
			offset = configBackCompat.offset;
			videoFile = configBackCompat.videoPath;

			//MVP duration implementation for reference:
			/*
			 * duration = duration.Hours > 0
                    ? $"{duration.Hours}:{duration.Minutes}:{duration.Seconds}"
                    : $"{duration.Minutes}:{duration.Seconds}";
             * TimeSpan.Parse assumes HH:MM instead of MM:SS if only one colon is present, so divide result by 60
			 */
			configBackCompat.duration ??= "0:00";
			duration = (int) TimeSpan.Parse(configBackCompat.duration).TotalSeconds;
			var colons = Regex.Matches(configBackCompat.duration, ":").Count;
			if (colons == 1)
			{
				duration /= 60;
			}

			var match = _regexParseID.Match(configBackCompat.URL ?? "");
			if (!match.Success)
			{
				throw new ArgumentException("Video config is missing the video URL");
			}
			videoID = match.Groups[1].Value;
		}

		public VideoConfig(YTResult searchResult, string levelPath)
		{
			videoID = searchResult.ID;
			title = searchResult.Title;
			author = searchResult.Author;
			duration = searchResult.Duration;

			LevelDir = levelPath;
			videoFile = GetVideoFileName();
		}

		private string GetVideoFileName()
		{
			videoFile ??= (Util.ReplaceIllegalFilesystemChars(title ?? videoID ?? "video") + ".mp4");
			videoFile = Util.ShortenFilename(VideoPath!, videoFile);
			return videoFile;
		}

		public new string ToString()
		{
			return $"[{videoID}] {title} by {author} ({duration})";
		}

		public float GetOffsetInSec()
		{
			return offset / 1000f;
		}

		public DownloadState UpdateDownloadState()
		{
			return (DownloadState = (VideoPath != null && (videoID != null || videoUrl != null) && IsLocal && File.Exists(VideoPath) ? DownloadState.Downloaded : DownloadState.NotDownloaded));
		}

		[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
		public class EnvironmentModification
		{
			[JsonRequired] public string? name;
			public string? parentName;
			public string? cloneFrom;

			[JsonIgnore] public GameObject? gameObject;
			[JsonIgnore] public EnvironmentObject? gameObjectClone;

			public bool? active;
			public SerializableVector3? position;
			public SerializableVector3? rotation;
			public SerializableVector3? scale;
		}

		[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
		public class ColorCorrection
		{
			public float? brightness;
			public float? contrast;
			public float? saturation;
			public float? hue;
			public float? exposure;
			public float? gamma;
		}

		[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
		public class Vignette
		{
			public string? type;
			public float? radius;
			public float? softness;
		}

		[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
		public class ScreenConfig
		{
			public SerializableVector3? position;
			public SerializableVector3? rotation;
			public SerializableVector3? scale;
		}

		[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
		public class UserSettings
		{
			public bool? customOffset;
			public int? originalOffset;
		}
	}
}