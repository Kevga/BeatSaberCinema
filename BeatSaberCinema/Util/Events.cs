using System;
using BeatmapEditor3D.DataModels;

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
		public static event Action<LevelSelectedArgs>? LevelSelected;

		/// <summary>
		/// Broadcasts SongCores DifficultyData every time the LevelDetailView is refreshed
		/// </summary>
		public static event Action<ExtraSongDataArgs>? DifficultySelected;

		internal static void InvokeSceneTransitionEvents(VideoConfig? videoConfig)
		{
			if (!Plugin.Enabled || videoConfig == null)
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

		internal static void SetSelectedLevel(BeatmapLevel? level)
		{
			LevelSelected?.InvokeSafe(new LevelSelectedArgs(level), nameof(LevelSelected));
		}

		internal static void SetSelectedLevel(BeatmapDataModel? beatmapData, string originalPath)
		{
			LevelSelected?.InvokeSafe(new LevelSelectedArgs(null, beatmapData, originalPath), nameof(LevelSelected));
		}

		internal static void SetExtraSongData(SongCore.Data.ExtraSongData? songData, SongCore.Data.ExtraSongData.DifficultyData? selectedDifficultyData)
		{
			DifficultySelected?.InvokeSafe(new ExtraSongDataArgs(songData, selectedDifficultyData), nameof(DifficultySelected));
		}
	}
}