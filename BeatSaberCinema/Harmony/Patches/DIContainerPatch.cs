using BeatmapEditor3D;
using HarmonyLib;
using JetBrains.Annotations;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
{
	[HarmonyPatch(typeof(MainSettingsMenuViewControllersInstaller), nameof(MainSettingsMenuViewControllersInstaller.InstallBindings))]
	public class MenuContainerPatch
	{
		[UsedImplicitly]
		private static void Prefix(MainSettingsMenuViewControllersInstaller __instance)
		{
			var container = __instance.Container;

			Plugin.menuContainer = container;
		}
	}

	[HarmonyPatch(typeof(GameplayCoreInstaller), nameof(GameplayCoreInstaller.InstallBindings))]
	public class GameCoreContainerPatch
	{
		[UsedImplicitly]
		private static void Prefix(GameplayCoreInstaller __instance)
		{
			var container = __instance.Container;

			Plugin.gameCoreContainer = container;
		}
	}

	[HarmonyPatch(typeof(BeatmapEditorGameplayInstaller), nameof(BeatmapEditorGameplayInstaller.InstallBindings))]
	public class EditorContainerPatch
	{
		[UsedImplicitly]
		private static void Prefix(BeatmapEditorGameplayInstaller __instance)
		{
			var container = __instance.Container;

			Plugin.gameCoreContainer = container;
		}
	}
}