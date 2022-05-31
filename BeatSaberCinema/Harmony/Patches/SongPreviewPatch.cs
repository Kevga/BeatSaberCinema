using System;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(SongPreviewPlayer), nameof(SongPreviewPlayer.CrossfadeTo), typeof(AudioClip), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(Action))]
	[UsedImplicitly]
	public class SongPreviewPatch
	{
		[UsedImplicitly]
		public static void Postfix(int ____activeChannel, float ____timeToDefaultAudioTransition,
			SongPreviewPlayer.AudioSourceVolumeController[] ____audioSourceControllers, int ____channelsCount, AudioClip audioClip, float startTime, bool isDefault)
		{
			try
			{
				SongPreviewPlayerController.SetFields(____audioSourceControllers, ____channelsCount, ____activeChannel, audioClip, startTime, ____timeToDefaultAudioTransition, isDefault);
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
		}
	}
}