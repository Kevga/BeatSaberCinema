using HarmonyLib;
using JetBrains.Annotations;

namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(LevelCollectionViewController), nameof(LevelCollectionViewController.HandleLevelCollectionTableViewDidSelectLevel))]
	[UsedImplicitly]
	public class LevelSelectionPatch
	{
		[UsedImplicitly]
		public static void Prefix(IPreviewBeatmapLevel level)
		{
			Events.SetSelectedLevel(level);
		}
	}
}