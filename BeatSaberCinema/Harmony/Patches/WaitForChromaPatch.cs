using System.Collections;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
{
	[HarmonyBefore("com.noodle.BeatSaber.ChromaCore", "com.noodle.BeatSaber.Chroma")]
	[HarmonyPatch(typeof(LightSwitchEventEffect), nameof(LightSwitchEventEffect.Start))]
	[UsedImplicitly]
	internal static class LightSwitchEventEffectStart
	{
		[UsedImplicitly]
		private static void Prefix(LightSwitchEventEffect __instance)
		{
			CoroutineStarter.Instance.StartCoroutine(WaitThenStart());
		}

		private static IEnumerator WaitThenStart()
		{
			//Have to wait two frames, since Chroma waits for one and we have to make sure we run after Chroma without directly interacting with it.
			//Chroma probably waits a frame to make sure the lights are all registered before accessing the LightManager.
			//If we run before Chroma, the prop groups will get different IDs than usual due to the changed z-positions.
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();

			//Turns out CustomPlatforms runs even later and undoes some of the scene modifications Cinema does. Waiting for a specific duration is more of a temporary fix.
			//TODO Find a better way to implement this. The problematic coroutine in CustomPlatforms is CustomFloorPlugin.EnvironmentHider+<InternalHideObjectsForPlatform>
			yield return new WaitForSeconds(InstalledMods.CustomPlatforms ? 0.75f : 0.05f);

			EnvironmentController.ModifyGameScene(PlaybackController.Instance.VideoConfig);
		}
	}
}