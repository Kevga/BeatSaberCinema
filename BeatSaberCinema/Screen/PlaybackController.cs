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
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace BeatSaberCinema
{
	public class PlaybackController: MonoBehaviour
	{
		public enum Scene { SoloGameplay, MultiplayerGameplay, Menu, Other }
		private Scene _activeScene = Scene.Other;

		public static PlaybackController Instance { get; private set; } = null!;
		private LightController LightController { get; set; } = null!;
		private IPreviewBeatmapLevel? _currentLevel;
		[NonSerialized]
		public CustomVideoPlayer VideoPlayer = null!;
		private AudioSource? _activeAudioSource;
		private AudioTimeSyncController? _timeSyncController;
		private MainSettingsModelSO? _mainSettingsModel;
		private float _lastKnownAudioSourceTime;
		private float _previewStartTime;
		private float _previewTimeRemaining;
		private bool _previewWaitingForVideoPlayer = true;
		private bool _previewWaitingForPreviewPlayer;
		private DateTime _previewSyncStartTime;
		private DateTime _audioSourceStartTime;
		private float _offsetAfterPrepare;

		public VideoConfig? VideoConfig { get; private set; }

		public bool IsPreviewPlaying { get; private set; }

		public static void Create()
		{
			if (Instance != null)
			{
				return;
			}

			new GameObject("CinemaPlaybackController").AddComponent<PlaybackController>();
		}

		public void Destroy()
		{
			if (Instance == null)
			{
				return;
			}
			Instance.StopPreview(true);

			Destroy(gameObject);
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
			LightController = gameObject.AddComponent<LightController>();
			VideoPlayer.Player.frameReady += FrameReady;
			VideoPlayer.Player.sendFrameReadyEvents = true;
			BSEvents.gameSceneActive += GameSceneActive;
			BSEvents.gameSceneLoaded += GameSceneLoaded;
			BSEvents.songPaused += PauseVideo;
			BSEvents.songUnpaused += ResumeVideo;
			BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
			BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
			VideoLoader.ConfigChanged += OnConfigChanged;
			VideoPlayer.Player.prepareCompleted += OnPrepareComplete;
			Events.DifficultySelected += DifficultySelected;
			DontDestroyOnLoad(gameObject);

			//The event handler is registered after the event is first fired, so we'll have to call the handler ourselves
			OnMenuSceneLoadedFresh(null);
		}

		private void OnDestroy()
		{
			VideoPlayer.Player.frameReady -= FrameReady;
			BSEvents.gameSceneActive -= GameSceneActive;
			BSEvents.gameSceneLoaded -= GameSceneLoaded;
			BSEvents.songPaused -= PauseVideo;
			BSEvents.songUnpaused -= ResumeVideo;
			BSEvents.lateMenuSceneLoadedFresh -= OnMenuSceneLoadedFresh;
			BSEvents.menuSceneLoaded -= OnMenuSceneLoaded;
			VideoLoader.ConfigChanged -= OnConfigChanged;
			VideoPlayer.Player.prepareCompleted -= OnPrepareComplete;
			Events.DifficultySelected -= DifficultySelected;
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
			if (!Plugin.Enabled || VideoPlayer.IsPlaying || VideoConfig == null || !VideoConfig.IsPlayable || (VideoPlayer.VideoEnded && VideoConfig.loop != true))
			{
				return;
			}

			var referenceTime = GetReferenceTime();
			if (referenceTime > 0)
			{
				VideoPlayer.Play();
			}
			else
			{
				StartCoroutine(PlayVideoDelayedCoroutine(-referenceTime));
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
			VideoPlayer.Player.frameReady += PlayerStartedAfterResync;
			Log.Debug("Applying offset: "+offset);
		}

		private void PlayerStartedAfterResync(VideoPlayer player, long frame)
		{
			VideoPlayer.Player.frameReady -= PlayerStartedAfterResync;
			if (_activeAudioSource == null)
			{
				Log.Warn("Active audio source was null in frame ready after resync");
				return;
			}

			VideoPlayer.IsSyncing = false;
			if (!_activeAudioSource.isPlaying)
			{
				_activeAudioSource.Play();
			}
		}

		private float GetReferenceTime(float? referenceTime = null, float? playbackSpeed = null)
		{
			if (_activeAudioSource == null || VideoConfig == null)
			{
				return 0;
			}

			float time;
			if (referenceTime == null && _activeAudioSource.time == 0)
			{
				time = _lastKnownAudioSourceTime;
			}
			else
			{
				time = referenceTime ?? _activeAudioSource.time;
			}
			var speed = playbackSpeed ?? VideoConfig.PlaybackSpeed;
			return (time * speed) + (VideoConfig.offset / 1000f);
		}

		public void ResyncVideo(float? referenceTime = null, float? playbackSpeed = null)
		{
			if (_activeAudioSource == null || VideoConfig == null || !VideoConfig.IsPlayable)
			{
				return;
			}

			var newTime = GetReferenceTime(referenceTime, playbackSpeed);

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

			if (Math.Abs(VideoPlayer.Player.time - newTime) < 0.2f)
			{
				return;
			}

			if (playbackSpeed.HasValue)
			{
				VideoPlayer.PlaybackSpeed = playbackSpeed.Value;
			}
			VideoPlayer.Player.time = newTime;
		}

		public void FrameReady(VideoPlayer videoPlayer, long frame)
		{
			if (_activeAudioSource == null || VideoConfig == null)
			{
				return;
			}

			var audioSourceTime = _activeAudioSource.time;

			if (VideoPlayer.IsFading)
			{
				return;
			}

			var playerTime = VideoPlayer.Player.time;
			var referenceTime = GetReferenceTime();
			if (VideoPlayer.VideoDuration > 0)
			{
				referenceTime %= VideoPlayer.VideoDuration;
			}
			var error = referenceTime - playerTime;

			if (!_activeAudioSource.isPlaying)
			{
				return;
			}

			if (frame % 120 == 0)
			{
				Log.Debug("Frame: " + frame + " - Player: " + Util.FormatFloat((float) playerTime) + " - AudioSource: " +
				          Util.FormatFloat(audioSourceTime) + " - Error (ms): " + Math.Round(error * 1000));
			}

			if (VideoConfig.endVideoAt.HasValue)
			{
				if (referenceTime >= VideoConfig.endVideoAt - 1f)
				{
					var brightness = Math.Max(0f, VideoConfig.endVideoAt.Value - referenceTime);
					VideoPlayer.SetBrightness(brightness);
				}
			}
			else if (referenceTime >= VideoPlayer.Player.length - 1f && VideoConfig.loop != true)
			{
				var brightness = Math.Max(0f, VideoPlayer.Player.length - referenceTime);
				VideoPlayer.SetBrightness((float) brightness);
			}

			if (Math.Abs(audioSourceTime - _lastKnownAudioSourceTime) > 0.3f && VideoPlayer.IsPlaying)
			{
				Log.Debug("Detected AudioSource seek, resyncing...");
				ResyncVideo();
			}

			//Sync if the error exceeds a threshold, but not if the video is close to the looping point
			if (Math.Abs(error) > 0.3f && Math.Abs(VideoPlayer.VideoDuration - playerTime) > 0.5f && VideoPlayer.IsPlaying)
			{
				//Audio can intentionally go out of sync when the level fails for example. Don't resync the video in that case.
				if (_timeSyncController != null && !_timeSyncController.forcedNoAudioSync)
				{
					Log.Debug($"Detected desync (reference {referenceTime}, actual {playerTime}), resyncing...");
					ResyncVideo();
				}
			}

			if (audioSourceTime > 0)
			{
				_lastKnownAudioSourceTime = audioSourceTime;
			}
		}

		private void Update()
		{
			//This is triggered when the level failed
			if (VideoPlayer.IsPlaying && _timeSyncController != null && _activeAudioSource != null && _timeSyncController.forcedNoAudioSync)
			{
				//Slow down video playback in-line with audio playback
				var pitch = _activeAudioSource.pitch;
				VideoPlayer.PlaybackSpeed = pitch;

				//Slowly fade out video player
				VideoPlayer.FadeOut(1f);
			}

			if (Util.IsInEditor())
			{
				if (VideoPlayer.IsPlaying && ((_activeAudioSource != null && !_activeAudioSource.isPlaying) || _activeAudioSource == null))
				{
					PauseVideo();
				}
				else if (!VideoPlayer.IsPlaying && _activeAudioSource != null && _activeAudioSource.isPlaying)
				{
					VideoPlayer.Player.time = GetReferenceTime();
					ResumeVideo();
				}
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
				Log.Debug("Starting preview");
				IsPreviewPlaying = true;

				if (VideoPlayer.IsPlaying)
				{
					StopPlayback();
				}

				if (!VideoPlayer.IsPrepared)
				{
					Log.Debug("Video not prepared yet");
				}

				//Start the preview at the point the video kicks in
				var startTime = 0f;
				if (VideoConfig.offset < 0)
				{
					startTime = -VideoConfig.GetOffsetInSec();
				}

				if (SongPreviewPlayerController.SongPreviewPlayer == null)
				{
					Log.Error("Failed to get reference to SongPreviewPlayer during preview");
					return;
				}

				try
				{
					Log.Debug($"Preview start time: {startTime}, offset: {VideoConfig.GetOffsetInSec()}");
					var audioClip = await VideoLoader.GetAudioClipForLevel(_currentLevel);
					if (audioClip != null)
					{
						SongPreviewPlayerController.SongPreviewPlayer.CrossfadeTo(audioClip, -5f, startTime, _currentLevel.songDuration, null);
					}
					else
					{
						Log.Error("AudioClip for level failed to load");
					}
				}
				catch (Exception e)
				{
					Log.Error(e);
					IsPreviewPlaying = false;
					return;
				}

				//+1.0 is hard right. only pan "mostly" right, because for some reason the video player audio doesn't
				//pan hard left either. Also, it sounds a bit more comfortable.
				SetAudioSourcePanning(0.9f);
				StartCoroutine(PlayVideoAfterAudioSourceCoroutine(true));
				VideoPlayer.PanStereo = -1f; // -1 is hard left
				VideoPlayer.Unmute();
			}
		}

		public void StopPreview(bool stopPreviewMusic)
		{
			if (!IsPreviewPlaying)
			{
				return;
			}
			Log.Debug($"Stopping preview (stop audio source: {stopPreviewMusic}");

			VideoPlayer.FadeOut();
			StopAllCoroutines();

			if (stopPreviewMusic && SongPreviewPlayerController.SongPreviewPlayer != null)
			{
				SongPreviewPlayerController.SongPreviewPlayer.CrossfadeToDefault();
				VideoPlayer.Mute();
			}

			IsPreviewPlaying = false;

			SetAudioSourcePanning(0f); //0f is neutral
			VideoPlayer.Mute();

			VideoMenu.instance.SetButtonState(true);
		}

		private void OnMenuSceneLoaded()
		{
			Log.Debug("MenuSceneLoaded");
			_activeScene = Scene.Menu;
			VideoPlayer.Hide();
			StopAllCoroutines();
			_previewWaitingForPreviewPlayer = true;

			if (VideoConfig != null)
			{
				PrepareVideo(VideoConfig);
			}
		}

		private void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO? scenesTransition)
		{
			OnMenuSceneLoaded();
			_mainSettingsModel = Resources.FindObjectsOfTypeAll<MainSettingsModelSO>().LastOrDefault();
			if (_mainSettingsModel != null)
			{
				VideoPlayer.VolumeScale = _mainSettingsModel.volume.value;
			}

			VideoPlayer.screenController.OnGameSceneLoadedFresh();
		}

		private void OnConfigChanged(VideoConfig? config)
		{
			OnConfigChanged(config, false);
		}

		internal void OnConfigChanged(VideoConfig? config, bool? reloadVideo)
		{
			var previousVideoPath = VideoConfig?.VideoPath;
			VideoConfig = config;

			if (config == null)
			{
				VideoPlayer.Hide();
				return;
			}

			if (!config.IsPlayable && (config.forceEnvironmentModifications == null || config.forceEnvironmentModifications == false))
			{
				return;
			}

			if (_activeScene == Scene.Menu)
			{
				StopPreview(true);
			}
			else
			{
				VideoPlayer.SetPlacement(Placement.CreatePlacementForConfig(config, _activeScene, VideoPlayer.GetVideoAspectRatio()));
				ResyncVideo();
			}

			if (previousVideoPath != config.VideoPath || reloadVideo == true)
			{
				VideoPlayer.Player.prepareCompleted += ConfigChangedPrepareHandler;
				PrepareVideo(config);
			}
			else
			{
				VideoPlayer.LoopVideo(config.loop == true);
				VideoPlayer.screenController.SetShaderParameters(config);
				VideoPlayer.SetBloomIntensity(config.bloom);
			}

			if (config.TransparencyEnabled)
			{
				VideoPlayer.ShowScreenBody();
			}
			else
			{
				VideoPlayer.HideScreenBody();
			}

			if (_activeScene == Scene.SoloGameplay)
			{
				EnvironmentController.VideoConfigSceneModifications(VideoConfig);
			}
		}

		private void ConfigChangedPrepareHandler(VideoPlayer sender)
		{
			sender.prepareCompleted -= ConfigChangedPrepareHandler;
			if (_activeScene == Scene.Menu || _activeAudioSource == null)
			{
				return;
			}

			sender.frameReady += ConfigChangedFrameReadyHandler;
			PlayVideo(_lastKnownAudioSourceTime);
		}

		private void ConfigChangedFrameReadyHandler(VideoPlayer sender, long frameIdx)
		{
			Log.Debug("First frame after config change is ready");
			sender.frameReady -= ConfigChangedFrameReadyHandler;
			if (_activeAudioSource == null)
			{
				return;
			}

			if (!_activeAudioSource.isPlaying)
			{
				VideoPlayer.Pause();
				VideoPlayer.SetBrightness(1f);
			}

			VideoPlayer.UpdateScreenContent();
		}

		public void SetSelectedLevel(IPreviewBeatmapLevel? level, VideoConfig? config)
		{
			_previewWaitingForPreviewPlayer = true;
			_previewWaitingForVideoPlayer = true;

			_currentLevel = level;
			VideoConfig = config;
			Log.Debug($"Selected Level: {level?.levelID ?? "null"}");

			if (VideoConfig == null)
			{
				VideoPlayer.FadeOut();
				StopAllCoroutines();
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
				var coverSprite = await _currentLevel.GetCoverImageAsync(CancellationToken.None);
				VideoPlayer.SetCoverTexture(coverSprite.texture);
				VideoPlayer.FadeIn();
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
		}

		private void DifficultySelected(ExtraSongDataArgs extraSongDataArgs)
		{
			if (VideoConfig == null)
			{
				return;
			}

			var difficultyData = extraSongDataArgs.SelectedDifficultyData;
			var songData = extraSongDataArgs.SongData;

			//If there is any difficulty that has a Cinema suggestion but the current one doesn't, disable playback. The current difficulty most likely has the suggestion missing on purpose.
			//If there are no difficulties that have the suggestion set, play the video. It might be a video added by the user.
			//Otherwise, if the map is WIP, disable playback even when no difficulty has the suggestion, to convince the mapper to add it.
			if (difficultyData?.HasCinema() == false && songData?.HasCinemaInAnyDifficulty() == true)
			{
				VideoConfig.PlaybackDisabledByMissingSuggestion = true;
			}
			else if (VideoConfig.IsWIPLevel && difficultyData?.HasCinema() == false)
			{
				VideoConfig.PlaybackDisabledByMissingSuggestion = true;
			}
			else
			{
				VideoConfig.PlaybackDisabledByMissingSuggestion = false;
			}

			if (VideoConfig.PlaybackDisabledByMissingSuggestion)
			{
				VideoPlayer.FadeOut(0.1f);
			}
			else
			{
				if (!VideoPlayer.IsPlaying)
				{
					StartSongPreview();
				}
			}
		}

		private void GameSceneActive()
		{
			if (Util.IsMultiplayer())
			{
				return;
			}

			//If BSUtils has no level data, we're probably in the tutorial
			if (BS_Utils.Plugin.LevelData.IsSet)
			{
				//Move to the environment scene to be picked up by Chroma
				var sceneName = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.sceneInfo.sceneName;
				var scene = SceneManager.GetSceneByName(sceneName);
				SceneManager.MoveGameObjectToScene(gameObject, scene);
			}

			Log.Debug("Moving to game scene");
		}

		public void GameSceneLoaded()
		{
			StopAllCoroutines();
			Log.Debug("GameSceneLoaded");

			_activeScene = Util.IsMultiplayer() ? Scene.MultiplayerGameplay : Scene.SoloGameplay;

			if (!Plugin.Enabled)
			{
				Log.Debug("Plugin disabled");
				VideoPlayer.Hide();
				return;
			}

			LightController.OnGameSceneLoaded();

			StopPlayback();
			VideoPlayer.Hide();

			if (!Util.IsInEditor())
			{
				if (BS_Utils.Plugin.LevelData.Mode == Mode.None)
				{
					Log.Debug("Level mode is None");
					return;
				}

				var bsUtilsLevel = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.difficultyBeatmap.level;
				if (_currentLevel?.levelID != bsUtilsLevel.levelID)
				{
					var video = VideoLoader.GetConfigForLevel(bsUtilsLevel);
					SetSelectedLevel(bsUtilsLevel, video);
				}
			}

			if (VideoConfig == null || !VideoConfig.IsPlayable)
			{
				Log.Debug("No video configured or video is not playable: "+VideoConfig?.VideoPath);

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

			VideoPlayer.SetPlacement(Placement.CreatePlacementForConfig(VideoConfig, _activeScene, VideoPlayer.GetVideoAspectRatio()));

			//Fixes rough pop-in at the start of the song when transparency is disabled
			if (VideoConfig.TransparencyEnabled)
			{
				VideoPlayer.Show();
				VideoPlayer.ScreenColor = Color.black;
				VideoPlayer.ShowScreenBody();
			}

			SetAudioSourcePanning(0);
			VideoPlayer.Mute();
			StartCoroutine(PlayVideoAfterAudioSourceCoroutine(false));
		}

		private IEnumerator PlayVideoAfterAudioSourceCoroutine(bool preview)
		{
			float startTime;

			if (!preview)
			{
				Log.Debug("Waiting for ATSC to be ready");

				if (Util.IsInEditor())
				{
					//_editorTimeSyncController = Resources.FindObjectsOfTypeAll<BeatmapEditorAudioTimeSyncController>().FirstOrDefault(atsc => atsc.name == "BeatmapEditorAudioTimeSyncController");
					_activeAudioSource = Resources.FindObjectsOfTypeAll<AudioSource>()
						.FirstOrDefault(audioSource => audioSource.name == "SongPreviewAudioSource(Clone)" && audioSource.transform.parent == null);
				}
				else
				{
					yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Any());

					//There can be multiple ATSC behaviors
					if (Util.IsMultiplayer())
					{
						//Hierarchy: MultiplayerLocalActivePlayerController(Clone)/IsActiveObjects/GameplayCore/SongController
						_timeSyncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().FirstOrDefault(atsc => atsc.transform.parent.parent.parent.name.Contains("(Clone)"));
					}
					else
					{
						//Hierarchy: Wrapper/StandardGameplay/GameplayCore/SongController
						_timeSyncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().FirstOrDefault(atsc => atsc.transform.parent.parent.name.Contains("StandardGameplay"));
					}

					if (_timeSyncController == null)
					{
						Log.Warn("Could not find ATSC the usual way. Did the object hierarchy change? Current scene name is "+SceneManager.GetActiveScene().name);

						//This throws an exception if we still don't find the ATSC
						_timeSyncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Last();
						Log.Warn("Selected ATSC: " + _timeSyncController.name);
					}

					_activeAudioSource = _timeSyncController.GetField<AudioSource, AudioTimeSyncController>("_audioSource");
				}
			}

			if (_activeAudioSource != null)
			{
				_lastKnownAudioSourceTime = 0;
				Log.Debug($"Waiting for AudioSource {_activeAudioSource.name} to start playing");
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
				if (SongPreviewPlayerController.AudioSourceControllers == null)
				{
					return;
				}

				// If resetting the panning back to neutral (0f), set all audio sources.
				// Otherwise only change the active channel.
				if (pan == 0f || _activeAudioSource == null)
				{
					foreach (var sourceVolumeController in SongPreviewPlayerController.AudioSourceControllers)
					{
						sourceVolumeController.audioSource.panStereo = pan;
					}
				}
				else
				{
					_activeAudioSource.panStereo = pan;
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
				Log.Warn("VideoConfig null in PlayVideo");
				return;
			}

			VideoPlayer.IsSyncing = false;

			// Always hide screen body in the menu, since the drawbacks of the body being visible are large
			if (VideoConfig.TransparencyEnabled && _activeScene != Scene.Menu)
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
						totalOffset /= (songSpeed * VideoConfig.PlaybackSpeed);
					}
				}
			}

			VideoPlayer.PlaybackSpeed = songSpeed * VideoConfig.PlaybackSpeed;
			totalOffset += startTime; //This must happen after song speed adjustment

			if ((songSpeed * VideoConfig.PlaybackSpeed) < 1f && totalOffset > 0f)
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

			//This fixes an issue where the Unity video player sometimes ignores a change in the .time property if the time is very small and the player is currently playing
			if (Math.Abs(totalOffset) < 0.001f)
			{
				totalOffset = 0;
				Log.Debug("Set very small offset to 0");
			}

			Log.Debug($"Total offset: {totalOffset}, startTime: {startTime}, songSpeed: {songSpeed}, player time: {VideoPlayer.Player.time}");

			StopAllCoroutines();

			if (_activeAudioSource != null && _activeAudioSource.time > 0)
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
					//In menus we don't need to wait, instead the preview player starts earlier
					VideoPlayer.Play();
				}
			}
			else
			{
				VideoPlayer.Play();
				if (!VideoPlayer.Player.isPrepared)
				{
					_audioSourceStartTime = DateTime.Now;
					_offsetAfterPrepare = totalOffset;
				}
				else
				{
					VideoPlayer.Player.time = totalOffset;
				}
			}
		}

		//TODO Using a stopwatch will not work properly when seeking in the map (e.g. IntroSkip, PracticePlugin)
		private IEnumerator PlayVideoDelayedCoroutine(float delayStartTime)
		{
			Log.Debug("Waiting for "+delayStartTime+" seconds before playing video");
			var stopwatch = new Stopwatch();
			stopwatch.Start();
			VideoPlayer.Pause();
			VideoPlayer.Hide();
			VideoPlayer.Player.time = 0;
			var ticksUntilStart = (delayStartTime) * TimeSpan.TicksPerSecond;
			yield return new WaitUntil(() => stopwatch.ElapsedTicks >= ticksUntilStart);
			Log.Debug("Elapsed ms: "+stopwatch.ElapsedMilliseconds);

			if (_activeAudioSource != null && _activeAudioSource.time > 0)
			{
				_lastKnownAudioSourceTime = _activeAudioSource.time;
			}

			VideoPlayer.Play();
		}

		private IEnumerator? _prepareVideoCoroutine;
		public void PrepareVideo(VideoConfig video)
		{
			_previewWaitingForVideoPlayer = true;

			if (_prepareVideoCoroutine != null)
			{
				StopCoroutine(_prepareVideoCoroutine);
			}

			_prepareVideoCoroutine = PrepareVideoCoroutine(video);
			StartCoroutine(_prepareVideoCoroutine);
		}

		private IEnumerator PrepareVideoCoroutine(VideoConfig video)
		{
			VideoConfig = video;

			VideoPlayer.Pause();
			if (!(VideoConfig.DownloadState == DownloadState.Downloaded || VideoConfig.IsStreamable))
			{
				Log.Debug("Video is not downloaded, stopping prepare");
				VideoPlayer.FadeOut();
				yield break;
			}

			VideoPlayer.LoopVideo(video.loop == true);
			VideoPlayer.screenController.SetShaderParameters(video);
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
				var timeout = new Timeout(0.25f);
				if (VideoPlayer.Url != videoPath)
				{
					yield return new WaitUntil(() =>
						!Util.IsFileLocked(videoFileInfo) || timeout.HasTimedOut);
				}

				timeout.Stop();
				if (timeout.HasTimedOut && Util.IsFileLocked(videoFileInfo))
				{
					Log.Warn("Video file locked");
				}
			}

			VideoPlayer.Url = videoPath;
			VideoPlayer.Prepare();
		}

		private void OnPrepareComplete(VideoPlayer player)
		{
			if (_offsetAfterPrepare > 0)
			{
				var offset = (DateTime.Now - _audioSourceStartTime).TotalSeconds + _offsetAfterPrepare;
				Log.Debug($"Adjusting offset after prepare to {offset}");
				VideoPlayer.Player.time = offset;
			}
			_offsetAfterPrepare = 0;

			if (_activeScene != Scene.Menu)
			{
				return;
			}

			_previewWaitingForVideoPlayer = false;
			StartSongPreview();
		}

		public void StopPlayback()
		{
			VideoPlayer.Stop();
			StopAllCoroutines();
		}

		public void SceneTransitionInitCalled()
		{
			Events.InvokeSceneTransitionEvents(VideoConfig);
		}

		public void UpdateSongPreviewPlayer(AudioSource? activeAudioSource, float startTime, float timeRemaining, bool isDefault)
		{
			_activeAudioSource = activeAudioSource;
			_lastKnownAudioSourceTime = 0;
			if (_activeAudioSource == null)
			{
				Log.Debug("Active AudioSource null in SongPreviewPlayer update");
			}

			if (IsPreviewPlaying)
			{
				_previewWaitingForPreviewPlayer = true;
				Log.Debug($"Ignoring SongPreviewPlayer update");
				return;
			}

			if (isDefault)
			{
				StopPreview(true);
				VideoPlayer.FadeOut();
				_previewWaitingForPreviewPlayer = true;

				Log.Debug("SongPreviewPlayer reverting to default loop");
				return;
			}

			//This allows the short preview for the practice offset to play
			if (!_previewWaitingForPreviewPlayer && Math.Abs(timeRemaining - 2.5f) > 0.001f)
			{
				StopPreview(true);
				VideoPlayer.FadeOut();

				Log.Debug("Unexpected SongPreviewPlayer update, ignoring.");
				return;
			}

			if (_activeScene != Scene.Menu)
			{
				return;
			}

			if (_currentLevel != null && _currentLevel.songDuration < startTime)
			{
				Log.Debug("Song preview start time was greater than song duration. Resetting start time to 0");
				startTime = 0;
			}

			_previewStartTime = startTime;
			_previewTimeRemaining = timeRemaining;
			_previewSyncStartTime = DateTime.Now;
			_previewWaitingForPreviewPlayer = false;
			StartSongPreview();
		}

		private void StartSongPreview()
		{
			if (!SettingsStore.Instance.PluginEnabled || VideoConfig == null || !VideoConfig.IsPlayable)
			{
				return;
			}

			if ((_previewWaitingForPreviewPlayer || _previewWaitingForVideoPlayer || IsPreviewPlaying))
			{
				return;
			}

			if (_currentLevel != null && VideoLoader.IsDlcSong(_currentLevel))
			{
				return;
			}

			var delay = DateTime.Now.Subtract(_previewSyncStartTime);
			var delaySeconds = (float) delay.TotalSeconds;

			Log.Debug($"Starting song preview playback with a delay of {delaySeconds}");

			var timeRemaining = _previewTimeRemaining - delaySeconds;
			if (timeRemaining > 1 || _previewTimeRemaining == 0)
			{
				PlayVideo(_previewStartTime + delaySeconds);
			}
			else
			{
				Log.Debug($"Not playing song preview, because delay was too long. Remaining preview time: {_previewTimeRemaining}");
			}
		}
	}
}