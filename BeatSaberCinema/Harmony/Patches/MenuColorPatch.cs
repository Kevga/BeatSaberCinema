using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
{
	[HarmonyPatch(typeof(MenuLightsManager), nameof(MenuLightsManager.SetColorPreset))]
	[UsedImplicitly]
	public class MenuColorPatch
	{
		public static Color BaseColor;

		public static LightIdColorPair[]? LightIdColorPairs;
		//public static MenuLightsPresetSO MenuLightsPreset = null!;
		//public static MenuLightsManager LightManager = null!;

		[UsedImplicitly]
		public static void Postfix(MenuLightsPresetSO preset)
		{
			BaseColor = FallbackColorPatch.DefaultColor;
			if (preset != null && preset.lightIdColorPairs != null && preset.lightIdColorPairs.Length > 0 && preset.lightIdColorPairs[0] != null)
			{
				LightIdColorPairs = preset.lightIdColorPairs;
				BaseColor = preset.lightIdColorPairs[0].baseColor;
			}

			//MenuLightsPreset = preset;
			//LightManager = __instance;
		}
	}

	[HarmonyPatch(typeof(MenuLightsManager), nameof(MenuLightsManager.Start))]
	[UsedImplicitly]
	public class FallbackColorPatch
	{
		public static Color DefaultColor;

		[UsedImplicitly]
		public static void Postfix(MenuLightsManager __instance)
		{
			if (__instance._defaultPreset != null && __instance._defaultPreset.lightIdColorPairs != null && __instance._defaultPreset.lightIdColorPairs.Length > 0 && __instance._defaultPreset.lightIdColorPairs[0] != null)
			{
				DefaultColor = __instance._defaultPreset.lightIdColorPairs[0].baseColor;
			}
		}
	}
}