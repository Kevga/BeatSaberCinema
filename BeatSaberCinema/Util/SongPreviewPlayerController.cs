using System.Linq;
using BS_Utils.Utilities;
using UnityEngine;

namespace BeatSaberCinema
{
	public static class SongPreviewPlayerController
	{
		public static SongPreviewPlayer? SongPreviewPlayer;
		public static AudioSource[]? AudioSources;
		public static AudioSource? ActiveAudioSource;
		private static object[]? _audioSourceControllers;
		private static int _channelCount;
		private static int _activeChannel;
		private static AudioClip? _currentAudioClip;
		public static void Init()
		{
			_audioSourceControllers = null;
			AudioSources = null;
			SongPreviewPlayer = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().Last();
		}

		public static void SetFields(object[] audioSourceControllers, int channelCount, int activeChannel,
				AudioClip? audioClip, float startTime, float timeToDefault)
		{
			if (_audioSourceControllers == null)
			{
				_audioSourceControllers = audioSourceControllers;
				AudioSources = new AudioSource[channelCount];
				for (var i = 0;	i < channelCount; i++)
				{
					AudioSources[i] = audioSourceControllers[i].GetField<AudioSource>("audioSource");
				}
			}

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

			if (AudioSources == null)
			{
				Log.Warn("Audiosources null in when updating playback controller");
				return;
			}

			if (_activeChannel < 0 || _activeChannel > (_channelCount-1))
			{
				Log.Warn($"No SongPreviewPlayer audio channel active ({_activeChannel})");
				return;
			}

			if (_currentAudioClip.name == "LevelCleared")
			{
				Log.Warn($"Ignoring {_currentAudioClip.name} sound");
				return;
			}

			ActiveAudioSource = AudioSources[_activeChannel];
			Log.Debug($"SongPreviewPatch -- channel {_activeChannel} -- startTime {startTime} -- timeRemaining {timeToDefault} -- audioclip {_currentAudioClip.name}");
			if (PlaybackController.Instance != null)
			{
				PlaybackController.Instance.UpdateSongPreviewPlayer(ActiveAudioSource, startTime, timeToDefault);
			}
		}
	}
}