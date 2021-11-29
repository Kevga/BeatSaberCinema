using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using BS_Utils.Utilities;
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

		private static bool _environmentModified;
		private static string _currentEnvironment = "Menu";
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
				foreach (var gameObject in gameObjects)
				{
					//Relevant GameObjects are mostly in "GameCore" or the scene of the current environment, so filter out everything else
					if (gameObject.scene != activeScene && gameObject.scene.name != _currentEnvironment)
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
			BSEvents.gameSceneLoaded += SceneChanged;
			BSEvents.lateMenuSceneLoadedFresh += SceneChanged;
			BSEvents.menuSceneLoaded += SceneChanged;
		}

		public static void Disable()
		{
			BSEvents.gameSceneLoaded -= SceneChanged;
			BSEvents.lateMenuSceneLoadedFresh -= SceneChanged;
			BSEvents.menuSceneLoaded -= SceneChanged;
			_environmentObjectList?.Clear();
		}

		private static void SceneChanged()
		{
			_currentEnvironment = "MainMenu";
			var sceneName = SceneManager.GetActiveScene().name;
			if (sceneName == "GameCore")
			{
				var environment = GameObject.Find("Environment");
				if (environment != null)
				{
					_currentEnvironment = environment.scene.name;
				}
			}
			else
			{
				Reset();
			}
			Log.Debug($"Environment name (new method): {_currentEnvironment}");
		}

		private static void SceneChanged(ScenesTransitionSetupDataSO scenesTransitionSetupDataSo)
		{
			SceneChanged();
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
			Log.Debug("Loaded environment: "+BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.serializedName);

			var stopwatch = new Stopwatch();
			stopwatch.Start();

			CreateAdditionalScreens(videoConfig);
			PrepareClonedScreens(videoConfig);
			CloneObjects(videoConfig);

			try
			{
				if (videoConfig!.disableDefaultModifications == null || videoConfig.disableDefaultModifications.Value == false)
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

			var mainScreen = PlaybackController.Instance.VideoPlayer.screenController.screens[0];
			mainScreen.gameObject.GetComponent<CustomBloomPrePass>().enabled = true;

			if (PlaybackController.Instance.VideoPlayer.screenController.screens.Count > 0)
			{
				foreach (var screen in PlaybackController.Instance.VideoPlayer.screenController.screens.Where(screen => screen.name.Contains("Clone")))
				{
					Object.Destroy(screen);
					Log.Debug("Destroyed screen");
				}

				PlaybackController.Instance.VideoPlayer.screenController.screens.RemoveRange(1, PlaybackController.Instance.VideoPlayer.screenController.screens.Count - 1);


				if (Util.IsModInstalled("_Heck"))
				{
					var types = new[] {"Chroma.GameObjectTrackController", "Chroma.Lighting.EnvironmentEnhancement.GameObjectTrackController"};

					foreach (var typeName in types)
					{
						try
						{
							var trackControllerType = Util.FindType(typeName, "Chroma");
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

				Log.Debug($"Screen count: {PlaybackController.Instance.VideoPlayer.screenController.screens.Count}");
			}

			_environmentModified = false;
			_environmentObjectList?.Clear();
			if (PlaybackController.Instance != null && PlaybackController.Instance.VideoPlayer != null)
			{
				PlaybackController.Instance.VideoPlayer.SetSoftParent(null);
			}
		}

		private static void DefaultSceneModifications(VideoConfig? videoConfig)
		{
			//FrontLights appear in many environments and need to be removed in all of them
			var frontLights = EnvironmentObjects.LastOrDefault(x => (x.name == "FrontLights" || x.name == "FrontLight") && x.activeInHierarchy);
			if (frontLights != null)
			{
				frontLights.SetActive(false);
			}

			switch (BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.serializedName)
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

					if (videoConfig!.screenPosition == null)
					{
						var placement = Placement.GetDefaultPlacementForScene(PlaybackController.Scene.SoloGameplay);
						placement.Position.z = 80;
						PlaybackController.Instance.VideoPlayer.SetPlacement(placement);
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
						ring.transform.localScale = new Vector3(5f, 5f, 1f);
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

					//Use different defaults for this environment
					var placement = new Placement(videoConfig, PlaybackController.Scene.SoloGameplay, PlaybackController.Instance.VideoPlayer.GetVideoAspectRatio());
					placement.Position = videoConfig?.screenPosition ?? new Vector3(0f, 6.2f, 52.7f);
					placement.Rotation = videoConfig?.screenRotation ?? Vector3.zero;
					placement.Height = videoConfig?.screenHeight ?? 16f;
					placement.Curvature = videoConfig?.screenCurvature ?? 0f;
					PlaybackController.Instance.VideoPlayer.SetPlacement(placement);
					break;
				}
				case "KaleidoscopeEnvironment":
				{
					var construction = EnvironmentObjects.LastOrDefault(x => x.name == "Construction" && x.transform.parent.name != "PlayersPlace" && x.activeInHierarchy);
					if (construction != null)
					{
						construction.SetActive(false);
					}
					var trackMirror = EnvironmentObjects.LastOrDefault(x => x.name == "TrackMirror" && x.activeInHierarchy);
					if (trackMirror != null)
					{
						trackMirror.SetActive(false);
					}
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

					var placement = new Placement(videoConfig, PlaybackController.Scene.SoloGameplay, PlaybackController.Instance.VideoPlayer.GetVideoAspectRatio());
					placement.Position = videoConfig?.screenPosition ?? new Vector3(0f, 1f, 35f);
					placement.Rotation = videoConfig?.screenRotation ?? Vector3.zero;
					placement.Height = videoConfig?.screenHeight ?? 12f;
					placement.Curvature = videoConfig?.screenCurvature;
					PlaybackController.Instance.VideoPlayer.SetPlacement(placement);
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
					if (ceilingFront != null)
					{
						ceilingFront.SetActive(false);
					}

					var ceilingBack = EnvironmentObjects.LastOrDefault(x => x.name == "Plane (4)" && x.activeInHierarchy);
					if (ceilingBack != null)
					{
						ceilingBack.SetActive(false);
					}

					var topLights = EnvironmentObjects.Where(x => x.name.Contains("NeonTop") && x.activeInHierarchy);
					foreach (var light in topLights)
					{
						light.SetActive(false);
					}

					var placement = new Placement(videoConfig, PlaybackController.Scene.SoloGameplay, PlaybackController.Instance.VideoPlayer.GetVideoAspectRatio());
					placement.Position = videoConfig?.screenPosition ?? new Vector3(0f, 6.3f, 37f);
					placement.Rotation = videoConfig?.screenRotation ?? Vector3.zero;
					placement.Height = videoConfig?.screenHeight ?? 12.5f;
					placement.Curvature = videoConfig?.screenCurvature;
					PlaybackController.Instance.VideoPlayer.SetPlacement(placement);
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

					var placement = new Placement(videoConfig, PlaybackController.Scene.SoloGameplay, PlaybackController.Instance.VideoPlayer.GetVideoAspectRatio());
					placement.Position = videoConfig?.screenPosition ?? new Vector3(0f, 5.46f, 40f);
					placement.Rotation = videoConfig?.screenRotation ?? new Vector3(-5f, 0f, 0f);
					placement.Height = videoConfig?.screenHeight ?? 13f;
					placement.Curvature = videoConfig?.screenCurvature;
					PlaybackController.Instance.VideoPlayer.SetPlacement(placement);

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

					var placement = new Placement(videoConfig, PlaybackController.Scene.SoloGameplay, PlaybackController.Instance.VideoPlayer.GetVideoAspectRatio());
					placement.Position = videoConfig?.screenPosition ?? new Vector3(0f, 1.5f, 30f);
					placement.Rotation = videoConfig?.screenRotation ?? new Vector3(0f, 0f, 0f);
					placement.Height = videoConfig?.screenHeight ?? 12f;
					placement.Curvature = videoConfig?.screenCurvature;
					PlaybackController.Instance.VideoPlayer.SetPlacement(placement);
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
					var clone = screenController.screens.Find(screen => screen.name.EndsWith("(" + (i) + ")"));
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
				var clone = Object.Instantiate(screenController.screens[0], screenController.screens[0].transform.parent);
				clone.name += $" ({i++.ToString()})";
			}
		}

		private static void PrepareClonedScreens(VideoConfig videoConfig)
		{
			var screenCount = PlaybackController.GO.transform.childCount;
			if (screenCount <= 1)
			{
				return;
			}

			Log.Debug($"Screens found: {screenCount}");
			foreach (Transform screen in PlaybackController.GO.transform)
			{
				if (screen.name.Contains("Clone"))
				{
					PlaybackController.Instance.VideoPlayer.screenController.screens.Add(screen.gameObject);
					screen.GetComponent<Renderer>().material = PlaybackController.Instance.VideoPlayer.screenController.screens[0].GetComponent<Renderer>().material;
				}

				screen.gameObject.GetComponent<CustomBloomPrePass>().enabled = false;
				Log.Debug("Disabled bloom prepass");
			}

			PlaybackController.Instance.VideoPlayer.SetPlacement(
				new Placement(videoConfig, PlaybackController.Scene.SoloGameplay, PlaybackController.Instance.VideoPlayer.GetVideoAspectRatio()));
			PlaybackController.Instance.VideoPlayer.screenController.SetShaderParameters(videoConfig);
		}

		private static void CloneObjects(VideoConfig? config)
		{
			if (config?.environment == null || config.environment.Length == 0 || Util.IsMultiplayer())
			{
				return;
			}

			Log.Debug("Cloning objects");
			var lightManager = Resources.FindObjectsOfTypeAll<LightWithIdManager>().LastOrDefault();
			if (lightManager == null)
			{
				Log.Error("Failed to find LightWithIdManager. Cannot clone lights.");
			}

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

				var clone = Object.Instantiate(originalObject, originalObject.transform.parent);

				//Move the new object far away to prevent changing the prop IDs that chroma assigns, but only if "mergePropGroups" is not set
				var position = clone.transform.position;
				var zOffset = (config.mergePropGroups == null || config.mergePropGroups == false ? CLONED_OBJECT_Z_OFFSET : 0);
				var newPosition = new Vector3(position.x, position.y, position.z + zOffset);
				clone.transform.position = newPosition;

				//If the object has no position specified, add a position that reverts the z-offset
				objectToBeCloned.position ??= position;

				if (!clone.name.EndsWith(CLONED_OBJECT_NAME_SUFFIX))
				{
					clone.name = objectToBeCloned.name + CLONED_OBJECT_NAME_SUFFIX;
				}

				try
				{
					RegisterLights(clone, lightManager);
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

				cloneCounter++;
			}
			Log.Debug("Cloned "+cloneCounter+" objects");
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
			var originalMirrorRenderer = mirror.GetField<MirrorRendererSO, Mirror>("_mirrorRenderer");
			var originalMaterial = mirror.GetField<Material, Mirror>("_mirrorMaterial");
			var clonedMirrorRenderer = Object.Instantiate(originalMirrorRenderer);
			var clonedMaterial = Object.Instantiate(originalMaterial);
			mirror.SetField("_mirrorRenderer", clonedMirrorRenderer);
			mirror.SetField("_mirrorMaterial", clonedMaterial);
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
			var meshRendererList = component.GetField<MeshRenderer[], Spectrogram>("_meshRenderers").ToList();
			meshRendererList.AddRange(spectrogramMeshRenderers);
			component.SetField("_meshRenderers", meshRendererList.ToArray());
		}

		private static EnvironmentModification TranslateNameForBackwardsCompatibility(EnvironmentModification modification)
		{
			var selectByCloneFrom = modification.cloneFrom != null;
			var name = selectByCloneFrom ? modification.cloneFrom! : modification.name;
			string newName = name;

			switch (BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.serializedName)
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