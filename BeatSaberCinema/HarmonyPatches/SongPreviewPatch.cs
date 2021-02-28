using HarmonyLib;
using IPA.Utilities;
using JetBrains.Annotations;
using UnityEngine;

namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(SongPreviewPlayer), nameof(SongPreviewPlayer.CrossfadeTo))]
	[UsedImplicitly]
	public class SongPreviewPatch
	{
		[UsedImplicitly]
		public static void Postfix(SongPreviewPlayer __instance, AudioClip audioClip, float startTime, float duration)
		{
			if (audioClip.name == "LevelCleared" || audioClip.name == "Menu")
			{
				Log.Debug($"Ignoring {audioClip.name} sound");
				return;
			}

			var activeChannel = __instance.GetField<int, SongPreviewPlayer>("_activeChannel");
			var activeAudioSource = __instance.GetField<AudioSource[], SongPreviewPlayer>("_audioSources")[activeChannel];
			var timeRemaining = __instance.GetField<float, SongPreviewPlayer>("_timeToDefaultAudioTransition");
			Log.Debug($"SongPreviewPatch -- channel {activeChannel} -- startTime {startTime} -- timeRemaining {timeRemaining} -- audioclip {audioClip.name}");
			if (PlaybackController.Instance != null)
			{
				PlaybackController.Instance.UpdateSongPreviewPlayer(activeAudioSource, startTime, timeRemaining);
			}
		}
	}
}