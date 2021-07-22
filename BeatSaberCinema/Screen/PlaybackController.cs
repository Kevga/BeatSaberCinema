using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BS_Utils.Gameplay;
using BS_Utils.Utilities;
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
		public static GameObject GO = null!;
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
		private bool _previewIgnoreNextUpdate;

		public VideoConfig? VideoConfig { get; private set; }

		public bool IsPreviewPlaying { get; private set; }

		public static void Create()
		{
			if (Instance != null)
			{
				return;
			}

			GO = new GameObject("CinemaPlaybackController");
			GO.AddComponent<PlaybackController>();
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
			if (!VideoPlayer.IsPlaying && VideoConfig != null && (!VideoPlayer.VideoEnded || VideoConfig.loop == true))
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
			_lastKnownAudioSourceTime = audioSourceTime;

			if (VideoPlayer.IsFading)
			{
				return;
			}

			var playerTime = VideoPlayer.Player.time;
			var referenceTime = audioSourceTime + (VideoConfig!.offset / 1000f);
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
				//Audio can intentionally go out of sync when the level fails for example. Don't resync the video in that case.
				if (_timeSyncController != null && !_timeSyncController.forcedNoAudioSync)
				{
					Log.Debug($"Detected desync (reference {referenceTime}, actual {playerTime}), resyncing...");
					ResyncVideo();
				}
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
					Log.Debug("Set preview start time to "+startTime);
					startTime = -VideoConfig.GetOffsetInSec();
				}

				if (SongPreviewPlayerController.SongPreviewPlayer == null)
				{
					Log.Error("Failed to get reference to SongPreviewPlayer during preview");
					return;
				}

				_previewIgnoreNextUpdate = true;
				try
				{
					SongPreviewPlayerController.SongPreviewPlayer.CrossfadeTo(await VideoLoader.GetAudioClipForLevel(_currentLevel), 1f, startTime, _currentLevel.songDuration);
				}
				catch (Exception e)
				{
					Log.Error(e);
					IsPreviewPlaying = false;
					_previewIgnoreNextUpdate = false;
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
			_previewWaitingForPreviewPlayer = false;

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
		}

		private void OnConfigChanged(VideoConfig? config)
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
				VideoPlayer.SetPlacement(new Placement(config, _activeScene, VideoPlayer.GetVideoAspectRatio()));
			}

			if (previousVideoPath != config.VideoPath)
			{
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

		public void SetSelectedLevel(IPreviewBeatmapLevel? level, VideoConfig? config)
		{
			_previewWaitingForPreviewPlayer = true;
			_previewWaitingForVideoPlayer = true;

			_currentLevel = level;
			VideoConfig = config;
			Log.Debug($"Selected Level: {level?.levelID}");

			if (VideoConfig == null)
			{
				VideoPlayer.FadeOut();
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

		private void GameSceneActive()
		{
			if (Util.IsMultiplayer())
			{
				return;
			}

			//Move to the environment scene to be picked up by Chroma
			var sceneName = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.sceneInfo.sceneName;
			var scene = SceneManager.GetSceneByName(sceneName);
			SceneManager.MoveGameObjectToScene(gameObject, scene);
			Log.Debug("Moving to game scene");
		}

		private void GameSceneLoaded()
		{
			StopAllCoroutines();
			Log.Debug("GameSceneLoaded");

			_activeScene = Util.IsMultiplayer() ? Scene.MultiplayerGameplay : Scene.SoloGameplay;

			if (!SettingsStore.Instance.PluginEnabled || !Plugin.Enabled)
			{
				Log.Debug("Plugin disabled");
				VideoPlayer.Hide();
				return;
			}

			StopPlayback();
			VideoPlayer.Hide();

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

			VideoPlayer.SetPlacement(Util.IsMultiplayer() ? Placement.MultiplayerPlacement : new Placement(VideoConfig, _activeScene, VideoPlayer.GetVideoAspectRatio()));

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
				yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Any());
				_timeSyncController = Util.IsMultiplayer() ?
					Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().First(atsc => atsc.transform.parent.parent.parent.name.Contains("(Clone)")) :
					Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Last();

				_activeAudioSource = _timeSyncController.audioSource;
			}

			if (_activeAudioSource != null)
			{
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
						totalOffset /= songSpeed;
					}
				}
			}

			VideoPlayer.PlaybackSpeed = songSpeed;
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

			//This fixes an issue where the Unity video player sometimes ignores a change in the .time property if the time is very small and the player is currently playing
			if (Math.Abs(totalOffset) < 0.001f)
			{
				totalOffset = 0;
				Log.Debug("Set very small offset to 0");
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
					//In menus we don't need to wait, instead the preview player starts earlier
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
			if (!video.IsPlayable)
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
		}

		public void SceneTransitionInitCalled()
		{
			Events.InvokeSceneTransitionEvents(VideoConfig);
		}

		public void UpdateSongPreviewPlayer(AudioSource? activeAudioSource, float startTime, float timeRemaining)
		{
			_activeAudioSource = activeAudioSource;
			if (_activeAudioSource == null)
			{
				Log.Debug("Active AudioSource null in SongPreviewPlayer update");
			}

			if (_previewIgnoreNextUpdate)
			{
				Log.Debug("Ignoring SongPreviewPlayer update");
				_previewIgnoreNextUpdate = false;
				return;
			}

			//This allows the short 3-second-preview for the practice offset to play
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

			_previewStartTime += delaySeconds;
			var timeRemaining = _previewTimeRemaining - delaySeconds;
			if (timeRemaining > 2 || _previewTimeRemaining == 0)
			{
				PlayVideo(_previewStartTime);
			}
			else
			{
				Log.Debug($"Not playing song preview, because delay was too long. Remaining preview time: {_previewTimeRemaining}");
			}
		}
	}
}