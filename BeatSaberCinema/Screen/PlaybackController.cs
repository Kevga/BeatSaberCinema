using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BS_Utils.Gameplay;
using BS_Utils.Utilities;
using UnityEngine;
using UnityEngine.Video;
using ReflectionUtil = IPA.Utilities.ReflectionUtil;

namespace BeatSaberCinema
{
	public class PlaybackController: MonoBehaviour
	{
		public static PlaybackController Instance { get; private set; } = null!;
		private VideoConfig? _currentVideo;
		private IPreviewBeatmapLevel? _currentLevel;
		private CustomVideoPlayer _videoPlayer = null!;
		private SongPreviewPlayer _songPreviewPlayer = null!;
		private AudioSource[] _songPreviewAudioSources = null!;
		private AudioSource? _activeAudioSource;

		public bool IsPreviewPlaying { get; private set; }
		public float PanStereo
		{
			set => _videoPlayer.PanStereo = value;
		}

		public static void Create()
		{
			if (Instance != null)
			{
				return;
			}

			new GameObject("CinemaPlaybackController").AddComponent<PlaybackController>();
		}

		private void Start()
		{
			if (Instance != null)
			{
				Destroy(this);
				return;
			}
			Instance = this;

			_videoPlayer = gameObject.AddComponent<CustomVideoPlayer>();
			_videoPlayer.Player.frameReady += FrameReady;
			_videoPlayer.Player.sendFrameReadyEvents = true;
			BSEvents.gameSceneLoaded += GameSceneLoaded;
			BSEvents.songPaused += PauseVideo;
			BSEvents.songUnpaused += ResumeVideo;
			BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
			BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
			DontDestroyOnLoad(gameObject);

			//The event handler is registered after the event is first fired, so we'll have to call the handler ourselves
			OnMenuSceneLoadedFresh(null);
		}

		public void ShowScreen()
		{
			_videoPlayer.Show();
		}

		public void HideScreen()
		{
			_videoPlayer.Hide();
		}

		public void ShowScreenBody()
		{
			_videoPlayer.ShowScreenBody();
		}

		public void HideScreenBody()
		{
			_videoPlayer.HideScreenBody();
		}

		public void SetMenuPlacement()
		{
			_videoPlayer.SetMenuPlacement();
		}

		public void PauseVideo()
		{
			StopAllCoroutines();
			if (_videoPlayer.IsPlaying && _currentVideo != null)
			{
				_videoPlayer.Pause();
			}
		}

		public void ResumeVideo()
		{
			if (!_videoPlayer.IsPlaying && _currentVideo != null)
			{
				_videoPlayer.Play();
			}
		}

		public void ApplyOffset(int offset)
		{
			if (!_videoPlayer.IsPlaying || _activeAudioSource == null)
			{
				return;
			}

			//Pause the preview audio source and start seeking. Audio Source will be re-enabled after video player draws its next frame
			_videoPlayer.IsSyncing = true;
			_activeAudioSource.Pause();

			//We add a frame extra to account for delay before playback starts again after seeking
			_videoPlayer.Player.time = _activeAudioSource!.time + (_currentVideo!.offset / 1000f) + (_videoPlayer.FrameDuration*1);
		}

		public void FrameReady(VideoPlayer videoPlayer, long frame)
		{
			if (_activeAudioSource == null)
			{
				return;
			}

			var audioSourceTime = _activeAudioSource.time;
			var playerTime = _videoPlayer.Player.time;
			var referenceTime = audioSourceTime + (_currentVideo!.offset / 1000f);
			var error = referenceTime - playerTime;
			var errorAbs = Math.Abs(error);

			if (audioSourceTime == 0 && !_activeAudioSource.isPlaying && IsPreviewPlaying && !_videoPlayer.IsSyncing)
			{
				Plugin.Logger.Debug("Preview AudioSource detected to have stopped playing");
				StopPreview(false);
				VideoMenu.instance.SetupVideoDetails();
				PrepareVideo(_currentVideo);
			}

			//TODO This seems to be broken, jumping far from the reference time. Might be worth it to try to fix it though.
			/*if (errorAbs > 0.1f)
			{
				_videoPlayer.OutOfSyncFrames += 1;
				Plugin.Logger.Debug(("[Out of sync] ")+"Frame: "+frame+" - Player: "+Util.FormatFloat((float) playerTime) + " - AudioSource: " + Util.FormatFloat(audioSourceTime) + " - Error (ms): "+Math.Round(error*1000));
			}
			else
			{
				//Don't go negative
				_videoPlayer.OutOfSyncFrames = Math.Max(0, _videoPlayer.OutOfSyncFrames - 1);
			}

			//Start syncing when enough frames were out of sync. Stop trying after one second.
			if (_videoPlayer.OutOfSyncFrames > 5 && _videoPlayer.OutOfSyncFrames < _videoPlayer.Player.frameRate)
			{
				Plugin.Logger.Debug("Syncing, current error: "+Util.FormatFloat((float) errorAbs)+ " - reference time: "+referenceTime);
				_videoPlayer.Player.timeReference = VideoTimeReference.ExternalTime;
				_videoPlayer.Player.externalReferenceTime = referenceTime;
			}
			else
			{
				_videoPlayer.Player.timeReference = VideoTimeReference.Freerun;
			}*/


			if (!_videoPlayer.IsSyncing)
			{
				return;
			}

			_videoPlayer.IsSyncing = false;
			if (!_activeAudioSource.isPlaying)
			{
				_activeAudioSource.Play();
			}
		}

		public IEnumerator StartPreviewCoroutine()
		{
			if (_currentVideo == null || _currentLevel == null)
			{
				Plugin.Logger.Warn("No video or level selected in OnPreviewAction");
				yield break;
			}
			if (IsPreviewPlaying)
			{
				Plugin.Logger.Debug("Stopping preview");
				StopPreview(true);
			}
			else
			{
				IsPreviewPlaying = true;
				_videoPlayer.IsSyncing = true;
				if (!_videoPlayer.IsPrepared)
				{
					Plugin.Logger.Info("Not Prepped yet");
				}

				//Start the preview at the point the video kicks in
				var startTime = 0f;
				if (_currentVideo.offset < 0)
				{
					Plugin.Logger.Debug("Set preview start time to "+startTime);
					startTime = -_currentVideo.GetOffsetInSec();
				}
				_songPreviewPlayer.CrossfadeTo(_currentLevel.GetPreviewAudioClipAsync(new CancellationToken()).Result, startTime, _currentLevel.songDuration, 0.65f);
				//+1.0 is hard right. only pan "mostly" right, because for some reason the video player audio doesn't
				//pan hard left either. Also, it sounds a bit more comfortable.
				SetAudioSourcePanning(0.85f);
				StartCoroutine(PlayVideoAfterAudioSourceCoroutine(true));
				PanStereo = -1f; // -1 is hard left
				_videoPlayer.Unmute();
			}
		}

		public void StopPreview(bool stopPreviewMusic)
		{
			StopPlayback();
			HideScreen();

			if (stopPreviewMusic && IsPreviewPlaying)
			{
				_songPreviewPlayer.FadeOut();
			}

			IsPreviewPlaying = false;

			SetAudioSourcePanning(0f); //0f is neutral
			_videoPlayer.Mute();
		}

		private void OnMenuSceneLoaded()
		{
			Plugin.Logger.Debug("MenuSceneLoaded");
			_videoPlayer.Stop();
			_videoPlayer.Hide();

			if (_currentVideo != null)
			{
				PrepareVideo(_currentVideo);
			}

			try
			{
				_songPreviewPlayer = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().First();
				_songPreviewAudioSources = ReflectionUtil.GetField<AudioSource[], SongPreviewPlayer>(_songPreviewPlayer,"_audioSources");
			}
			catch (Exception e)
			{
				Plugin.Logger.Debug("SongPreviewPlayer or AudioSources not found: ");
				Plugin.Logger.Warn(e);
			}
		}

		private void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO? scenesTransition)
		{
			OnMenuSceneLoaded();
		}

		public void SetSelectedLevel(IPreviewBeatmapLevel level, VideoConfig? config)
		{
			_videoPlayer.Stop();
			_currentLevel = level;
			_currentVideo = config;
			Plugin.Logger.Debug($"Selected Level: {level.levelID}");

			if (_currentVideo == null)
			{
				return;
			}

			Plugin.Logger.Debug("Preparing video...");
			PrepareVideo(_currentVideo);
		}

		private void GameSceneLoaded()
		{
			StopAllCoroutines();
			Plugin.Logger.Debug("GameSceneLoaded");

			if (!SettingsStore.Instance.PlaybackEnabled || !Plugin.Enabled || BS_Utils.Plugin.LevelData.Mode == Mode.Multiplayer)
			{
				//TODO add screen positioning for MP
				Plugin.Logger.Debug("Plugin disabled");
				HideScreen();
				return;
			}

			if (_currentVideo == null || !_currentVideo.IsPlayable)
			{
				Plugin.Logger.Debug("No video configured or video is not playable");
				return;
			}

			if (_currentVideo.NeedsToSave)
			{
				VideoLoader.SaveVideoConfig(_currentVideo);
			}

			ModifyGameScene();
			SetAudioSourcePanning(0);
			_videoPlayer.Mute();
			StartCoroutine(PlayVideoAfterAudioSourceCoroutine(false));
		}

		private IEnumerator PlayVideoAfterAudioSourceCoroutine(bool preview)
		{
			var audioSource = _activeAudioSource;
			var startTime = 0f;

			if (!preview)
			{
				yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Any());
				var syncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Last();
				audioSource = syncController.audioSource;
			}

			if (audioSource != null)
			{
				yield return new WaitUntil(() => audioSource.isPlaying);
				startTime = audioSource.time;
			}
			else
			{
				Plugin.Logger.Warn("Active AudioSource was null, cannot wait for it to start");
			}

			PlayVideo(startTime);
		}

		private void ModifyGameScene()
		{
			Plugin.Logger.Debug("Loaded environment: "+BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.serializedName);
			_videoPlayer.SetPlacement(_currentVideo?.screenPosition, _currentVideo?.screenRotation, _currentVideo?.screenHeight, _currentVideo?.screenCurvature);

			try
			{
				DefaultSceneModifications();
				VideoConfigSceneModifications();
			}
			catch (Exception e)
			{
				Plugin.Logger.Error(e);
			}

			Plugin.Logger.Debug("Modified environment");
		}

		private void VideoConfigSceneModifications()
		{
			if (_currentVideo?.environment != null && _currentVideo.environment.Length > 0)
			{
				foreach (var environmentModification in _currentVideo.environment)
				{
					Plugin.Logger.Debug($"Modifying {environmentModification.name}");
					var environmentObjectList = Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.name == environmentModification.name && x.activeInHierarchy);

					foreach (var environmentObject in environmentObjectList)
					{
						if (environmentObject == null)
						{
							Plugin.Logger.Warn($"Environment object {environmentModification.name} was not found in the scene. Skipping modifications.");
							continue;
						}

						if (environmentModification.active.HasValue)
						{
							environmentObject.SetActive(environmentModification.active.Value);
						}

						if (environmentModification.position.HasValue)
						{
							environmentObject.gameObject.transform.position = environmentModification.position.Value;
						}

						if (environmentModification.rotation.HasValue)
						{
							environmentObject.gameObject.transform.eulerAngles = environmentModification.rotation.Value;
						}

						if (environmentModification.scale.HasValue)
						{
							environmentObject.gameObject.transform.localScale = environmentModification.scale.Value;
						}
					}
				}
			}
		}

		private void DefaultSceneModifications()
		{
			//FrontLights appear in many environments and need to be removed in all of them
			var frontLights = Resources.FindObjectsOfTypeAll<GameObject>().LastOrDefault(x => x.name == "FrontLights" && x.activeInHierarchy);
			if (frontLights != null)
			{
				frontLights.SetActive(false);
			}

			switch (BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.serializedName)
			{
				case "BigMirrorEnvironment":
				{
					var doubleColorLasers = Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.name.Contains("DoubleColorLaser") && x.activeInHierarchy);
					foreach (var doubleColorLaser in doubleColorLasers)
					{
						var laserName = doubleColorLaser.name;
						if (laserName == "DoubleColorLaser")
						{
							laserName = "DoubleColorLaser (0)";
						}

						var match = Regex.Match(laserName, "^DoubleColorLaser \\(([0-9])\\)$");
						if (!match.Success)
						{
							Plugin.Logger.Debug($"Could not find index of: {laserName}");
							continue;
						}
						var i = int.Parse(match.Groups[1].Value);

						var sign = 1;
						if (i % 2 == 0)
						{
							sign = -sign;
						}

						var shiftBy = 18f * sign;
						var pos = doubleColorLaser.transform.position;
						doubleColorLaser.transform.position = new Vector3(pos.x + shiftBy, pos.y, pos.z);
					}
					break;
				}
				case "BTSEnvironment":
				{
					var centerLight = Resources.FindObjectsOfTypeAll<GameObject>().LastOrDefault(x => x.name == "MagicDoorSprite" && x.activeInHierarchy);
					if (centerLight != null)
					{
						centerLight.SetActive(false);
					}

					//Not optimal, but if we don't deactivate this, it will override the x position set further down
					var movementEffect = Resources.FindObjectsOfTypeAll<GameObject>().LastOrDefault(x => x.name == "PillarsMovementEffect" && x.activeInHierarchy);
					if (movementEffect != null)
					{
						movementEffect.SetActive(false);
					}

					var pillarPairs = Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.name.Contains("PillarPair") && x.activeInHierarchy);
					foreach (var pillarPair in pillarPairs)
					{
						var pillarPairName = pillarPair.name;
						if (pillarPairName == "PillarPair" || pillarPairName == "SmallPillarPair")
						{
							pillarPairName += " (0)";
						}

						var match = Regex.Match(pillarPairName, "PillarPair \\(([0-9])\\)$");
						if (!match.Success)
						{
							Plugin.Logger.Debug($"Could not find index of: {pillarPairName}");
							continue;
						}
						var i = int.Parse(match.Groups[1].Value);

						var children = Resources.FindObjectsOfTypeAll<GameObject>().Where(x =>
						{
							Transform parent;
							return x.name.Contains("Pillar") &&
							       (parent = x.transform.parent) != null &&
							       parent.name == pillarPair.name &&
							       x.activeInHierarchy;
						});

						foreach (var child in children)
						{
							var childPos = child.transform.position;
							var sign = 1;
							var newX = 16f;
							if (child.name == "PillarL")
							{
								sign *= -1;
							}

							newX = (newX + (i * 2.3f)) * sign;
							child.transform.position = new Vector3(newX, childPos.y, childPos.z);
						}

						var pairPos = pillarPair.transform.position;
						pillarPair.transform.position = new Vector3(pairPos.x, pairPos.y - 2f, pairPos.z);
					}

					if (_currentVideo!.screenPosition == null)
					{
						SetScreenDistance(80f);
					}

					break;
				}
				case "OriginsEnvironment":
				{
					var spectrograms = Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.name == "Spectrogram" && x.activeInHierarchy);
					foreach (var spectrogram in spectrograms)
					{
						var pos = spectrogram.transform.position;
						var newX = 12;
						if (pos.x < 0)
						{
							newX *= -1;
						}
						spectrogram.transform.position = new Vector3(newX, pos.y, pos.z);
					}
					break;
				}
				case "KDAEnvironment":
				{
					var construction = Resources.FindObjectsOfTypeAll<GameObject>().LastOrDefault(x => x.name == "Construction" && x.transform.parent.name != "PlayersPlace" && x.activeInHierarchy);
					if (construction != null)
					{
						//Stretch it in the y-axis to get rid of the beam above
						construction.transform.localScale = new Vector3(1, 2, 1);
					}

					var tentacles = Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.name.Contains("Tentacle") && x.activeInHierarchy);
					foreach (var spectrogram in tentacles)
					{
						var pos = spectrogram.transform.position;
						var rot = spectrogram.transform.eulerAngles;
						const int newPosX = 15;
						const int newRotY = -135;
						var sign = 1;
						if (pos.x < 0)
						{
							sign = -1;
						}

						spectrogram.transform.position = new Vector3(newPosX * sign, pos.y, pos.z);
						spectrogram.transform.eulerAngles = new Vector3(rot.x, newRotY * sign, rot.z);
					}

					var verticalLasers = Resources.FindObjectsOfTypeAll<GameObject>().Where(
						x => x.name.Contains("Laser") && !x.name.Contains("RotatingLasersPair") && x.activeInHierarchy);
					foreach (var laser in verticalLasers)
					{
						var pos = laser.transform.position;
						var newX = 10;
						if (pos.x < 0)
						{
							newX *= -1;
						}

						laser.transform.position = new Vector3(newX, pos.y, pos.z);
					}

					var glowLines = Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.name.Contains("GlowTopLine") && x.activeInHierarchy);
					foreach (var glowLine in glowLines)
					{
						var pos = glowLine.transform.position;
						glowLine.transform.position = new Vector3(pos.x, 20f, pos.z);
					}

					break;
				}
				case "RocketEnvironment":
				{
					var cars = Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x.name.Contains("RocketCar") && x.activeInHierarchy);
					foreach (var car in cars)
					{
						var pos = car.transform.position;
						var newX = 16;
						if (pos.x < 0)
						{
							newX *= -1;
						}
						car.transform.position = new Vector3(newX, pos.y, pos.z);
					}

					var arena = Resources.FindObjectsOfTypeAll<GameObject>().LastOrDefault(x => x.name == "RocketArena" && x.activeInHierarchy);
					if (arena != null)
					{
						arena.transform.localScale = new Vector3(1, 2, 1);
					}

					var arenaLight = Resources.FindObjectsOfTypeAll<GameObject>().LastOrDefault(x => x.name == "RocketArenaLight" && x.activeInHierarchy);
					if (arenaLight != null)
					{
						arenaLight.transform.position = new Vector3(0, 23, 42);
						arenaLight.transform.localScale = new Vector3(2.5f, 1, 1);
					}

					var gateLight = Resources.FindObjectsOfTypeAll<GameObject>().LastOrDefault(x => x.name == "RocketGateLight" && x.activeInHierarchy);
					if (gateLight != null)
					{
						gateLight.transform.position = new Vector3(0, -3, 64);
						gateLight.transform.localScale = new Vector3(2.6f, 1, 4.5f);
					}
					break;
				}
			}
		}

		public void SetAudioSourcePanning(float pan)
		{
			try
			{
				// If resetting the panning back to neutral (0f), set all audio sources.
				// Otherwise only change the active channel.
				var activeChannel = ReflectionUtil.GetField<int, SongPreviewPlayer>(_songPreviewPlayer, "_activeChannel");
				if (pan == 0f || activeChannel > _songPreviewAudioSources.Length - 1)
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
					if (_songPreviewAudioSources[activeChannel] == null)
					{
						return;
					}

					_activeAudioSource = _songPreviewAudioSources[activeChannel];
					_activeAudioSource.panStereo = pan;
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.Warn(e);
			}
		}

		private void PlayVideo(float startTime)
		{
			if (_currentVideo == null)
			{
				return;
			}

			_videoPlayer.Show();
			if ((_currentVideo.transparency == null && !SettingsStore.Instance.TransparencyEnabled) ||
			    (_currentVideo.transparency != null && !_currentVideo.transparency.Value))
			{
				_videoPlayer.ShowScreenBody();
			}
			else
			{
				_videoPlayer.HideScreenBody();
			}

			_videoPlayer.OutOfSyncFrames = 0;

			var totalOffset = _currentVideo.GetOffsetInSec();
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

			_videoPlayer.Player.playbackSpeed = songSpeed;
			totalOffset += startTime; //This must happen after song speed adjustment

			Plugin.Logger.Debug($"Total offset: {totalOffset}, startTime: {startTime}, songSpeed: {songSpeed}, player time: {_videoPlayer.Player.time}");

			if (songSpeed < 1f && totalOffset > 0f)
			{
				//Unity crashes if the playback speed is less than 1 and the video time at the start of playback is greater than 0
				Plugin.Logger.Warn("Video playback disabled to prevent Unity crash");
				HideScreen();
				StopPlayback();
				return;
			}


			//Video seemingly always lags behind in the game scene. A fixed offset seems to work well enough
			if (!IsPreviewPlaying)
			{
				totalOffset += 0.08f;
			}
			else
			{
				totalOffset -= 0.08f;
			}


			StopAllCoroutines();
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
					_videoPlayer.Play();
				}
			}
			else
			{
				_videoPlayer.Play();
				if (_activeAudioSource != null)
				{
					_activeAudioSource.Pause();
				}

				_videoPlayer.Player.time = totalOffset;
			}
		}

		private IEnumerator PlayVideoDelayedCoroutine(float delayStartTime)
		{
			Plugin.Logger.Debug("Waiting for "+delayStartTime+" seconds before playing video");
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			_videoPlayer.Player.time = 0;
			var ticksUntilStart = (delayStartTime) * TimeSpan.TicksPerSecond;
			yield return new WaitUntil(() => stopwatch.ElapsedTicks >= ticksUntilStart);
			Plugin.Logger.Debug("Elapsed ms: "+stopwatch.ElapsedMilliseconds);
			_videoPlayer.Play();
			if (_activeAudioSource != null)
			{
				_activeAudioSource.Pause();
			}
		}

		private IEnumerator? _prepareVideoCoroutine;
		public void PrepareVideo(VideoConfig video)
		{
			if(_prepareVideoCoroutine != null)
			{
				StopCoroutine(_prepareVideoCoroutine);
			}

			_videoPlayer.OutOfSyncFrames = 0;
			StopPlayback();
			_prepareVideoCoroutine = PrepareVideoCoroutine(video);
			StartCoroutine(_prepareVideoCoroutine);
		}

		private IEnumerator PrepareVideoCoroutine(VideoConfig video)
		{
			_currentVideo = video;

			_videoPlayer.Pause();
			if (!video.IsPlayable)
			{
				Plugin.Logger.Debug("Video is not downloaded, stopping prepare");
				yield break;
			}

			_videoPlayer.Player.isLooping = video.loop;

			string videoPath;
			if (video.VideoPath == null)
			{
				Plugin.Logger.Debug("Video path was null, stopping prepare");
				yield break;
			}
			yield return videoPath = video.VideoPath;
			Plugin.Logger.Info($"Loading video: {videoPath}");
			_videoPlayer.Pause();
			var videoFileInfo = new FileInfo(videoPath);
			var timeout = new Timeout(3f);
			if (_videoPlayer.Url != videoPath)
			{
				yield return new WaitUntil(() =>
					!Util.IsFileLocked(videoFileInfo) || timeout.HasTimedOut);
				yield return (_videoPlayer.Url = videoPath);
			}

			timeout.Stop();
			if (timeout.HasTimedOut && Util.IsFileLocked(videoFileInfo))
			{
				var exception = new Exception("File Locked");
				Plugin.Logger.Error(exception);
				throw exception;
			}

			_videoPlayer.Prepare();
		}

		public void StopPlayback()
		{
			_videoPlayer.Stop();
		}

		public void SetScreenDistance(float value)
		{
			_videoPlayer.SetScreenDistance(value);
		}
	}
}