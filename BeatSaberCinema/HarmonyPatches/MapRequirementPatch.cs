using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using SongCore;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema
{
	[HarmonyAfter("com.kyle1413.BeatSaber.SongCore")]
	[HarmonyPatch(typeof(StandardLevelDetailView))]
	[HarmonyPatch("RefreshContent", MethodType.Normal)]
	[UsedImplicitly]
	public class StandardLevelDetailViewRefreshContent
	{

		[UsedImplicitly]
		private static void Postfix(ref IDifficultyBeatmap ____selectedDifficultyBeatmap, ref PlayerData ____playerData,
			ref UnityEngine.UI.Button ____actionButton, ref UnityEngine.UI.Button ____practiceButton)
		{
			//Don't bother checking capability if no video config is available
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

			IDifficultyBeatmap selectedDiff = ____selectedDifficultyBeatmap;
			var diffData = Collections.RetrieveDifficultyData(selectedDiff);
			if (diffData == null || !diffData.additionalDifficultyData._requirements.Any())
			{
				return;
			}

			foreach (var requirement in diffData.additionalDifficultyData._requirements)
			{
				if (requirement != Plugin.CAPABILITY)
				{
					continue;
				}

				Log.Debug("REQUIREMENT FOUND");

				if (PlaybackController.Instance.VideoConfig?.IsPlayable == true || PlaybackController.Instance.VideoConfig?.forceEnvironmentModifications == true)
				{
					Log.Debug("Requirement fulfilled");
				}
				else
				{
					Log.Debug("Requirement not met");
					VideoMenu.instance.LevelDetailMenu.SetText("Video required to play this map!", "Download", Color.red, Color.green);
					____actionButton.interactable = false;
					____practiceButton.interactable = false;
				}
			}
		}
	}
}