using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(MenuLightsManager), nameof(MenuLightsManager.SetColorPreset))]
	[UsedImplicitly]
	public class MenuColor
	{
		public static Color BaseColor;
		//public static MenuLightsPresetSO MenuLightsPreset = null!;
		//public static MenuLightsManager LightManager = null!;

		[UsedImplicitly]
		public static void Postfix(MenuLightsManager __instance, ref MenuLightsPresetSO preset)
		{
			if (preset.lightIdColorPairs.Length > 0)
			{
				BaseColor = preset.lightIdColorPairs[0].baseColor;
			}

			//MenuLightsPreset = preset;
			//LightManager = __instance;
		}
	}
}