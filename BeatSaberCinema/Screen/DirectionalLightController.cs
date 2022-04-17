using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace BeatSaberCinema
{
	public class DirectionalLightController: MonoBehaviour
	{
		private AsyncGPUReadbackRequest? _readbackRequest;
		private readonly Stopwatch _readbackRequestStopwatch = new Stopwatch();

		private CustomVideoPlayer _customVideoPlayer = null!;
		private GameObject _lightGameObject = null!;
		private DirectionalLight _light = null!;
		private List<RenderTexture> _downscaleTextures = null!;
		private Color _color;

		private const int INITIAL_DOWNSCALING_SIZE = 128;
		private const float LIGHT_INTENSITY = 1.4f;
		//TODO: Radius should ideally depend on screen size and maybe distance
		private const int LIGHT_RADIUS = 150;
		private const int LIGHT_X_ROTATION = 15;
		private const float MAX_BYTE_AS_FLOAT = byte.MaxValue;

		private void Awake()
		{
			var textureCount = (int) Math.Ceiling(Math.Log(INITIAL_DOWNSCALING_SIZE, 2)) + 1;
			_downscaleTextures = new List<RenderTexture>(textureCount);
			for (var i = 1; i <= textureCount; i++)
			{
				var size = (int) Math.Pow(2, textureCount - i);
				_downscaleTextures.Add(new RenderTexture(size, size, 0));
			}
		}

		private void OnEnable()
		{
			_customVideoPlayer = PlaybackController.Instance.VideoPlayer;
			if (_lightGameObject == null)
			{
				CreateLight();
			}

			_customVideoPlayer.Player.frameReady += ProcessFrame;
			_customVideoPlayer.stopped += VideoStopped;
			_customVideoPlayer.FadeController.EasingUpdate += OnFadeUpdate;
		}

		private void OnDisable()
		{
			_customVideoPlayer.Player.frameReady -= ProcessFrame;
			_customVideoPlayer.stopped -= VideoStopped;
			_customVideoPlayer.FadeController.EasingUpdate -= OnFadeUpdate;

			VideoStopped();
		}

		private void OnFadeUpdate(float f)
		{
			UpdateColor(_color);
		}

		private void CreateLight()
		{
			var screen = _customVideoPlayer.screenController.Screens[0];
			_lightGameObject = new GameObject("CinemaDirectionalLight");
			_lightGameObject.transform.parent = screen.transform;
			_lightGameObject.transform.forward = - screen.transform.forward;
			var euler = _lightGameObject.transform.eulerAngles;
			euler.x = LIGHT_X_ROTATION;
			_lightGameObject.transform.eulerAngles = euler;

			_light = _lightGameObject.AddComponent<DirectionalLight>();
			_light.radius = LIGHT_RADIUS;
			_light.intensity = LIGHT_INTENSITY;
			_light.color = Color.black;
		}

		private void VideoStopped()
		{
			UpdateColor(Color.black);
		}

		private void ProcessFrame(VideoPlayer source, long frameIdx)
		{
			if (_light == null)
			{
				return;
			}

			var lowResTex = Downscale(source.texture);

			// Don't start a new readback until the currently running one is finished
			// Not sure if this is necessary, but it does prevent callbacks arriving out-of-order
			if (_readbackRequest is { done: false })
			{
				return;
			}

			_readbackRequestStopwatch.Restart();
			_readbackRequest = AsyncGPUReadback.Request(lowResTex, 0, req =>
			{
				var pixelData = req.GetData<uint>();

				var byteArray = BitConverter.GetBytes(pixelData[0]);
				var color = new Color(byteArray[0] / MAX_BYTE_AS_FLOAT, byteArray[1] / MAX_BYTE_AS_FLOAT, byteArray[2] / MAX_BYTE_AS_FLOAT);

				UpdateColor(color);
			});
		}

		private void UpdateColor(Color color)
		{
			_color = color;
			_light.color = _color * _customVideoPlayer.ScreenColor;
		}

		private RenderTexture Downscale(Texture tex)
		{
			// Blit the video texture into a texture of size INITIAL_DOWNSCALING_SIZE^2
			Graphics.Blit(tex, _downscaleTextures[0]);

			// Blit into textures of decreasing size with the last one being a single pixel to get the average color
			for (var i = 0; i < _downscaleTextures.Count - 1; i++)
			{
				Graphics.Blit(_downscaleTextures[i], _downscaleTextures[i + 1]);
			}

			return _downscaleTextures[_downscaleTextures.Count - 1];

		}
	}
}