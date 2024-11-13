using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using IPA.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using static BeatSaberCinema.VideoConfig;
using Object = UnityEngine.Object;

namespace BeatSaberCinema
{
	public static class EnvironmentController
	{
		private const float CLONED_OBJECT_Z_OFFSET = 200f;
		private const string CLONED_OBJECT_NAME_SUFFIX = " (CinemaClone)";
		private const string HIDE_CINEMA_SCREEN_OBJECT_NAME = "HideCinemaScreen";

		private static bool _environmentModified;
		private static string _currentEnvironmentName = "MainMenu";
		internal static bool IsScreenHidden { get; private set; }
		private static List<EnvironmentObject>? _environmentObjectList;
		private static IEnumerable<EnvironmentObject> EnvironmentObjects
		{
			get
			{
				if (_environmentObjectList != null && _environmentObjectList.Any())
				{
					return _environmentObjectList;
				}

				//Cache the state of all GameObjects
				_environmentObjectList = new List<EnvironmentObject>(10000);
				var stopwatch = new Stopwatch();
				stopwatch.Start();
				var gameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
				Log.Debug($"Resource call finished after {stopwatch.ElapsedMilliseconds} ms");
				var activeScene = SceneManager.GetActiveScene();
				var currentEnvironmentScene = SceneManager.GetSceneByName(_currentEnvironmentName);
				var pcInitScene = SceneManager.GetSceneByName("PCInit"); //This scene is used by CustomPlatforms
				foreach (var gameObject in gameObjects)
				{
					//Relevant GameObjects are mostly in "GameCore" or the scene of the current environment, so filter out everything else
					if (gameObject.scene != activeScene && gameObject.scene != currentEnvironmentScene && gameObject.scene != pcInitScene)
					{
						continue;
					}

					_environmentObjectList.Add(new EnvironmentObject(gameObject, false));
				}

				stopwatch.Stop();
				Log.Debug($"Created environment object list in {stopwatch.ElapsedMilliseconds} ms, items: {_environmentObjectList.Count}");

				return _environmentObjectList;
			}
		}

		public static void Init()
		{
			SceneManager.activeSceneChanged += SceneChanged;
		}

		public static void Disable()
		{
			SceneManager.activeSceneChanged -= SceneChanged;
			_environmentObjectList?.Clear();
		}

		private static void SceneChanged(Scene arg0, Scene arg1)
		{
			Log.Debug($"Scene changed from {arg0.name} to {arg1.name}");
			var sceneName = arg1.name;
			if (sceneName == "BeatmapLevelEditorWorldUi")
			{
				Reset();
				PlaybackController.Instance.GameSceneLoaded();
			}

			if (sceneName == "MainMenu" || sceneName == "PCInit" || sceneName == "EmptyTransition")
			{
				Reset();
			}
		}

		public static void ModifyGameScene(VideoConfig? videoConfig)
		{
			//Move back to the DontDestroyOnLoad scene
			Object.DontDestroyOnLoad(PlaybackController.Instance);

			if (!Plugin.Enabled || videoConfig == null || Util.IsMultiplayer() ||
			    (!videoConfig.IsPlayable && (videoConfig.forceEnvironmentModifications == null || videoConfig.forceEnvironmentModifications == false)))
			{
				return;
			}

			//Make sure the environment is only modified once, since the trigger for this functions runs multiple times
			if (_environmentModified)
			{
				return;
			}

			_environmentModified = true;
			_currentEnvironmentName = Util.GetEnvironmentName();
			Log.Debug("Loaded environment: "+_currentEnvironmentName);

			var stopwatch = new Stopwatch();
			stopwatch.Start();

			CreateAdditionalScreens(videoConfig);
			PrepareClonedScreens(videoConfig);
			CloneObjects(videoConfig);

			try
			{
				if (videoConfig.disableDefaultModifications == null || videoConfig.disableDefaultModifications.Value == false)
				{
					DefaultSceneModifications(videoConfig);
				}
			}
			catch (Exception e)
			{
				Log.Error(e);
			}

			try
			{
				VideoConfigSceneModifications(videoConfig);
			}
			catch (Exception e)
			{
				Log.Error(e);
			}

			stopwatch.Stop();
			Log.Debug($"Modified environment in {stopwatch.ElapsedMilliseconds} ms");
		}

		private static void Reset()
		{
			if (!PlaybackController.Instance)
			{
				return;
			}

			PlaybackController.Instance.VideoPlayer.screenController.SetScreensActive(true);
			var mainScreen = PlaybackController.Instance.VideoPlayer.screenController.Screens[0];
			mainScreen.gameObject.GetComponent<CustomBloomPrePass>().enabled = true;
			PlaybackController.Instance.LightController.enabled = true;
			IsScreenHidden = false;

			if (PlaybackController.Instance.VideoPlayer.screenController.Screens.Count > 0)
			{
				foreach (var screen in PlaybackController.Instance.VideoPlayer.screenController.Screens.Where(screen => screen.name.Contains("Clone")))
				{
					Object.Destroy(screen);
					Log.Debug("Destroyed screen");
				}

				PlaybackController.Instance.VideoPlayer.screenController.Screens.RemoveRange(1, PlaybackController.Instance.VideoPlayer.screenController.Screens.Count - 1);


				if (InstalledMods.Heck)
				{
					var types = new[] {"Chroma.GameObjectTrackController", "Chroma.Lighting.EnvironmentEnhancement.GameObjectTrackController"};

					foreach (var typeName in types)
					{
						try
						{
							var trackControllerType = ReflectionUtil.FindType("Chroma", typeName);
							if (trackControllerType != null)
							{
								Object.Destroy(mainScreen.GetComponent(trackControllerType));
								Log.Debug($"Destroyed {typeName}");
							}
							else
							{
								Log.Debug($"Failed to find type {typeName}");
							}
						}
						catch (Exception)
						{
							Log.Debug($"Failed to remove {typeName} from screen");
						}
					}
				}
			}

			_environmentModified = false;
			_environmentObjectList?.Clear();
			if (PlaybackController.Instance != null && PlaybackController.Instance.VideoPlayer != null)
			{
				PlaybackController.Instance.VideoPlayer.SetSoftParent(null);

				//Some maps with bad Chroma regex can end up disabling this
				PlaybackController.Instance.LightController.Enable();
			}
		}

		private static void DefaultSceneModifications(VideoConfig? videoConfig)
		{
			//Scuffed way for custom platforms to hide the screen while keeping the video running
			if (EnvironmentObjects.FirstOrDefault(x => x.name == HIDE_CINEMA_SCREEN_OBJECT_NAME) != null)
			{
				PlaybackController.Instance.VideoPlayer.screenController.SetScreensActive(false);
				IsScreenHidden = true;
				/*mainScreen.GetComponent<CustomBloomPrePass>().enabled = false;
				PlaybackController.Instance.LightController.enabled = false;*/
				Log.Info("Hiding video screen due to custom platform");
			}

			//FrontLights appear in many environments and need to be removed in all of them
			var frontLights = EnvironmentObjects.LastOrDefault(x => (x.name == "FrontLights" || x.name == "FrontLight") && x.activeInHierarchy);
			frontLights?.SetActive(false);

			//To make the screen's directional lighting work, we have to disable on of the base game lights. The last one is the least noticable from my testing.
			var directionalLight = EnvironmentObjects.LastOrDefault(x => (x.name == "DirectionalLight" && x.parentName == "CoreLighting"));
			directionalLight?.SetActive(false);

			switch (_currentEnvironmentName)
			{
				case "NiceEnvironment":
				case "BigMirrorEnvironment":
				{
					var doubleColorLasers = EnvironmentObjects.Where(x => x.name.Contains("DoubleColorLaser") && x.activeInHierarchy);
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
							Log.Debug($"Could not find index of: {laserName}");
							continue;
						}
						var i = int.Parse(match.Groups[1].Value);

						var sign = 1;
						if (i % 2 == 0)
						{
							sign = -sign;
						}

						var shiftXBy = 18f * sign;
						var shiftZBy = -28.5f;
						var pos = doubleColorLaser.transform.position;
						doubleColorLaser.transform.position = new Vector3(pos.x + shiftXBy, pos.y, pos.z + shiftZBy );
					}

					//Move rotating lasers BaseL and BaseR from x = -8/+8 to something farther away
					var rotatingLaserPairs = EnvironmentObjects.Where(x => x.name.Contains("RotatingLasersPair") && x.activeInHierarchy);
					foreach (var laser in rotatingLaserPairs)
					{
						foreach (Transform child in laser.transform)
						{
							var pos = child.transform.position;
							var newX = 20;
							if (pos.x < 0)
							{
								newX *= -1;
							}
							child.transform.position = new Vector3(newX, pos.y, pos.z);
						}

					}
					break;
				}
				case "BTSEnvironment":
				{
					var centerLight = EnvironmentObjects.LastOrDefault(x => x.name == "MagicDoorSprite" && x.activeInHierarchy);
					if (centerLight != null)
					{
						centerLight.SetActive(false);
					}

					var pillarPairs = EnvironmentObjects.Where(x => x.name.Contains("PillarPair") && x.activeInHierarchy);
					var movementEffectStartPositions = new List<Vector3>();
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
							Log.Debug($"Could not find index of: {pillarPairName}");
							continue;
						}
						var i = int.Parse(match.Groups[1].Value);

						var children = EnvironmentObjects.Where(x =>
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

							if (child.name == "PillarL")
							{
								sign *= -1;
							}
							var newX = 16f;
							newX = (newX + (i * 2.3f)) * sign;
							child.transform.position = new Vector3(newX, childPos.y, childPos.z);
							if (pillarPairName.Contains("SmallPillarPair"))
							{
								movementEffectStartPositions.Add(new Vector3(newX, 0, 0));
							}
						}

						var pairPos = pillarPair.transform.position;
						var newPos = new Vector3(pairPos.x, pairPos.y - 2f, pairPos.z);
						pillarPair.transform.position = newPos;
					}

					var movementEffect = Resources.FindObjectsOfTypeAll<MovementBeatmapEventEffect>().LastOrDefault(x => x.name == "PillarsMovementEffect");
					if (movementEffect != null)
					{
						movementEffectStartPositions.Reverse();
						movementEffect.SetField("_startLocalPositions", movementEffectStartPositions.ToArray());
					}
					else
					{
						Log.Warn("BTS movement effect not found");
					}
					break;
				}
				case "OriginsEnvironment":
				{
					var spectrograms = EnvironmentObjects.Where(x => x.name == "Spectrogram" && x.activeInHierarchy);
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
					var construction = EnvironmentObjects.LastOrDefault(x => x.name == "Construction" && x.transform.parent.name != "PlayersPlace" && x.activeInHierarchy);
					if (construction != null)
					{
						//Stretch it in the y-axis to get rid of the beam above
						construction.transform.localScale = new Vector3(1, 2, 1);
					}

					var tentacles = EnvironmentObjects.Where(x => x.name.Contains("Tentacle") && x.activeInHierarchy);
					foreach (var tentacle in tentacles)
					{
						var pos = tentacle.transform.position;
						var rot = tentacle.transform.eulerAngles;
						const int newPosX = 15;
						const int newRotY = -135;
						var sign = 1;
						if (pos.x < 0)
						{
							sign = -1;
						}

						tentacle.transform.position = new Vector3(newPosX * sign, pos.y, pos.z);
						tentacle.transform.eulerAngles = new Vector3(rot.x, newRotY * sign, rot.z);
					}

					var verticalLasers = EnvironmentObjects.Where(
						x => x.name.Contains("Laser") && !x.name.Contains("RotatingLasersPair") && x.activeInHierarchy);
					foreach (var laser in verticalLasers)
					{
						var pos = laser.transform.position;
						var newX = 10;
						if (pos.x < 0)
						{
							newX *= -1;
						}

						var newZ = pos.z;
						laser.transform.position = new Vector3(newX, pos.y, newZ);
					}

					var glowLines = EnvironmentObjects.Where(x => x.name.Contains("GlowTopLine") && x.activeInHierarchy);
					foreach (var glowLine in glowLines)
					{
						var pos = glowLine.transform.position;
						glowLine.transform.position = new Vector3(pos.x, 20f, pos.z);
					}

					//Move rotating lasers BaseL and BaseR farther away
					var rotatingLaserPairs = EnvironmentObjects.Where(x => x.name.Contains("RotatingLasersPair") && x.activeInHierarchy);
					foreach (var laser in rotatingLaserPairs)
					{
						foreach (Transform child in laser.transform)
						{
							var pos = child.transform.position;
							var newX = 18;
							if (pos.x < 0)
							{
								newX *= -1;
							}
							child.transform.position = new Vector3(newX, pos.y, pos.z);
						}

					}

					break;
				}
				case "RocketEnvironment":
				{
					var cars = EnvironmentObjects.Where(x => x.name.Contains("RocketCar") && x.activeInHierarchy);
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

					var arena = EnvironmentObjects.LastOrDefault(x => x.name == "RocketArena" && x.activeInHierarchy);
					if (arena != null)
					{
						arena.transform.localScale = new Vector3(2.38f, 2, 1);
					}

					var arenaLight = EnvironmentObjects.LastOrDefault(x => x.name == "RocketArenaLight" && x.activeInHierarchy);
					if (arenaLight != null)
					{
						arenaLight.transform.position = new Vector3(0, 5.8f, 42.4f);
						arenaLight.transform.localScale = new Vector3(2.38f, 1, 1);
						arenaLight.transform.eulerAngles = new Vector3(90, 180, 0);
					}

					var gateLight = EnvironmentObjects.LastOrDefault(x => x.name == "RocketGateLight" && x.activeInHierarchy);
					if (gateLight != null)
					{
						gateLight.transform.position = new Vector3(0, -3, 64);
						gateLight.transform.localScale = new Vector3(2.6f, 1, 4.5f);
					}
					break;
				}
				case "DragonsEnvironment":
				{
					var spectrograms = EnvironmentObjects.Where(x => x.name == "Spectrogram" && x.activeInHierarchy);
					foreach (var spectrogram in spectrograms)
					{
						var pos = spectrogram.transform.position;
						var newX = 16;
						if (pos.x < 0)
						{
							newX *= -1;
						}
						spectrogram.transform.position = new Vector3(newX, pos.y, pos.z);
					}

					//Move rotating lasers BaseL and BaseR from x = -8/+8 to something farther away
					var rotatingLaserPairs = EnvironmentObjects.Where(x => x.name.Contains("RotatingLasersPair") && x.activeInHierarchy);
					foreach (var laser in rotatingLaserPairs)
					{
						foreach (Transform child in laser.transform)
						{
							var pos = child.transform.position;
							var newX = 20;
							if (pos.x < 0)
							{
								newX *= -1;
							}
							child.transform.position = new Vector3(newX, pos.y, pos.z);
						}

					}

					var topConstructionParts = EnvironmentObjects.Where(x => x.name.Contains("TopConstruction") && x.activeInHierarchy);
					foreach (var topConstruction in topConstructionParts)
					{
						var pos = topConstruction.transform.position;
						topConstruction.transform.position = new Vector3(pos.x, 27.0f, pos.z);
					}

					var hallConstruction = EnvironmentObjects.LastOrDefault(x => x.name == "HallConstruction" && x.activeInHierarchy);
					if (hallConstruction != null)
					{
						var pos = hallConstruction.transform.position;
						hallConstruction.transform.position = new Vector3(pos.x, 22.0f, pos.z);
					}

					var trackLaneRings = EnvironmentObjects.Where(x => x.name.Contains("PanelsTrackLaneRing") && x.activeInHierarchy);
					foreach (var ring in trackLaneRings)
					{
						ring.SetActive(false);
					}
					break;
				}
				case "Dragons2Environment":
				{
					var directionalLightLeft = EnvironmentObjects.LastOrDefault(x => (x.name == "Left" && x.parentName == "CoreLighting"));
					directionalLightLeft?.SetActive(false);
					var directionalLightRight = EnvironmentObjects.LastOrDefault(x => (x.name == "Right" && x.parentName == "CoreLighting"));
					directionalLightRight?.SetActive(false);

					var spectrograms = EnvironmentObjects.Where(x => x.name == "Spectrogram" && x.activeInHierarchy);
					foreach (var spectrogram in spectrograms)
					{
						var pos = spectrogram.transform.position;
						var newX = 18;
						var newYRotation = 10;
						if (pos.x < 0)
						{
							newX *= -1;
							newYRotation = 180 - newYRotation;
						}
						spectrogram.transform.position = new Vector3(newX, pos.y, pos.z);
						spectrogram.transform.eulerAngles = new Vector3(0, newYRotation, 0);
					}

					var hallConstruction = EnvironmentObjects.LastOrDefault(x => x.name == "HallConstruction" && x.activeInHierarchy);
					if (hallConstruction != null)
					{
						var pos = hallConstruction.transform.position;
						hallConstruction.transform.position = new Vector3(pos.x, 17.2f, pos.z);
						hallConstruction.transform.localScale = new Vector3(1f, 0.7f, 1f);
					}

					var trackLaneRings = EnvironmentObjects.Where(x => x.name.Contains("PanelsTrackLaneRing") && x.activeInHierarchy);
					foreach (var ring in trackLaneRings)
					{
						ring.SetActive(false);
					}
					break;
				}
				case "LinkinParkEnvironment":
				{
					var logo = EnvironmentObjects.LastOrDefault(x =>
					{
						Transform parent;
						return x.name == "Logo" &&
						       (parent = x.transform.parent) != null &&
						       parent.name == "Environment" &&
						       x.activeInHierarchy;
					});
					if (logo != null)
					{
						logo.SetActive(false);
					}

					var environmentScale = new Vector3(4f, 3f, 3f);
					var invertedScale = new Vector3(1/environmentScale.x, 1/environmentScale.y, 1/environmentScale.z);

					var environment = EnvironmentObjects.LastOrDefault(x => x.name == "Environment" && x.activeInHierarchy);
					if (environment != null)
					{
						environment.transform.localScale = environmentScale;
					}

					var trackConstruction = EnvironmentObjects.LastOrDefault(x => x.name == "TrackConstruction" && x.activeInHierarchy);
					if (trackConstruction != null)
					{
						trackConstruction.transform.position = new Vector3(0.9f, 0f, 106.5f);
						trackConstruction.transform.localScale = invertedScale;
					}

					var trackMirror = EnvironmentObjects.LastOrDefault(x => x.name == "TrackMirror" && x.activeInHierarchy);
					if (trackMirror != null)
					{
						trackMirror.transform.position = new Vector3(0.3f, 0f, 6.55f);
						trackMirror.transform.localScale = invertedScale;
					}

					var trackShadow = EnvironmentObjects.LastOrDefault(x => x.name == "TrackShadow" && x.activeInHierarchy);
					if (trackShadow != null)
					{
						trackShadow.transform.position = new Vector3(0f, -0.3f, 126.1f);
						trackShadow.transform.localScale = invertedScale;
					}

					var playersPlace = EnvironmentObjects.LastOrDefault(x => x.name == "PlayersPlace" && x.activeInHierarchy);
					if (playersPlace != null)
					{
						playersPlace.transform.localScale = invertedScale;
					}

					var playersPlaceShadow = EnvironmentObjects.LastOrDefault(x => x.name == "PlayersPlaceShadow" && x.activeInHierarchy);
					if (playersPlaceShadow != null)
					{
						playersPlaceShadow.transform.localScale = invertedScale;
					}

					var hud = EnvironmentObjects.LastOrDefault(x => x.name == "NarrowGameHUD" && x.activeInHierarchy);
					if (hud != null)
					{
						hud.transform.localScale = invertedScale;
					}
					break;
				}
				case "KaleidoscopeEnvironment":
				{
					var construction = EnvironmentObjects.LastOrDefault(x => x.name == "Construction" && x.transform.parent.name != "PlayersPlace" && x.activeInHierarchy);
					construction?.SetActive(false);
					var trackMirror = EnvironmentObjects.LastOrDefault(x => x.name == "TrackMirror" && x.activeInHierarchy);
					trackMirror?.SetActive(false);
					const float coneOffset = 2.5f;
					var evenCones = EnvironmentObjects.Where(x => x.name == "Cone" && x.transform.parent.name == "ConeRing(Clone)" && x.activeInHierarchy);
					foreach (var glowLine in evenCones)
					{
						var localPos = glowLine.transform.localPosition;
						glowLine.transform.localPosition = new Vector3(localPos.x, localPos.y + coneOffset, localPos.z);
					}
					var oddCones = EnvironmentObjects.Where(x => x.name == "Cone (1)" && x.transform.parent.name == "ConeRing(Clone)" && x.activeInHierarchy);
					foreach (var glowLine in oddCones)
					{
						var localPos = glowLine.transform.localPosition;
						glowLine.transform.localPosition = new Vector3(localPos.x, localPos.y - coneOffset, localPos.z);
					}
					break;
				}
				case "GlassDesertEnvironment":
				{
					var coreHUDController = Resources.FindObjectsOfTypeAll<CoreGameHUDController>().LastOrDefault(x => x.isActiveAndEnabled);
					if (coreHUDController != null)
					{
						PlaybackController.Instance.VideoPlayer.SetSoftParent(coreHUDController.transform);
					}

					break;
				}
				case "InterscopeEnvironment":
				{
					//Not full support for this environment (not whitelisted)
					//These changes just make it look passable when using environment overrides.

					var ceilingFront = EnvironmentObjects.LastOrDefault(x => x.name == "Plane (1)" && x.activeInHierarchy);
					ceilingFront?.SetActive(false);

					var ceilingBack = EnvironmentObjects.LastOrDefault(x => x.name == "Plane (4)" && x.activeInHierarchy);
					ceilingBack?.SetActive(false);

					var topLights = EnvironmentObjects.Where(x => x.name.Contains("NeonTop") && x.activeInHierarchy);
					foreach (var light in topLights)
					{
						light.SetActive(false);
					}
					break;
				}
				case "CrabRaveEnvironment":
				case "MonstercatEnvironment":
				{
					var smallRings = EnvironmentObjects.Where(x => x.name == "SmallTrackLaneRing(Clone)" && x.activeInHierarchy);
					foreach (var ring in smallRings)
					{
						ring.transform.localScale = new Vector3(3f, 3f, 1f);
					}

					var glowLineL = EnvironmentObjects.LastOrDefault(x => x.name == "GlowLineL" && x.activeInHierarchy);
					if (glowLineL != null)
					{
						glowLineL.transform.position = new Vector3(-10f, -1.5f, 9.5f);
					}

					var glowLineR = EnvironmentObjects.LastOrDefault(x => x.name == "GlowLineR" && x.activeInHierarchy);
					if (glowLineR != null)
					{
						glowLineR.transform.position = new Vector3(10f, -1.5f, 9.5f);
					}

					var glowLineL2 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowLineL (1)" && x.activeInHierarchy);
					if (glowLineL2 != null)
					{
						glowLineL2.transform.position = new Vector3(-12f, 1.5f, -20f);
					}

					var glowLineR2 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowLineR (1)" && x.activeInHierarchy);
					if (glowLineR2 != null)
					{
						glowLineR2.transform.position = new Vector3(12f, 1.5f, -20f);
					}

					var monstercatLogo = EnvironmentObjects.Where(x => x.name.Contains("MonstercatLogo") && x.activeInHierarchy);
					foreach (var logo in monstercatLogo)
					{
						logo.SetActive(false);
					}

					var glowTopLines = EnvironmentObjects.Where(x => x.name.Contains("GlowTopLine") && x.activeInHierarchy);
					foreach (var glowTopLine in glowTopLines)
					{
						glowTopLine.transform.localScale = new Vector3(2, 2, 2);
					}

					var glowTopLine5 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowTopLine (5)" && x.activeInHierarchy);
					if (glowTopLine5 != null)
					{
						glowTopLine5.transform.position = new Vector3(0f, 12f, 0f);
					}

					var glowTopLine6 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowTopLine (6)" && x.activeInHierarchy);
					if (glowTopLine6 != null)
					{
						glowTopLine6.transform.position = new Vector3(-3f, 12f, 0f);
					}

					var glowTopLine7 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowTopLine (7)" && x.activeInHierarchy);
					if (glowTopLine7 != null)
					{
						glowTopLine7.transform.position = new Vector3(3f, 12f, 0f);
					}

					var glowTopLine8 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowTopLine (8)" && x.activeInHierarchy);
					if (glowTopLine8 != null)
					{
						glowTopLine8.transform.position = new Vector3(-6f, 12f, 0f);
					}

					var glowTopLine9 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowTopLine (9)" && x.activeInHierarchy);
					if (glowTopLine9 != null)
					{
						glowTopLine9.transform.position = new Vector3(6f, 12f, 0f);
					}

					var glowTopLine10 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowTopLine (10)" && x.activeInHierarchy);
					if (glowTopLine10 != null)
					{
						glowTopLine10.transform.position = new Vector3(-9f, 12f, 1f);
					}

					var glowTopLine11 = EnvironmentObjects.LastOrDefault(x => x.name == "GlowTopLine (11)" && x.activeInHierarchy);
					if (glowTopLine11 != null)
					{
						glowTopLine11.transform.position = new Vector3(9f, 12f, 1f);
					}

					var rotatingLasers = EnvironmentObjects.Where(x => x.name.Contains("RotatingLasersPair") && x.activeInHierarchy);
					foreach (var rotatingLaser in rotatingLasers)
					{
						rotatingLaser.transform.eulerAngles = new Vector3(-15, 0, 180);
					}

					var farBuildings = EnvironmentObjects.LastOrDefault(x => x.name == "FarBuildings" && x.activeInHierarchy);
					if (farBuildings != null)
					{
						farBuildings.transform.localScale = new Vector3(1.1f, 1f, 1f);
						farBuildings.transform.position = new Vector3(0f, -5f, -30f);
					}

					var construction = EnvironmentObjects.LastOrDefault(x => x.name == "Construction" && x.transform.parent.name != "PlayersPlace" && x.activeInHierarchy);
					if (construction != null)
					{
						construction.transform.position = new Vector3(0f, -1f, 2f);
						construction.transform.localScale = new Vector3(1.5f, 1, 1);

					}

					var vConstruction = EnvironmentObjects.LastOrDefault(x => x.name == "VConstruction" && x.activeInHierarchy);
					if (vConstruction != null)
					{
						vConstruction.transform.position = new Vector3(0f, 2f, -1f);
						vConstruction.transform.localScale = new Vector3(1f, 1, 0.7f);

					}

					var trackMirror = EnvironmentObjects.LastOrDefault(x => x.name == "TrackMirror" && x.activeInHierarchy);
					if (trackMirror != null)
					{
						trackMirror.transform.position = new Vector3(0f, -1f, 9.75f);
						trackMirror.transform.localScale = new Vector3(1.8f, 1, 1);

					}

					var laser4 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (4)" && x.activeInHierarchy);
					if (laser4 != null)
					{
						laser4.transform.position = new Vector3(12.4f, 10f, 9.9f);
						laser4.transform.eulerAngles = new Vector3(0f, 0, -30);
					}

					var laser5 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (5)" && x.activeInHierarchy);
					if (laser5 != null)
					{
						laser5.transform.position = new Vector3(-12.4f, 10f, 9.9f);
						laser5.transform.eulerAngles = new Vector3(0f, 0, 30);
					}

					//Not a typo, Beat Games apparently just skipped Laser (6)
					var laser6 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (7)" && x.activeInHierarchy);
					if (laser6 != null)
					{
						laser6.transform.position = new Vector3(12.4f, 10f, 9.7f);
						laser6.transform.eulerAngles = new Vector3(0f, 0, -30);
					}

					var laser7 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (8)" && x.activeInHierarchy);
					if (laser7 != null)
					{
						laser7.transform.position = new Vector3(-12.4f, 10f, 9.7f);
						laser7.transform.eulerAngles = new Vector3(0f, 0, 30);
					}

					var laser8 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (9)" && x.activeInHierarchy);
					if (laser8 != null)
					{
						laser8.transform.position = new Vector3(12.4f, 10f, 9.5f);
						laser8.transform.eulerAngles = new Vector3(0f, 0, -30);
					}

					var laser9 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (10)" && x.activeInHierarchy);
					if (laser9 != null)
					{
						laser9.transform.position = new Vector3(-12.4f, 10f, 9.5f);
						laser9.transform.eulerAngles = new Vector3(0f, 0, 30);
					}

					var laser10 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (11)" && x.activeInHierarchy);
					if (laser10 != null)
					{
						laser10.transform.position = new Vector3(12.4f, 10f, 9.3f);
						laser10.transform.eulerAngles = new Vector3(0f, 0, -30);
					}

					var laser11 = EnvironmentObjects.LastOrDefault(x => x.name == "Laser (12)" && x.activeInHierarchy);
					if (laser11 != null)
					{
						laser11.transform.position = new Vector3(-12.4f, 10f, 9.3f);
						laser11.transform.eulerAngles = new Vector3(0f, 0, 30);
					}
					break;
				}
				case "SkrillexEnvironment":
				{
					var skrillexLogoTop = EnvironmentObjects.LastOrDefault(x => x.name == "SkrillexLogo" && x.activeInHierarchy);
					if (skrillexLogoTop != null)
					{
						skrillexLogoTop.transform.position = new Vector3(-0.23f, 15.5f, 60f);
					}

					var skrillexLogoBottom = EnvironmentObjects.LastOrDefault(x => x.name == "SkrillexLogo (1)" && x.activeInHierarchy);
					if (skrillexLogoBottom != null)
					{
						skrillexLogoBottom.transform.position = new Vector3(-0.23f, -15.5f, 60f);
					}
					break;
				}
				case "PyroEnvironment":
				{
					var logo = EnvironmentObjects.LastOrDefault(x => x.name == "PyroLogo" && x.activeInHierarchy);
					if (logo != null)
					{
						logo.SetActive(false);
					}

					//Pull light groups in front of the screen
					var leftLightGroup = EnvironmentObjects.LastOrDefault(x => x.name == "LightGroupLeft" && x.activeInHierarchy);
					if (leftLightGroup != null)
					{
						leftLightGroup.transform.position = new Vector3(-11.1f, 0.99f, 59f);
						leftLightGroup.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
					}

					var rightLightGroup = EnvironmentObjects.LastOrDefault(x => x.name == "LightGroupRight" && x.activeInHierarchy);
					if (rightLightGroup != null)
					{
						rightLightGroup.transform.position = new Vector3(11.1f, 0.99f, 59f);
						rightLightGroup.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
					}

					var video = EnvironmentObjects.LastOrDefault(x => x.name == "Video" && x.activeInHierarchy);
					if (video != null)
					{
						video.SetActive(false);
					}

					var screens = EnvironmentObjects.Where(x => x.name.Contains("ScreenSetup") && x.activeInHierarchy);
					foreach (var screen in screens)
					{
						screen.SetActive(false);
					}

					var lightboxLeft = EnvironmentObjects.LastOrDefault(x => x.name == "LightBoxesScaffoldingLeft" && x.activeInHierarchy);
					if (lightboxLeft != null)
					{
						lightboxLeft.transform.position = new Vector3(-37.65f, -2.92f, 50.54f);
					}

					var lightboxRight = EnvironmentObjects.LastOrDefault(x => x.name == "LightBoxesScaffoldingRight" && x.activeInHierarchy);
					if (lightboxRight != null)
					{
						lightboxRight.transform.position = new Vector3(37.65f, -2.92f, 50.54f);
					}

					var mainLasersLeft = EnvironmentObjects.LastOrDefault(x => x.name == "LightGroupCloserLeft" && x.activeInHierarchy);
					if (mainLasersLeft != null)
					{
						mainLasersLeft.transform.position = new Vector3(-35.13f, -0.02f, 66.74f);
					}

					var mainLasersRight = EnvironmentObjects.LastOrDefault(x => x.name == "LightGroupCloserRight" && x.activeInHierarchy);
					if (mainLasersRight != null)
					{
						mainLasersRight.transform.position = new Vector3(35.13f, -0.02f, 66.74f);
					}

					var stairs = EnvironmentObjects.LastOrDefault(x => x.name == "Stairs" && x.activeInHierarchy);
					if (stairs != null)
					{
						stairs.transform.position = new Vector3(0f, -1f, 51.31f);
					}

					var crowd = EnvironmentObjects.LastOrDefault(x => x.name == "CrowdFlipbookGroup" && x.activeInHierarchy);
					if (crowd != null)
					{
						crowd.transform.position = new Vector3(-4.83f, -0.40f, -1.80f);
					}

					var cloneConfigLeft = new EnvironmentModification { cloneFrom = "ScafoldTriangularLeft" };
					var leftScaffoldingList = SelectObjectsFromScene(cloneConfigLeft, true);
					if (leftScaffoldingList.Any())
					{
						var original = leftScaffoldingList.First();
						var clone = CloneObject(original.gameObject, cloneConfigLeft, videoConfig, true);
						clone.gameObject.transform.position = new Vector3(original.position.x, 0.97f, original.position.z);
						clone.gameObject.transform.localScale = new Vector3(original.scale.x, 4f, original.scale.z);
					}

					var cloneConfigRight = new EnvironmentModification { cloneFrom = "ScafoldTriangularRight" };
					var rightScaffoldingList = SelectObjectsFromScene(cloneConfigRight, true);
					if (rightScaffoldingList.Any())
					{
						var original = rightScaffoldingList.First();
						var clone = CloneObject(original.gameObject, cloneConfigRight, videoConfig, true);
						clone.gameObject.transform.position = new Vector3(original.position.x, 0.97f, original.position.z);
						clone.gameObject.transform.localScale = new Vector3(original.scale.x, 4f, original.scale.z);
					}
					break;
				}
				case "LizzoEnvironment":
				{
					var rainbow = EnvironmentObjects.LastOrDefault(x => x.name == "Rainbow" && x.activeInHierarchy);
					if (rainbow != null)
					{
						rainbow.transform.position = new Vector3(0f, 2.1f, 65.51f);
						rainbow.transform.localScale = new Vector3(2f, 2f, 2f);
					}

					var rainbowLights = EnvironmentObjects.LastOrDefault(x => x.name == "BehindRainbowSpotlights" && x.activeInHierarchy);
					if (rainbowLights != null)
					{
						rainbowLights.transform.position = new Vector3(0f, 2.1f, 62.24f);
						rainbowLights.transform.localScale = new Vector3(2f, 2f, 2f);
					}
					break;
				}
				case "WeaveEnvironment":
				{
					var lightGroupLeft = EnvironmentObjects.LastOrDefault(x => x.name == "LightGroup14" && x.activeInHierarchy);
					if (lightGroupLeft != null)
					{
						lightGroupLeft.transform.position = new Vector3(-3.85f, 1.50f, 20.90f);
					}

					var lightGroupRight = EnvironmentObjects.LastOrDefault(x => x.name == "LightGroup15" && x.activeInHierarchy);
					if (lightGroupRight != null)
					{
						lightGroupRight.transform.position = new Vector3(3.85f, 1.50f, 20.90f);
					}

					break;
				}

				case "EDMEnvironment":
				{
					var spectrograms = EnvironmentObjects.LastOrDefault(x => x.name == "Spectrograms" && x.activeInHierarchy);
					if (spectrograms != null)
					{
						spectrograms.transform.position = new Vector3(0f, -2f, 1.2f);
					}



					break;
				}
			}
		}

		public static void VideoConfigSceneModifications(VideoConfig? config)
		{
			if (config == null)
			{
				return;
			}

			if (!config.IsPlayable && (config.forceEnvironmentModifications == null || config.forceEnvironmentModifications == false))
			{
				return;
			}

			if (config.additionalScreens != null)
			{
				var screenController = PlaybackController.Instance.VideoPlayer.screenController;
				var i = 0;
				foreach (var screenConfig in config.additionalScreens)
				{
					var clone = screenController.Screens.Find(screen => screen.name.EndsWith("(" + (i) + ")"));
					if (!clone)
					{
						Log.Error($"Couldn't find a screen ending with {"(" + (i) + ")"}");
						continue;
					}
					if (screenConfig.position.HasValue)
					{
						clone.transform.position = screenConfig.position.Value;
					}

					if (screenConfig.rotation.HasValue)
					{
						clone.transform.eulerAngles = screenConfig.rotation.Value;
					}

					if (screenConfig.scale.HasValue)
					{
						clone.transform.localScale = screenConfig.scale.Value;
					}

					i++;
				}
			}

			if (config.environment == null || config.environment.Length == 0)
			{
				return;
			}

			foreach (var environmentModification in config.environment)
			{
				var selectedObjectsList = SelectObjectsFromScene(environmentModification, false);
				if (!selectedObjectsList.Any())
				{
					Log.Error($"Failed to find object: name={environmentModification.name}, parentName={environmentModification.parentName ?? "null"}, cloneFrom={environmentModification.cloneFrom ?? "null"}");
					continue;
				}

				foreach (var environmentObject in selectedObjectsList)
				{
					if (_currentEnvironmentName == "PyroEnvironment" &&
					    environmentObject.name.StartsWith("LightGroup") &&
					    environmentModification.position.HasValue &&
					    Math.Abs(environmentModification.position.Value.y - 1.99f) < 0.1f)
					{
						//Fixes configs that were made before Pyro changes
						continue;
					}

					if (environmentModification.active.HasValue)
					{
						environmentObject.SetActive(environmentModification.active.Value);
					}

					if (environmentModification.position.HasValue)
					{
						environmentObject.transform.position = environmentModification.position.Value;
					}

					if (environmentModification.rotation.HasValue)
					{
						environmentObject.transform.eulerAngles = environmentModification.rotation.Value;
					}

					if (environmentModification.scale.HasValue)
					{
						environmentObject.transform.localScale = environmentModification.scale.Value;
					}
				}
			}
		}

		private static List<EnvironmentObject> SelectObjectsFromScene(EnvironmentModification modification, bool selectByCloneFrom)
		{
			modification = TranslateNameForBackwardsCompatibility(modification);
			var name = selectByCloneFrom ? modification.cloneFrom! : modification.name;
			var parentName = modification.parentName;
			if (!selectByCloneFrom && modification.cloneFrom != null)
			{
				name += CLONED_OBJECT_NAME_SUFFIX;
			}

			IEnumerable<EnvironmentObject>? environmentObjects = null;
			try
			{
				environmentObjects = EnvironmentObjects.Where(x =>
					x.name == name &&
					(parentName == null || x.transform.parent.name == parentName));
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}

			var environmentObjectList = (environmentObjects ?? Array.Empty<EnvironmentObject>()).ToList();
			return environmentObjectList;
		}

		private static void CreateAdditionalScreens(VideoConfig videoConfig)
		{
			if (videoConfig.additionalScreens == null)
			{
				return;
			}

			var screenController = PlaybackController.Instance.VideoPlayer.screenController;
			var i = 0;
			foreach (var _ in videoConfig.additionalScreens)
			{
				var clone = Object.Instantiate(screenController.Screens[0], screenController.Screens[0].transform.parent);
				clone.name += $" ({i++.ToString()})";
			}
		}

		private static void PrepareClonedScreens(VideoConfig videoConfig)
		{
			var screenCount = PlaybackController.Instance.gameObject.transform.childCount;

			if (screenCount <= 1)
			{
				return;
			}

			Log.Debug($"Screens found: {screenCount}");
			foreach (Transform screen in PlaybackController.Instance.gameObject.transform)
			{
				if (!screen.name.StartsWith("CinemaScreen"))
				{
					return;
				}

				if (screen.name.Contains("Clone"))
				{
					PlaybackController.Instance.VideoPlayer.screenController.Screens.Add(screen.gameObject);
					screen.GetComponent<Renderer>().material = PlaybackController.Instance.VideoPlayer.screenController.Screens[0].GetComponent<Renderer>().material;
					Object.Destroy(screen.Find("CinemaDirectionalLight").gameObject);
				}

				screen.gameObject.GetComponent<CustomBloomPrePass>().enabled = false;
				Log.Debug("Disabled bloom prepass");
			}

			PlaybackController.Instance.VideoPlayer.SetPlacement(
				Placement.CreatePlacementForConfig(videoConfig, PlaybackController.Scene.SoloGameplay, PlaybackController.Instance.VideoPlayer.GetVideoAspectRatio())
			);
			PlaybackController.Instance.VideoPlayer.screenController.SetShaderParameters(videoConfig);
		}

		private static void CloneObjects(VideoConfig? config)
		{
			if (config?.environment == null || config.environment.Length == 0 || Util.IsMultiplayer())
			{
				return;
			}

			Log.Debug("Cloning objects");
			var cloneCounter = 0;
			foreach (var objectToBeCloned in config.environment)
			{
				if (objectToBeCloned.cloneFrom == null)
				{
					continue;
				}

				var environmentObjectList = SelectObjectsFromScene(objectToBeCloned, true);
				if (!environmentObjectList.Any())
				{
					Log.Error($"Failed to find object while cloning: name={objectToBeCloned.cloneFrom}, parentName={objectToBeCloned.parentName ?? "null"}");
					continue;
				}

				var originalObject = environmentObjectList.Last().gameObject;
				CloneObject(originalObject, objectToBeCloned, config);
				cloneCounter++;
			}
			Log.Debug("Cloned "+cloneCounter+" objects");
		}

		private static EnvironmentObject CloneObject(GameObject originalObject, EnvironmentModification objectToBeCloned, VideoConfig? config, bool disableZOffset = false)
		{
			var lightManager = EnvironmentObjects.LastOrDefault(x => x.name == "LightWithIdManager");
			if (lightManager == null)
			{
				Log.Error("Failed to find LightWithIdManager. Cannot clone lights.");
			}

			var clone = Object.Instantiate(originalObject, originalObject.transform.parent);

			//Move the new object far away to prevent changing the prop IDs that chroma assigns, but only if "mergePropGroups" is not set
			var position = clone.transform.position;
			var zOffset = disableZOffset ? 0 : (config?.mergePropGroups == null || config.mergePropGroups == false ? CLONED_OBJECT_Z_OFFSET : 0);
			var newPosition = new Vector3(position.x, position.y, position.z + zOffset);
			clone.transform.position = newPosition;

			//If the object has no position specified, add a position that reverts the z-offset
			objectToBeCloned.position ??= position;

			if (!clone.name.EndsWith(CLONED_OBJECT_NAME_SUFFIX))
			{
				clone.name = (objectToBeCloned.name ?? originalObject.transform.name)  + CLONED_OBJECT_NAME_SUFFIX;
			}

			try
			{
				RegisterLights(clone, lightManager?.transform.GetComponent<LightWithIdManager>());
				RegisterMirror(clone);
				RegisterSpectrograms(clone);
			}
			catch (Exception e)
			{
				Log.Error(e);
			}

			var cloneEnvironmentObject = new EnvironmentObject(clone, true);
			objectToBeCloned.gameObjectClone = cloneEnvironmentObject;
			_environmentObjectList?.Add(cloneEnvironmentObject);
			return cloneEnvironmentObject;
		}

		private static void RegisterLights(GameObject clone, LightWithIdManager? lightWithIdManager)
		{
			if (lightWithIdManager == null)
			{
				return;
			}

			RegisterLight(clone.GetComponent<LightWithIdMonoBehaviour>(), lightWithIdManager);
			foreach (Transform child in clone.transform)
			{
				RegisterLight(child.GetComponent<LightWithIdMonoBehaviour>(), lightWithIdManager);
			}
		}

		private static void RegisterLight(LightWithIdMonoBehaviour? newLight, LightWithIdManager lightWithIdManager)
		{
			if (newLight != null)
			{
				lightWithIdManager.RegisterLight(newLight);
			}
		}

		private static void RegisterMirror(GameObject clone)
		{
			var mirror = clone.GetComponent<Mirror>();
			if (mirror == null)
			{
				return;
			}

			Log.Debug("Cloned a mirror surface");
			var originalMirrorRenderer = mirror._mirrorRenderer;
			var originalMaterial = mirror._mirrorMaterial;
			var clonedMirrorRenderer = Object.Instantiate(originalMirrorRenderer);
			var clonedMaterial = Object.Instantiate(originalMaterial);
			mirror._mirrorRenderer = clonedMirrorRenderer;
			mirror._mirrorMaterial = clonedMaterial;
		}

		private static void RegisterSpectrograms(GameObject clone)
		{
			//Hierarchy looks like this:
			//"Spectrograms" (one, this one has the Spectrogram component) --> "Spectrogram" (multiple, this is what we're cloning) -->
			//"Spectrogram0" + "Spectrogram1" (contain the MeshRenderers that need to be registered in the Spectrogram component)
			var parent = clone.transform.parent;
			var component = parent.gameObject.GetComponent<Spectrogram>();
			if (parent.name != "Spectrograms" || component == null)
			{
				return;
			}

			var spectrogramMeshRenderers = clone.GetComponentsInChildren<MeshRenderer>();
			var meshRendererList = component._meshRenderers.ToList();
			meshRendererList.AddRange(spectrogramMeshRenderers);
			component._meshRenderers = meshRendererList.ToArray();
		}

		private static EnvironmentModification TranslateNameForBackwardsCompatibility(EnvironmentModification modification)
		{
			var selectByCloneFrom = modification.cloneFrom != null;
			var name = selectByCloneFrom ? modification.cloneFrom! : modification.name;
			var newName = name;

			switch (_currentEnvironmentName)
			{
				case "BigMirrorEnvironment":
				{
					newName = name switch
					{
						"GlowLineL" => "NeonTubeDirectionalL",
						"GlowLineL2" => "NeonTubeDirectionalFL",
						"GlowLineR" => "NeonTubeDirectionalR",
						"GlowLineR2" => "NeonTubeDirectionalFR",
						_ => name
					};

					if (modification.parentName == "Buildings")
					{
						modification.parentName = "Environment";
					}

					break;
				}
				case "NiceEnvironment":
				{
					newName = name switch
					{
						"TrackLaneRing(Clone)" => "SmallTrackLaneRing(Clone)",
						_ => name
					};
					break;
				}
			}

			if (selectByCloneFrom)
			{
				modification.cloneFrom = newName;
			}
			else
			{
				modification.name = newName;
			}

			return modification;
		}
	}
}