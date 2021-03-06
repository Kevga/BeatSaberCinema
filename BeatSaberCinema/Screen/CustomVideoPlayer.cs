﻿using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BS_Utils.Utilities;
using UnityEngine;
using UnityEngine.Video;

namespace BeatSaberCinema
{
	public class CustomVideoPlayer: MonoBehaviour
	{
		public VideoPlayer Player { get; }
		private readonly AudioSource _videoPlayerAudioSource;
		internal ScreenController screenController = null!;
		private readonly Renderer _screenRenderer;
		private readonly EasingController _fadeController;

		private const string MAIN_TEXTURE_NAME = "_MainTex";

		private const float MAX_BRIGHTNESS = 0.92f;
		private readonly Color _screenColorOn = Color.white.ColorWithAlpha(0f) * MAX_BRIGHTNESS;
		private readonly Color _screenColorOff = Color.clear;
		private static readonly int MainTex = Shader.PropertyToID(MAIN_TEXTURE_NAME);
		private bool _waitForFirstFrame;
		private string _currentlyPlayingVideo = "";
		private readonly Stopwatch _firstFrameStopwatch = new Stopwatch();

		private float _correctPlaybackSpeed = 1.0f;
		private const float MAX_VOLUME = 0.5f;
		[NonSerialized] public float VolumeScale = 1.0f;
		private bool _muted = true;
		private bool _bodyVisible;
		private bool _waitingForFadeOut;
		public bool VideoEnded { get; private set; }

		public Color ScreenColor
		{
			get => _screenRenderer.material.color;
			set => _screenRenderer.material.color = value;
		}

		public float PlaybackSpeed
		{
			get => Player.playbackSpeed;
			set
			{
				if (!IsSyncing)
				{
					_correctPlaybackSpeed = value;
				}

				Player.playbackSpeed = value;
			}
		}

		public float VideoDuration => (float) Player.length;
		public float Volume
		{
			set => _videoPlayerAudioSource.volume = value;
		}

		public float PanStereo
		{
			set => _videoPlayerAudioSource.panStereo = value;
		}

		public string Url
		{
			get => Player.url;
			set => Player.url = value;
		}

		public bool IsPlaying => Player.isPlaying;
		public bool IsFading => _fadeController.IsFading;
		public bool IsPrepared => Player.isPrepared;
		[NonSerialized] public bool IsSyncing;

		public CustomVideoPlayer()
		{
			CreateScreen();
			_screenRenderer = screenController.GetRenderer();
			_screenRenderer.material = new Material(GetShader()) {color = _screenColorOff};

			Player = gameObject.AddComponent<VideoPlayer>();
			Player.source = VideoSource.Url;
			Player.renderMode = VideoRenderMode.MaterialOverride;
			Player.targetMaterialProperty = MAIN_TEXTURE_NAME;
			Player.targetMaterialRenderer = _screenRenderer;

			Player.playOnAwake = false;
			Player.waitForFirstFrame = true;
			Player.errorReceived += VideoPlayerErrorReceived;
			Player.prepareCompleted += VideoPlayerPrepareComplete;
			Player.started += VideoPlayerStarted;
			Player.loopPointReached += VideoPlayerFinished;

			//TODO PanStereo does not work as expected with this AudioSource. Panning fully to one side is still slightly audible in the other.
			_videoPlayerAudioSource = gameObject.AddComponent<AudioSource>();
			Player.audioOutputMode = VideoAudioOutputMode.AudioSource;
			Player.SetTargetAudioSource(0, _videoPlayerAudioSource);
			Mute();
			LoopVideo(false);

			_videoPlayerAudioSource.reverbZoneMix = 0f;
			_videoPlayerAudioSource.playOnAwake = false;
			_videoPlayerAudioSource.spatialize = false;

			_fadeController = new EasingController();
			_fadeController.EasingUpdate += FadeControllerUpdate;
			Hide();

			BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
			SetDefaultMenuPlacement();

#if DEBUG
			var shaderWatcher = new FileSystemWatcher();
			var projectDir = Environment.GetEnvironmentVariable("CinemaProjectDir");
			if (projectDir == null)
			{
				return;
			}

			var configPath = projectDir + "\\BeatSaberCinema\\Resources\\bscinema.bundle";
			shaderWatcher.Path = Path.GetDirectoryName(configPath);
			shaderWatcher.Filter = Path.GetFileName(configPath);
			shaderWatcher.EnableRaisingEvents = true;
			shaderWatcher.Changed += ReloadShader;
#endif
		}

		public void OnDestroy()
		{
			BSEvents.menuSceneLoaded -= OnMenuSceneLoaded;
			_fadeController.EasingUpdate -= FadeControllerUpdate;
		}

#if DEBUG
		private void ReloadShader(object sender, FileSystemEventArgs fileSystemEventArgs)
		{
			StartCoroutine(ReloadShaderCoroutine(fileSystemEventArgs.FullPath));
		}

		private IEnumerator ReloadShaderCoroutine(string path)
		{
			var shaderFileInfo = new FileInfo(path);
			var timeout = new Timeout(3f);
			yield return new WaitUntil(() =>
				!Util.IsFileLocked(shaderFileInfo) || timeout.HasTimedOut);
			_screenRenderer.material = new Material(GetShader(path));
			var timeout2 = new Timeout(1f);
			yield return new WaitUntil(() => timeout2.HasTimedOut);
		}
#endif

		private void CreateScreen()
		{
			screenController = new ScreenController();
			screenController.CreateScreen(transform);
			screenController.SetScreensActive(true);
			SetDefaultMenuPlacement();
		}

		private static Shader GetShader(string? path = null)
		{
			AssetBundle myLoadedAssetBundle;
			if (path == null)
			{
				var bundle = UIUtilities.GetResource(Assembly.GetExecutingAssembly(), "BeatSaberCinema.Resources.bscinema.bundle");
				if (bundle == null || bundle.Length == 0)
				{
					Log.Error("GetResource failed");
					return Shader.Find("Hidden/BlitAdd");
				}

				myLoadedAssetBundle = AssetBundle.LoadFromMemory(bundle);
				if (myLoadedAssetBundle == null)
				{
					Log.Error("LoadFromMemory failed");
					return Shader.Find("Hidden/BlitAdd");
				}
			}
			else
			{
				myLoadedAssetBundle = AssetBundle.LoadFromFile(path);
			}

			Shader shader = myLoadedAssetBundle.LoadAsset<Shader>("ScreenShader");
			myLoadedAssetBundle.Unload(false);

			return shader;
		}

		public void ResetPlaybackSpeed()
		{
			Player.playbackSpeed = _correctPlaybackSpeed;
			IsSyncing = false;
		}

		public void FadeControllerUpdate(float value)
		{
			ScreenColor = _screenColorOn * value;
			if (!_muted)
			{
				Volume = MAX_VOLUME * VolumeScale * value;
			}

			if (value >= 1 && _bodyVisible)
			{
				screenController.SetScreenBodiesActive(true);
			}
			else
			{
				screenController.SetScreenBodiesActive(false);
			}

			if (value == 0 && Player.url == _currentlyPlayingVideo && _waitingForFadeOut)
			{
				Stop();
			}
		}

		public void OnMenuSceneLoaded()
		{
			SetDefaultMenuPlacement();
		}

		public void SetDefaultMenuPlacement(float? width = null)
		{
			var placement = Placement.MenuPlacement;
			placement.Width = width ?? placement.Height * (21f/9f);
			SetPlacement(placement);
		}

		public void SetPlacement(Placement placement)
		{
			screenController.SetPlacement(placement);
		}

		private void FirstFrameReady(VideoPlayer player, long frame)
		{
			//This is done because the video screen material needs to be set to white, otherwise no video would be visible.
			//When no video is playing, we want it to be black though to not blind the user.
			//If we set the white color when calling Play(), a few frames of white screen are still visible.
			//So, we wait before the player renders its first frame and then set the color, making the switch invisible.
			if (!_waitForFirstFrame)
			{
				return;
			}

			_waitForFirstFrame = false;
			FadeIn();
			_firstFrameStopwatch.Stop();
			Log.Debug("Delay from Play() to first frame: "+_firstFrameStopwatch.ElapsedMilliseconds+" ms");
			_firstFrameStopwatch.Reset();
			screenController.SetAspectRatio(GetVideoAspectRatio());
			Player.frameReady -= FirstFrameReady;
		}

		public void SetBloomIntensity(float? bloomIntensity)
		{
			screenController.SetBloomIntensity(bloomIntensity);
		}

		internal void LoopVideo(bool loop)
		{
			Player.isLooping = loop;
		}

		public void Show()
		{
			FadeIn(0);
		}

		public void FadeIn(float duration = 0.4f)
		{
			screenController.SetScreensActive(true);
			_waitingForFadeOut = false;
			_fadeController.EaseIn(duration);
		}

		public void Hide()
		{
			FadeOut(0);
		}

		public void FadeOut(float duration = 0.4f)
		{
			_waitingForFadeOut = true;
			_fadeController.EaseOut(duration);
		}

		public void ShowScreenBody()
		{
			_bodyVisible = true;
			if (!_fadeController.IsFading && _fadeController.IsOne)
			{
				screenController.SetScreenBodiesActive(true);
			}
		}

		public void HideScreenBody()
		{
			_bodyVisible = false;
			if (!_fadeController.IsFading)
			{
				screenController.SetScreenBodiesActive(false);
			}
		}

		public void Play()
		{
			Log.Debug("Starting playback, waiting for first frame...");
			_waitingForFadeOut = false;
			_waitForFirstFrame = true;
			_firstFrameStopwatch.Start();
			Player.frameReady += FirstFrameReady;
			Player.Play();
		}

		public void Pause()
		{
			Player.Pause();
		}

		public void Stop()
		{
			Log.Debug("Stopping playback");
			Player.Stop();
			SetStaticTexture(null);
		}

		public void Prepare()
		{
			_waitingForFadeOut = false;
			Player.Prepare();
		}

		public void Update()
		{
			if (Player.isPlaying)
			{
				SetTexture(Player.texture);
			}
		}

		private void SetTexture(Texture? texture)
		{
			_screenRenderer.material.SetTexture(MainTex, texture);
		}

		public void SetCoverTexture(Texture? texture)
		{
			if (texture == null)
			{
				SetTexture(texture);
				return;
			}

			SetStaticTexture(texture);

			var placement = Placement.CoverPlacement;
			var width = ((float) texture.width / texture.height) * placement.Height;
			placement.Width = width;
			SetPlacement(placement);
			FadeIn();
		}

		public void SetStaticTexture(Texture? texture)
		{
			if (texture == null)
			{
				SetTexture(texture);
				return;
			}

			SetTexture(texture);
			var width = ((float) texture.width / texture.height) * Placement.MenuPlacement.Height;
			SetDefaultMenuPlacement(width);
			screenController.SetShaderParameters(null);
		}

		private static void VideoPlayerPrepareComplete(VideoPlayer source)
		{
			Log.Debug("Video player prepare complete");
			var texture = source.texture;
			Log.Debug($"Video resolution: {texture.width}x{texture.height}");
		}

		private void VideoPlayerStarted(VideoPlayer source)
		{
			Log.Debug("Video player started event");
			_currentlyPlayingVideo = source.url;
			_waitingForFadeOut = false;
			VideoEnded = false;
		}

		private void VideoPlayerFinished(VideoPlayer source)
		{
			Log.Debug("Video player loop point event");
			VideoEnded = true;
		}

		private static void VideoPlayerErrorReceived(VideoPlayer source, string message)
		{
			if (message == "Can't play movie []")
			{
				//Expected when preparing null source
				return;
			}
			Log.Error("Video player error: " + message);
		}

		public float GetVideoAspectRatio()
		{
			var texture = Player.texture;
			if (texture != null && texture.width != 0 && texture.height != 0)
			{
				var aspectRatio = (float) texture.width / texture.height;
				return aspectRatio;
			}

			Log.Debug("Using default aspect ratio (texture missing)");
			return 16f / 9f;
		}

		public void Mute()
		{
			_muted = true;
			Volume = 0f;
		}

		public void Unmute()
		{
			_muted = false;
		}

		public void SetSoftParent(Transform? parent)
		{
			if (SettingsStore.Instance.Enable360Rotation)
			{
				screenController.SetSoftParent(parent);
			}
		}
	}
}