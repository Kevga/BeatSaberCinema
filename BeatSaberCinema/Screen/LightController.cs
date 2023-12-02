using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BeatSaberCinema.Patches;
using BS_Utils.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace BeatSaberCinema
{
	public class LightController : MonoBehaviour
	{
		private AsyncGPUReadbackRequest? _readbackRequest;
		private readonly Stopwatch _readbackRequestStopwatch = new Stopwatch();

		private CustomVideoPlayer _customVideoPlayer = null!;
		private GameObject _lightGameObject = null!;
		private DirectionalLight _light = null!;
		private List<RenderTexture> _downscaleTextures = null!;
		private Color _color;
		private MaterialLightWithId? _menuFloorLight;
		private TubeBloomPrePassLight? _groundLaser;
		private BloomPrePassBackgroundColor? _menuBackground;
		private InstancedMaterialLightWithId? _menuFogRing;
		private static readonly Dictionary<string, Color> ColorDictionary = new Dictionary<string, Color>();
		private bool _menuReferencesSet;

		private const int INITIAL_DOWNSCALING_SIZE = 128;
		private const float DIRECTIONAL_LIGHT_INTENSITY_MENU = 3.1f;

		private const float DIRECTIONAL_LIGHT_INTENSITY_GAMEPLAY = 2.0f;

		//TODO: Radius should ideally depend on screen size and maybe distance
		private const int LIGHT_RADIUS = 250;
		private const int LIGHT_X_ROTATION = 15;
		private const float MENU_FLOOR_INTENSITY = 0.7f;
		private const float MENU_FOG_INTENSITY = 0.75f;
		private const float MENU_LIGHT_DARKENING = 0.7f;
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
			Events.LevelSelected += OnLevelSelected;
			BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
			BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
		}

		private void OnDisable()
		{
			_customVideoPlayer.Player.frameReady -= ProcessFrame;
			_customVideoPlayer.stopped -= VideoStopped;
			_customVideoPlayer.FadeController.EasingUpdate -= OnFadeUpdate;
			Events.LevelSelected -= OnLevelSelected;
			BSEvents.menuSceneLoaded -= OnMenuSceneLoaded;
			BSEvents.lateMenuSceneLoadedFresh -= OnMenuSceneLoadedFresh;

			VideoStopped();
		}

		private void OnLevelSelected(LevelSelectedArgs levelSelectedArgs)
		{
			if (_menuReferencesSet)
			{
				return;
			}

			try
			{
				GetMenuReferences();
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
		}

		internal void OnGameSceneLoaded()
		{
			var euler = _lightGameObject.transform.eulerAngles;
			euler.x = LIGHT_X_ROTATION;
			_light.intensity = DIRECTIONAL_LIGHT_INTENSITY_GAMEPLAY;

			switch (Util.GetEnvironmentName())
			{
				case "BillieEnvironment":
					//Tone down lighting on this env a bit, since clouds get pretty bright
					_light.intensity = 1.2f;
					euler.x = 42;
					break;
				case "BTSEnvironment":
					//Same as with Billie, clouds are too bright
					_light.intensity = 1.6f;
					euler.x = 55;
					break;
				case "LizzoEnvironment":
					//Background objects behind player too bright
					_light.intensity = 1f;
					euler.x = 42;
					break;
			}

			_lightGameObject.transform.eulerAngles = euler;
		}

		private void OnMenuSceneLoaded()
		{
			var euler = _lightGameObject.transform.eulerAngles;
			euler.x = LIGHT_X_ROTATION;
			_lightGameObject.transform.eulerAngles = euler;
			_light.intensity = DIRECTIONAL_LIGHT_INTENSITY_MENU;
			ColorDictionary.Clear();
			SaveDefaultColors();
		}

		private void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO scenesTransitionSetupDataSo)
		{
			try
			{
				GetMenuReferences();
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
		}

		internal void Enable()
		{
			_lightGameObject.SetActive(true);
		}

		private void GetMenuReferences()
		{
			_menuFloorLight = Resources.FindObjectsOfTypeAll<MaterialLightWithId>().FirstOrDefault(x => x.gameObject.name == "BasicMenuGround" && x.isActiveAndEnabled);
			_groundLaser = Resources.FindObjectsOfTypeAll<TubeBloomPrePassLight>().FirstOrDefault(x => x.gameObject.name == "GroundLaser" && x.isActiveAndEnabled);
			_menuBackground = Resources.FindObjectsOfTypeAll<BloomPrePassBackgroundColor>().FirstOrDefault(x => x.gameObject.name == "BackgroundColor" && x.isActiveAndEnabled);
			_menuFogRing = Resources.FindObjectsOfTypeAll<InstancedMaterialLightWithId>().FirstOrDefault(x => x.gameObject.name == "MenuFogRing" && x.isActiveAndEnabled);
			_menuReferencesSet = true;
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
			_lightGameObject.transform.forward = -screen.transform.forward;
			var euler = _lightGameObject.transform.eulerAngles;
			euler.x = LIGHT_X_ROTATION;
			_lightGameObject.transform.eulerAngles = euler;

			_light = _lightGameObject.AddComponent<DirectionalLight>();
			_light.radius = LIGHT_RADIUS;
			_light.intensity = DIRECTIONAL_LIGHT_INTENSITY_MENU;
			_light.color = Color.black;
		}

		private void VideoStopped()
		{
			UpdateColor(Color.black);
		}

		private void ProcessFrame(VideoPlayer source, long frameIdx)
		{
			if (_light == null || !source.isPlaying)
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

		private void SaveDefaultColors()
		{
			if (_menuFloorLight != null)
			{
				SetColorName(_menuFloorLight.name, _menuFloorLight.color);
			}

			if (_groundLaser != null)
			{
				SetColorName(_groundLaser.name, _groundLaser.color);
			}
			else
			{
				Log.Debug("Ground laser not found");
			}

			if (_menuFogRing != null)
			{
				SetColorName(_menuFogRing.name, _menuFogRing._color);
			}
			else
			{
				Log.Debug("Menu fog ring not found");
			}

			if (_menuBackground != null)
			{
				SetColorName(_menuBackground.name, _menuBackground.color);
			}
		}

		private void UpdateColor(Color color)
		{
			_color = color;
			_light.color = _color * _customVideoPlayer.ScreenColor;

			if (Util.GetEnvironmentName() != "MainMenu")
			{
				return;
			}

			//Darken the base menu lighting
			if (_menuFloorLight != null && _menuFloorLight.isActiveAndEnabled)
			{
				ColorDictionary.TryGetValue(_menuFloorLight.name, out var baseColor);
				if (baseColor != null && baseColor != Color.clear)
				{
					var darkenedBaseColor = baseColor * (Color.white - (_customVideoPlayer.ScreenColor * MENU_LIGHT_DARKENING));
					darkenedBaseColor.a = 1;
					var colorsToBeCombined = new[] { darkenedBaseColor, (_light.color * MENU_FLOOR_INTENSITY) };
					var finalColor = AddColors(colorsToBeCombined);
					_menuFloorLight.ColorWasSet(finalColor);
				}
				else
				{
					Log.Debug("Base color not found for menu floor light");
					SaveDefaultColors();
				}
			}

			if (_groundLaser != null && _groundLaser.isActiveAndEnabled)
			{
				ColorDictionary.TryGetValue(_groundLaser.name, out var baseColor);
				if (baseColor != null && baseColor != Color.clear)
				{
					var darkenedBaseColor = baseColor * (Color.white - (_customVideoPlayer.ScreenColor * MENU_LIGHT_DARKENING));
					darkenedBaseColor.a = 1;
					var colorsToBeCombined = new[] { darkenedBaseColor, (_light.color * MENU_FOG_INTENSITY) };
					var finalColor = AddColors(colorsToBeCombined);
					_groundLaser.color = finalColor;
				} else
				{
					Log.Debug("Base color not found for ground laser");
					SaveDefaultColors();
				}
			}

			if (_menuFogRing != null && _menuFogRing.isActiveAndEnabled)
			{
				ColorDictionary.TryGetValue(_menuFogRing.name, out var baseColor);
				if (baseColor != null && baseColor != Color.clear)
				{
					var darkenedBaseColor = baseColor * (Color.white - (_customVideoPlayer.ScreenColor * MENU_LIGHT_DARKENING));
					darkenedBaseColor.a = 1;
					var colorsToBeCombined = new[] { darkenedBaseColor, (_light.color * MENU_FOG_INTENSITY) };
					var finalColor = AddColors(colorsToBeCombined);
					_menuFogRing.ColorWasSet(finalColor);
				} else
				{
					Log.Debug("Base color not found for menu fog ring");
					SaveDefaultColors();
				}
			}

			if (_menuBackground != null && _menuBackground.isActiveAndEnabled)
			{
				ColorDictionary.TryGetValue(_menuBackground.name, out var baseColor);
				if (baseColor != null && baseColor != Color.clear)
				{
					var darkenedBaseColor = baseColor * (Color.white - (_customVideoPlayer.ScreenColor * MENU_LIGHT_DARKENING));
					darkenedBaseColor.a = 1;
					_menuBackground.color = darkenedBaseColor;
				} else
				{
					Log.Debug("Base color not found for menu background");
					SaveDefaultColors();
				}
			}
		}

		public static void SetColorName(string name, Color color)
		{
			if (MenuColorPatch.LightIdColorPairs == null)
			{
				return;
			}

			if (ColorDictionary.ContainsKey(name))
			{
				return;
			}

			var pair = MenuColorPatch.LightIdColorPairs.FirstOrDefault(x => x.baseColor == color);

			if (pair != null)
			{
				Log.Debug($"Setting color name {name} to {color}");
				ColorDictionary.Add(name, pair.baseColor);
			}
			else
			{
				Log.Debug($"Could not find color {color} for name {name}");
				ColorDictionary.Add(name, color);
			}
		}

		private static Color AddColors(params Color[] aColors)
		{
			var result = new Color(0, 0, 0, 0);
			result = aColors.Aggregate(result, (current, c) => current + c);
			result.a = 1;
			return result;
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