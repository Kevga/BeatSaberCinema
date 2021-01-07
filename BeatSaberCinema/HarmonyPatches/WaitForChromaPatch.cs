using System.Collections;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(LightSwitchEventEffect))]
	[HarmonyPatch("Start")]
	[UsedImplicitly]
	internal static class LightSwitchEventEffectStart
	{
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
		[UsedImplicitly]
		private static void Postfix(LightSwitchEventEffect __instance, BeatmapSaveData.BeatmapEventType ____event)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
		{
			__instance.StartCoroutine(WaitThenStart(__instance, ____event));
		}

		private static IEnumerator WaitThenStart(LightSwitchEventEffect instance, BeatmapSaveData.BeatmapEventType eventType)
		{
			//Have to wait two frames, since Chroma waits for one and we have to make sure we run after Chroma.
			//Why does Chroma wait a frame? Who knows.
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			PlaybackController.Instance.ModifyGameScene();
		}
	}
}