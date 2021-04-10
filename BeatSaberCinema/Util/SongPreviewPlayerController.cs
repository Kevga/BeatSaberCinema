using System.Linq;
using UnityEngine;

namespace BeatSaberCinema
{
	public static class SongPreviewPlayerController
	{
		public static SongPreviewPlayer? SongPreviewPlayer;
		public static AudioSource? ActiveAudioSource;
		public static SongPreviewPlayer.AudioSourceVolumeController[]? AudioSourceControllers;
		private static int _channelCount;
		private static int _activeChannel;
		private static AudioClip? _currentAudioClip;
		public static void Init()
		{
			AudioSourceControllers = null;
			SongPreviewPlayer = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().Last();
		}

		public static void SetFields(SongPreviewPlayer.AudioSourceVolumeController[] audioSourceControllers, int channelCount, int activeChannel,
				AudioClip? audioClip, float startTime, float timeToDefault)
		{
			AudioSourceControllers = audioSourceControllers;
			_channelCount = channelCount;
			_activeChannel = activeChannel;
			_currentAudioClip = audioClip;
			UpdatePlaybackController(startTime, timeToDefault);
		}

		private static void UpdatePlaybackController(float startTime, float timeToDefault)
		{
			if (_currentAudioClip == null)
			{
				Log.Warn("SongPreviewPlayer AudioClip was null");
				return;
			}

			if (AudioSourceControllers == null)
			{
				Log.Warn("Audiosources null in when updating playback controller");
				return;
			}

			if (_activeChannel < 0 || _activeChannel > (_channelCount-1))
			{
				Log.Warn($"No SongPreviewPlayer audio channel active ({_activeChannel})");
				return;
			}

			if (_currentAudioClip.name == "LevelCleared" || _currentAudioClip.name == "Credits")
			{
				Log.Debug($"Ignoring {_currentAudioClip.name} sound");
				return;
			}

			ActiveAudioSource = AudioSourceControllers[_activeChannel].audioSource;
			Log.Debug($"SongPreviewPatch -- channel {_activeChannel} -- startTime {startTime} -- timeRemaining {timeToDefault} -- audioclip {_currentAudioClip.name}");
			if (PlaybackController.Instance != null)
			{
				PlaybackController.Instance.UpdateSongPreviewPlayer(ActiveAudioSource, startTime, timeToDefault);
			}
		}
	}
}