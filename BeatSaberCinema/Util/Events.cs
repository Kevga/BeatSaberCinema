using System;

// These events are primarily for other mods to use, so they have no usages in this code base
// ReSharper disable EventNeverSubscribedTo.Global

namespace BeatSaberCinema
{
	public static class Events
	{
		/// <summary>
		/// Indicates if Cinema will be doing something on the upcoming song (either play a video or modify the scene).
		/// Will be invoked as soon as the scene transition to the gameplay scene is initiated.
		/// </summary>
		public static event Action<bool>? CinemaActivated;

		/// <summary>
		/// Used by CustomPlatforms to detect whether or not a custom platform should be loaded.
		/// Will be invoked as soon as the scene transition to the gameplay scene is initiated.
		/// </summary>
		public static event Action<bool>? AllowCustomPlatform;

		/// <summary>
		/// Informs about the selected level in Solo or Party mode. Is fired a bit earlier than the BSEvents event.
		/// </summary>
		public static event Action<IPreviewBeatmapLevel?>? LevelSelected;

		/// <summary>
		/// Broadcasts SongCores DifficultyData every time the LevelDetailView is refreshed
		/// </summary>
		public static event Action<SongCore.Data.ExtraSongData?, SongCore.Data.ExtraSongData.DifficultyData?>? DifficultySelected;

		internal static void InvokeSceneTransitionEvents(VideoConfig? videoConfig)
		{
			if (!SettingsStore.Instance.PluginEnabled || !Plugin.Enabled || videoConfig == null)
			{
				CinemaActivated?.Invoke(false);
				AllowCustomPlatform?.Invoke(true);
				return;
			}

			var cinemaActivated = (videoConfig.IsPlayable || videoConfig.forceEnvironmentModifications == true);
			CinemaActivated?.Invoke(cinemaActivated);

			bool allowCustomPlatform;
			if (videoConfig.allowCustomPlatform == null)
			{
				//If the mapper didn't explicitly allow or disallow custom platforms, use global setting
				allowCustomPlatform = (!cinemaActivated || !SettingsStore.Instance.DisableCustomPlatforms);
			}
			else
			{
				//Otherwise use that setting instead of the global one
				allowCustomPlatform = (!cinemaActivated || videoConfig.allowCustomPlatform == true);
			}

			AllowCustomPlatform?.Invoke(allowCustomPlatform);
		}

		internal static void SetSelectedLevel(IPreviewBeatmapLevel? level)
		{
			LevelSelected?.Invoke(level);
		}

		internal static void SetExtraSongData(SongCore.Data.ExtraSongData? songData, SongCore.Data.ExtraSongData.DifficultyData? selectedDifficultyData)
		{
			DifficultySelected?.Invoke(songData, selectedDifficultyData);
		}
	}
}