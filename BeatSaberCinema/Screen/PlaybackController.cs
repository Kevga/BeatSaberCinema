using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BS_Utils.Gameplay;
using BS_Utils.Utilities;
using IPA.Utilities;
using UnityEngine;
using UnityEngine.Video;

namespace BeatSaberCinema
{
	public class PlaybackController: MonoBehaviour
	{
		private enum Scene { Gameplay, Menu, Other }

		public static PlaybackController Instance { get; private set; } = null!;
		private static GameObject _playbackControllerGameObject = null!;
		private IPreviewBeatmapLevel? _currentLevel;
		[NonSerialized]
		public CustomVideoPlayer VideoPlayer = null!;
		private SongPreviewPlayer _songPreviewPlayer = null!;
		private AudioSource[] _songPreviewAudioSources = null!;
		private AudioSource? _activeAudioSource;
		private float _lastKnownAudioSourceTime;
		private Scene _activeScene = Scene.Other;

		public VideoConfig? VideoConfig { get; private set; }

		public bool IsPreviewPlaying { get; private set; }

		private SongPreviewPlayer? PreviewPlayer
		{
			get
			{
				// ReSharper disable once ConditionIsAlwaysTrueOrFalse
				if (_songPreviewPlayer != null && _songPreviewAudioSources != null)
				{
					return _songPreviewPlayer;
				}

				try
				{
					_songPreviewPlayer = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().Last();
					_songPreviewAudioSources = _songPreviewPlayer.GetField<AudioSource[], SongPreviewPlayer>("_audioSources");
					return _songPreviewPlayer;
				}
				catch (Exception e)
				{
					Log.Debug("SongPreviewPlayer or AudioSources not found: ");
					Log.Warn(e);
				}

				return null;
			}
		}

		private AudioSource? ActivePreviewAudioSource
		{
			get
			{
				if (PreviewPlayer == null)
				{
					Log.Debug("PreviewPlayer is null");
					return null;
				}

				try
				{
					var activeChannel = PreviewPlayer.GetField<int, SongPreviewPlayer>("_activeChannel");
					if (activeChannel == -1)
					{
						return null;
					}

					_activeAudioSource = _songPreviewAudioSources![activeChannel];
					return _activeAudioSource;
				}
				catch (Exception e)
				{
					Log.Warn(e);
				}

				return null;
			}
		}

		public static void Create()
		{
			if (Instance != null)
			{
				return;
			}

			_playbackControllerGameObject = new GameObject("CinemaPlaybackController");
			_playbackControllerGameObject.AddComponent<PlaybackController>();
		}

		public static void Destroy()
		{
			if (Instance == null)
			{
				return;
			}
			Instance.StopPreview(true);

			Destroy(Instance);
			Destroy(_playbackControllerGameObject);
		}

		private void Start()
		{
			if (Instance != null)
			{
				Destroy(this);
				return;
			}
			Instance = this;

			VideoPlayer = gameObject.AddComponent<CustomVideoPlayer>();
			VideoPlayer.Player.frameReady += FrameReady;
			VideoPlayer.Player.sendFrameReadyEvents = true;
			BSEvents.gameSceneLoaded += GameSceneLoaded;
			BSEvents.songPaused += PauseVideo;
			BSEvents.songUnpaused += ResumeVideo;
			BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
			BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
			VideoLoader.ConfigChanged += OnConfigChanged;
			DontDestroyOnLoad(gameObject);

			//The event handler is registered after the event is first fired, so we'll have to call the handler ourselves
			OnMenuSceneLoadedFresh(null);
		}

		private void OnDestroy()
		{
			VideoPlayer.Player.frameReady -= FrameReady;
			BSEvents.gameSceneLoaded -= GameSceneLoaded;
			BSEvents.songPaused -= PauseVideo;
			BSEvents.songUnpaused -= ResumeVideo;
			BSEvents.lateMenuSceneLoadedFresh -= OnMenuSceneLoadedFresh;
			BSEvents.menuSceneLoaded -= OnMenuSceneLoaded;
			VideoLoader.ConfigChanged -= OnConfigChanged;
		}

		public void PauseVideo()
		{
			StopAllCoroutines();
			if (VideoPlayer.IsPlaying && VideoConfig != null)
			{
				VideoPlayer.Pause();
			}
		}

		public void ResumeVideo()
		{
			if (!VideoPlayer.IsPlaying && VideoConfig != null)
			{
				VideoPlayer.Play();
			}
		}

		public void ApplyOffset(int offset)
		{
			if (!VideoPlayer.IsPlaying || _activeAudioSource == null)
			{
				return;
			}

			//Pause the preview audio source and start seeking. Audio Source will be re-enabled after video player draws its next frame
			VideoPlayer.IsSyncing = true;
			_activeAudioSource.Pause();

			ResyncVideo();
			Log.Debug("Applying offset: "+offset);
		}

		public void ResyncVideo()
		{
			if (_activeAudioSource == null || VideoConfig == null)
			{
				return;
			}

			var newTime = _activeAudioSource.time + (VideoConfig.offset / 1000f);

			if (newTime < 0)
			{
				VideoPlayer.Hide();
				StopAllCoroutines();
				StartCoroutine(PlayVideoDelayedCoroutine(-newTime));
			}
			else if (newTime > VideoPlayer.VideoDuration && VideoPlayer.VideoDuration > 0)
			{
				newTime %= VideoPlayer.VideoDuration;
			}

			VideoPlayer.Player.time = newTime;
			Log.Debug("Set time to: " + newTime);
		}

		public void FrameReady(VideoPlayer videoPlayer, long frame)
		{
			if (_activeAudioSource == null)
			{
				return;
			}

			var audioSourceTime = _activeAudioSource.time;
			var playerTime = VideoPlayer.Player.time;
			var referenceTime = audioSourceTime + (VideoConfig!.offset / 1000f);
			if (VideoPlayer.VideoDuration > 0)
			{
				referenceTime %= VideoPlayer.VideoDuration;
			}
			var error = referenceTime - playerTime;

			if (audioSourceTime == 0 && !_activeAudioSource.isPlaying && IsPreviewPlaying && !VideoPlayer.IsSyncing)
			{
				Log.Debug("Preview AudioSource detected to have stopped playing");
				StopPreview(false);
				VideoMenu.instance.SetupVideoDetails();
				PrepareVideo(VideoConfig);
			}

			if (!_activeAudioSource.isPlaying && !VideoPlayer.IsSyncing)
			{
				return;
			}

			if (frame % 120 == 0)
			{
				Log.Debug("Frame: " + frame + " - Player: " + Util.FormatFloat((float) playerTime) + " - AudioSource: " +
				                    Util.FormatFloat(audioSourceTime) + " - Error (ms): " + Math.Round(error * 1000));
			}

			if (VideoConfig.endVideoAt != null)
			{
				if (referenceTime >= VideoConfig.endVideoAt)
				{
					Log.Debug("Reached video endpoint as configured at "+referenceTime);
					VideoPlayer.Pause();
				}
			}

			if (Math.Abs(audioSourceTime - _lastKnownAudioSourceTime) > 0.3f && VideoPlayer.IsPlaying)
			{
				Log.Debug("Detected AudioSource seek, resyncing...");
				ResyncVideo();
			}

			//Sync if the error exceeds a threshold, but not if the video is close to the looping point
			if (Math.Abs(error) > 0.3f && Math.Abs(VideoPlayer.VideoDuration - playerTime) > 0.5f && VideoPlayer.IsPlaying)
			{
				Log.Debug($"Detected desync (reference {referenceTime}, actual {playerTime}), resyncing...");
				ResyncVideo();
			}

			_lastKnownAudioSourceTime = audioSourceTime;

			if (!VideoPlayer.IsSyncing)
			{
				return;
			}

			VideoPlayer.IsSyncing = false;
			if (!_activeAudioSource.isPlaying)
			{
				_activeAudioSource.Play();
			}
		}

		public async void StartPreview()
		{
			if (VideoConfig == null || _currentLevel == null)
			{
				Log.Warn("No video or level selected in OnPreviewAction");
				return;
			}
			if (IsPreviewPlaying)
			{
				Log.Debug("Stopping preview");
				StopPreview(true);
			}
			else
			{
				IsPreviewPlaying = true;
				if (!VideoPlayer.IsPrepared)
				{
					Log.Info("Not Prepped yet");
				}

				//Start the preview at the point the video kicks in
				var startTime = 0f;
				if (VideoConfig.offset < 0)
				{
					Log.Debug("Set preview start time to "+startTime);
					startTime = -VideoConfig.GetOffsetInSec();
				}

				if (PreviewPlayer == null)
				{
					Log.Error("Failed to get reference to SongPreviewPlayer during preview");
					return;
				}

				const float previewPlayerVolume = 0.8f;
				PreviewPlayer.CrossfadeTo(await VideoLoader.GetAudioClipForLevel(_currentLevel), startTime, _currentLevel.songDuration, previewPlayerVolume);
				//+1.0 is hard right. only pan "mostly" right, because for some reason the video player audio doesn't
				//pan hard left either. Also, it sounds a bit more comfortable.
				SetAudioSourcePanning(0.85f);
				StartCoroutine(PlayVideoAfterAudioSourceCoroutine(true));
				VideoPlayer.PanStereo = -1f; // -1 is hard left
				VideoPlayer.Unmute();
			}
		}

		public void StopPreview(bool stopPreviewMusic)
		{
			StopPlayback();
			VideoPlayer.Hide();
			StopAllCoroutines();

			if (stopPreviewMusic && IsPreviewPlaying && PreviewPlayer != null)
			{
				PreviewPlayer.FadeOut();
			}

			IsPreviewPlaying = false;

			SetAudioSourcePanning(0f); //0f is neutral
			VideoPlayer.Mute();
		}

		private void OnMenuSceneLoaded()
		{
			Log.Debug("MenuSceneLoaded");
			_activeScene = Scene.Menu;
			EnvironmentController.Reset();
			VideoPlayer.Stop();
			VideoPlayer.Hide();
			StopAllCoroutines();
			_songPreviewPlayer = null!;

			if (VideoConfig != null)
			{
				PrepareVideo(VideoConfig);
			}
		}

		public AudioSource InstantiateAudioSourceFromPrefab()
		{
			var prefab = PreviewPlayer!.GetField<AudioSource, SongPreviewPlayer>("_audioSourcePrefab");
			return Instantiate(prefab, transform);
		}

		private void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO? scenesTransition)
		{
			OnMenuSceneLoaded();
		}

		private void OnConfigChanged(VideoConfig? config)
		{
			var previousVideoPath = VideoConfig?.VideoPath;
			VideoConfig = config;

			if (config == null)
			{
				VideoPlayer.Stop();
				return;
			}

			if (!config.IsPlayable && (config.forceEnvironmentModifications == null || config.forceEnvironmentModifications == false))
			{
				return;
			}

			if (IsPreviewPlaying)
			{
				StopPreview(true);
			}
			else
			{
				VideoPlayer.SetPlacement(VideoConfig?.screenPosition, VideoConfig?.screenRotation, null, VideoConfig?.screenHeight, VideoConfig?.screenCurvature);
			}

			if (previousVideoPath != config.VideoPath)
			{
				PrepareVideo(config);
			}
			else
			{
				VideoPlayer.Player.isLooping = (config.loop == true);
				VideoPlayer.SetShaderParameters(config);
				VideoPlayer.SetBloomIntensity(config.bloom);
			}

			if ((config.transparency == null && !SettingsStore.Instance.TransparencyEnabled) ||
			    (config.transparency != null && !config.transparency.Value))
			{
				VideoPlayer.ShowScreenBody();
			}
			else
			{
				VideoPlayer.HideScreenBody();
			}

			if (_activeScene == Scene.Gameplay)
			{
				EnvironmentController.VideoConfigSceneModifications(VideoConfig);
			}
		}

		public void SetSelectedLevel(IPreviewBeatmapLevel level, VideoConfig? config)
		{
			VideoPlayer.Stop();
			_currentLevel = level;
			VideoConfig = config;
			Log.Debug($"Selected Level: {level.levelID}");

			if (VideoConfig == null)
			{
				return;
			}

			Log.Debug("Preparing video...");
			PrepareVideo(VideoConfig);
		}

		private async void ShowSongCover()
		{
			if (_currentLevel == null)
			{
				return;
			}

			try
			{
				var coverSprite = await _currentLevel.GetCoverImageAsync(new CancellationToken());
				VideoPlayer.SetStaticTexture(coverSprite.texture);
				VideoPlayer.Show();

				if (!SettingsStore.Instance.TransparencyEnabled)
				{
					VideoPlayer.ShowScreenBody();
				}
				else
				{
					VideoPlayer.HideScreenBody();
				}
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
		}

		private void GameSceneLoaded()
		{
			StopAllCoroutines();
			Log.Debug("GameSceneLoaded");
			_activeScene = Scene.Gameplay;

			if (!SettingsStore.Instance.PluginEnabled || !Plugin.Enabled || BS_Utils.Plugin.LevelData.Mode == Mode.Multiplayer)
			{
				//TODO add screen positioning for MP
				Log.Debug("Plugin disabled");
				VideoPlayer.Hide();
				return;
			}

			if (BS_Utils.Plugin.LevelData.Mode == Mode.None)
			{
				Log.Debug("Level mode is None");
				return;
			}

			if (VideoConfig == null || !VideoConfig.IsPlayable)
			{
				Log.Debug("No video configured or video is not playable");

				if (SettingsStore.Instance.CoverEnabled && (VideoConfig?.forceEnvironmentModifications == null || VideoConfig.forceEnvironmentModifications == false))
				{
					ShowSongCover();
				}
				return;
			}

			if (VideoConfig.NeedsToSave)
			{
				VideoLoader.SaveVideoConfig(VideoConfig);
			}

			VideoPlayer.SetPlacement(VideoConfig?.screenPosition, VideoConfig?.screenRotation, null, VideoConfig?.screenHeight, VideoConfig?.screenCurvature);

			SetAudioSourcePanning(0);
			VideoPlayer.Mute();
			StartCoroutine(PlayVideoAfterAudioSourceCoroutine(false));
		}

		private IEnumerator PlayVideoAfterAudioSourceCoroutine(bool preview)
		{
			float startTime;

			if (!preview)
			{
				yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Any());
				var syncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Last();
				_activeAudioSource = syncController.audioSource;
			}

			if (_activeAudioSource == null)
			{
				_activeAudioSource = ActivePreviewAudioSource;
			}

			if (_activeAudioSource != null)
			{
				yield return new WaitUntil(() => _activeAudioSource.isPlaying);
				startTime = _activeAudioSource.time;
			}
			else
			{
				Log.Warn("Active AudioSource was null, cannot wait for it to start");
				StopPreview(true);
				yield break;
			}

			PlayVideo(startTime);
		}

		public void SetAudioSourcePanning(float pan)
		{
			try
			{
				// ReSharper disable once ConditionIsAlwaysTrueOrFalse
				if (_songPreviewAudioSources == null)
				{
					return;
				}
				var activeAudioSource = ActivePreviewAudioSource;

				// If resetting the panning back to neutral (0f), set all audio sources.
				// Otherwise only change the active channel.
				if (pan == 0f || activeAudioSource == null)
				{
					foreach (var source in _songPreviewAudioSources)
					{
						if (source != null)
						{
							source.panStereo = pan;
						}
					}
				}
				else
				{
					activeAudioSource.panStereo = pan;
				}
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}
		}

		private void PlayVideo(float startTime)
		{
			if (VideoConfig == null)
			{
				return;
			}

			VideoPlayer.Show();
			VideoPlayer.IsSyncing = false;

			if ((VideoConfig.transparency == null && !SettingsStore.Instance.TransparencyEnabled) ||
			    (VideoConfig.transparency != null && !VideoConfig.transparency.Value))
			{
				VideoPlayer.ShowScreenBody();
			}
			else
			{
				VideoPlayer.HideScreenBody();
			}

			var totalOffset = VideoConfig.GetOffsetInSec();
			var songSpeed = 1f;
			if (BS_Utils.Plugin.LevelData.IsSet)
			{
				songSpeed = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.gameplayModifiers.songSpeedMul;

				if (BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData?.practiceSettings != null)
				{
					songSpeed = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.practiceSettings.songSpeedMul;
					if ((totalOffset+startTime) < 0)
					{
						totalOffset /= songSpeed;
					}
				}
			}

			VideoPlayer.Player.playbackSpeed = songSpeed;
			totalOffset += startTime; //This must happen after song speed adjustment

			if (songSpeed < 1f && totalOffset > 0f)
			{
				//Unity crashes if the playback speed is less than 1 and the video time at the start of playback is greater than 0
				Log.Warn("Video playback disabled to prevent Unity crash");
				VideoPlayer.Hide();
				StopPlayback();
				return;
			}

			//Video seemingly always lags behind. A fixed offset seems to work well enough
			if (!IsPreviewPlaying)
			{
				totalOffset += 0.0667f;
			}

			if (VideoConfig.endVideoAt != null && totalOffset > VideoConfig.endVideoAt)
			{
				totalOffset = VideoConfig.endVideoAt.Value;
			}

			//This will fail if the video is not prepared yet
			if (VideoPlayer.VideoDuration > 0)
			{
				totalOffset %= VideoPlayer.VideoDuration;
			}

			Log.Debug($"Total offset: {totalOffset}, startTime: {startTime}, songSpeed: {songSpeed}, player time: {VideoPlayer.Player.time}");

			StopAllCoroutines();

			if (_activeAudioSource != null)
			{
				_lastKnownAudioSourceTime = _activeAudioSource.time;
			}

			if (totalOffset < 0)
			{
				if (!IsPreviewPlaying)
				{
					//Negate the offset to turn it into a positive delay
					StartCoroutine(PlayVideoDelayedCoroutine(-(totalOffset)));
				}
				else
				{
					//In previews we start at the point where the video kicks in
					VideoPlayer.Play();
				}
			}
			else
			{
				VideoPlayer.Play();
				VideoPlayer.Player.time = totalOffset;
			}
		}

		private IEnumerator PlayVideoDelayedCoroutine(float delayStartTime)
		{
			Log.Debug("Waiting for "+delayStartTime+" seconds before playing video");
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			VideoPlayer.Pause();
			VideoPlayer.Player.time = 0;
			var ticksUntilStart = (delayStartTime) * TimeSpan.TicksPerSecond;
			yield return new WaitUntil(() => stopwatch.ElapsedTicks >= ticksUntilStart);
			Log.Debug("Elapsed ms: "+stopwatch.ElapsedMilliseconds);

			if (_activeAudioSource != null)
			{
				_lastKnownAudioSourceTime = _activeAudioSource.time;
			}

			VideoPlayer.Show();
			VideoPlayer.Play();
		}

		private IEnumerator? _prepareVideoCoroutine;
		public void PrepareVideo(VideoConfig video)
		{
			if(_prepareVideoCoroutine != null)
			{
				StopCoroutine(_prepareVideoCoroutine);
			}

			StopPlayback();
			_prepareVideoCoroutine = PrepareVideoCoroutine(video);
			StartCoroutine(_prepareVideoCoroutine);
		}

		private IEnumerator PrepareVideoCoroutine(VideoConfig video)
		{
			VideoConfig = video;

			VideoPlayer.Pause();
			if (!video.IsPlayable)
			{
				Log.Debug("Video is not downloaded, stopping prepare");
				yield break;
			}

			VideoPlayer.Player.isLooping = (video.loop == true);
			VideoPlayer.SetShaderParameters(video);
			VideoPlayer.SetBloomIntensity(video.bloom);

			if (video.VideoPath == null)
			{
				Log.Debug("Video path was null, stopping prepare");
				yield break;
			}
			var videoPath = video.VideoPath;
			Log.Info($"Loading video: {videoPath}");

			if (VideoConfig.IsLocal)
			{
				var videoFileInfo = new FileInfo(videoPath);
				var timeout = new Timeout(3f);
				if (VideoPlayer.Url != videoPath)
				{
					yield return new WaitUntil(() =>
						!Util.IsFileLocked(videoFileInfo) || timeout.HasTimedOut);
				}

				timeout.Stop();
				if (timeout.HasTimedOut && Util.IsFileLocked(videoFileInfo))
				{
					var exception = new Exception("File locked");
					Log.Error(exception);
					throw exception;
				}
			}

			VideoPlayer.Url = videoPath;
			VideoPlayer.Prepare();
		}

		public void StopPlayback()
		{
			VideoPlayer.Stop();
		}

		public void SetScreenDistance(float value)
		{
			VideoPlayer.SetScreenDistance(value);
		}

		public void SceneTransitionInitCalled()
		{
			Events.InvokeSceneTransitionEvents(VideoConfig);
		}
	}
}