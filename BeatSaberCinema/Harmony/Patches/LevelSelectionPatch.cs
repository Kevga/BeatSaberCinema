using HarmonyLib;
using JetBrains.Annotations;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
{
	[HarmonyPatch(typeof(LevelCollectionViewController), nameof(LevelCollectionViewController.HandleLevelCollectionTableViewDidSelectLevel))]
	public class LevelSelectionPatch
	{
		[UsedImplicitly]
		public static void Prefix(IPreviewBeatmapLevel level)
		{
			Events.SetSelectedLevel(level);
		}
	}

	[HarmonyPatch(typeof(LevelCollectionViewController), nameof(LevelCollectionViewController.HandleLevelCollectionTableViewDidSelectPack))]
	public class PackSelectionPatch
	{
		[UsedImplicitly]
		public static void Prefix()
		{
			Events.SetSelectedLevel(null);
		}
	}

	[HarmonyPatch(typeof(MainMenuViewController), "DidActivate")]
	public class MainMenuSelectionResetPatch
	{
		[UsedImplicitly]
		public static void Prefix()
		{
			Events.SetSelectedLevel(null);
		}
	}

	[HarmonyPatch(typeof(LobbySetupViewController), "DidActivate")]
	public class MultiplayerMenuSelectionResetPatch
	{
		[UsedImplicitly]
		public static void Prefix()
		{
			Events.SetSelectedLevel(null);
		}
	}
}