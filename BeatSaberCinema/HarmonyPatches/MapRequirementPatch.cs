using System;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using SongCore;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema
{
	[HarmonyAfter("com.kyle1413.BeatSaber.SongCore")]
	[HarmonyPatch(typeof(StandardLevelDetailView), nameof(StandardLevelDetailView.RefreshContent))]
	[UsedImplicitly]
	public class StandardLevelDetailViewRefreshContent
	{

		[UsedImplicitly]
		private static void Postfix(ref IDifficultyBeatmap ____selectedDifficultyBeatmap, ref PlayerData ____playerData,
			ref UnityEngine.UI.Button ____actionButton, ref UnityEngine.UI.Button ____practiceButton)
		{
			try
			{
				if (PlaybackController.Instance.VideoConfig == null)
				{
					return;
				}

				var level = ____selectedDifficultyBeatmap.level is CustomBeatmapLevel ? ____selectedDifficultyBeatmap.level as CustomPreviewBeatmapLevel : null;
				if (level == null)
				{
					return;
				}

				var songData = Collections.RetrieveExtraSongData(SongCore.Utilities.Hashing.GetCustomLevelHash(level), level.customLevelPath);
				if (songData == null)
				{
					return;
				}

				var diffData = Collections.RetrieveDifficultyData(____selectedDifficultyBeatmap);
				Events.SetExtraSongData(songData, diffData);
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
		}
	}
}