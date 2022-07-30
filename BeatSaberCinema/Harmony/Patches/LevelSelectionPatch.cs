using System.Linq;
using BeatmapEditor3D;
using BeatmapEditor3D.DataModels;
using HarmonyLib;
using IPA.Utilities;
using JetBrains.Annotations;
using UnityEngine;

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

	[HarmonyPatch(typeof(BeatmapDataModelVersionedLoader), nameof(BeatmapDataModelVersionedLoader.Load))]
	public class EditorSelectionPatch
	{
		[UsedImplicitly]
		public static void Postfix(IBeatmapDataModel ____beatmapDataModel)
		{
			var projectFlowCoordinator = Resources.FindObjectsOfTypeAll<BeatmapProjectFlowCoordinator>().FirstOrDefault();
			var projectManager = projectFlowCoordinator.GetField<BeatmapProjectManager, BeatmapProjectFlowCoordinator>("_beatmapProjectManager");
			var originalPath = projectManager.GetField<string, BeatmapProjectManager>("_originalBeatmapProject");
			Events.SetSelectedLevel(____beatmapDataModel, originalPath);
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