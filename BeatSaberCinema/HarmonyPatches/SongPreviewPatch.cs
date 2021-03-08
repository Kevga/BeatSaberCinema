using HarmonyLib;
using IPA.Utilities;
using JetBrains.Annotations;
using UnityEngine;
// ReSharper disable InconsistentNaming

namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(SongPreviewPlayer), nameof(SongPreviewPlayer.CrossfadeTo))]
	[UsedImplicitly]
	public class SongPreviewPatch
	{
		[UsedImplicitly]
		public static void Postfix(SongPreviewPlayer __instance, int ____activeChannel, float ____timeToDefaultAudioTransition, AudioSource[] ____audioSources,
			AudioClip audioClip, float startTime, float duration)
		{
			if (audioClip == null)
			{
				Log.Debug("SongPreviewPlayer AudioClip was null");
				return;
			}

			if (____activeChannel < 0 || ____activeChannel > (____audioSources.Length-1))
			{
				Log.Debug($"No SongPreviewPlayer audio channel active ({____activeChannel})");
				return;
			}

			if (audioClip.name == "LevelCleared")
			{
				Log.Debug($"Ignoring {audioClip.name} sound");
				return;
			}

			var activeAudioSource = ____audioSources[____activeChannel];
			Log.Debug($"SongPreviewPatch -- channel {____activeChannel} -- startTime {startTime} -- timeRemaining {____timeToDefaultAudioTransition} -- audioclip {audioClip.name}");
			if (PlaybackController.Instance != null)
			{
				PlaybackController.Instance.UpdateSongPreviewPlayer(activeAudioSource, startTime, ____timeToDefaultAudioTransition);
			}
		}
	}
}