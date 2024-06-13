using System;
using HarmonyLib;
using JetBrains.Annotations;
using SongCore;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
{
	[HarmonyAfter("com.kyle1413.BeatSaber.SongCore")]
	[HarmonyPatch(typeof(StandardLevelDetailView), nameof(StandardLevelDetailView.CheckIfBeatmapLevelDataExists))]
	[UsedImplicitly]
	public class StandardLevelDetailViewRefreshContent
	{

		[UsedImplicitly]
		private static void Postfix(StandardLevelDetailView __instance)
		{
			try
			{
				if (PlaybackController.Instance.VideoConfig == null)
				{
					return;
				}

				if (__instance._beatmapLevel.hasPrecalculatedData)
				{
					return;
				}

				var songData = Collections.RetrieveExtraSongData(SongCore.Utilities.Hashing.GetCustomLevelHash(__instance._beatmapLevel));
				if (songData == null)
				{
					return;
				}

				var diffData = Collections.RetrieveDifficultyData(__instance._beatmapLevel, __instance.beatmapKey);
				Events.SetExtraSongData(songData, diffData);

				if (diffData?.HasCinemaRequirement() != true)
				{
					return;
				}

				if (PlaybackController.Instance.VideoConfig?.IsPlayable == true || PlaybackController.Instance.VideoConfig?.forceEnvironmentModifications == true)
				{
					Log.Debug("Requirement fulfilled");
					return;
				}

				Log.Info("Cinema requirement not met for "+__instance._beatmapLevel.songName);
				__instance._actionButton.interactable = false;
				__instance._practiceButton.interactable = false;
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
		}
	}
}