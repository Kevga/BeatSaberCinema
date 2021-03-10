using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IPA.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BeatSaberCinema
{
	public static class EnvironmentController
	{
		private const float CLONED_OBJECT_Z_OFFSET = 200f;
		private const string CLONED_OBJECT_NAME_SUFFIX = " (CinemaClone)";

		private static bool _environmentModified;

		public static void ModifyGameScene(VideoConfig? videoConfig)
		{
			if (!SettingsStore.Instance.PluginEnabled || !Plugin.Enabled || videoConfig == null || Util.IsMultiplayer() ||
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

			try
			{
				if (videoConfig!.disableDefaultModifications == null || videoConfig.disableDefaultModifications.Value == false)
				{
					DefaultSceneModifications(videoConfig);
				}

				VideoConfigSceneModifications(videoConfig);
			}
			catch (Exception e)
			{
				Log.Error(e);
			}

			Log.Debug("Modified environment");
		}

		public static void Reset()
		{
			_environmentModified = false;
		}

		private static void DefaultSceneModifications(VideoConfig? videoConfig)
		{
			var sceneObjectList = Resources.FindObjectsOfTypeAll<GameObject>();

			//FrontLights appear in many environments and need to be removed in all of them
			var frontLights = sceneObjectList.LastOrDefault(x => x.name == "FrontLights" && x.activeInHierarchy);
			if (frontLights != null)
			{
				frontLights.SetActive(false);
			}

			switch (BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.environmentInfo.serializedName)
			{
				case "NiceEnvironment":
				case "BigMirrorEnvironment":
				{
					var doubleColorLasers = sceneObjectList.Where(x => x.name.Contains("DoubleColorLaser") && x.activeInHierarchy);
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

						var shiftBy = 18f * sign;
						var pos = doubleColorLaser.transform.position;
						doubleColorLaser.transform.position = new Vector3(pos.x + shiftBy, pos.y, pos.z);
					}
					break;
				}
				case "BTSEnvironment":
				{
					var centerLight = sceneObjectList.LastOrDefault(x => x.name == "MagicDoorSprite" && x.activeInHierarchy);
					if (centerLight != null)
					{
						centerLight.SetActive(false);
					}

					var pillarPairs = sceneObjectList.Where(x => x.name.Contains("PillarPair") && x.activeInHierarchy);
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

						var children = sceneObjectList.Where(x =>
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
					movementEffectStartPositions.Reverse();
					movementEffect.SetField("_startLocalPositions", movementEffectStartPositions.ToArray());

					if (videoConfig!.screenPosition == null)
					{
						PlaybackController.Instance.SetScreenDistance(80f);
					}

					break;
				}
				case "OriginsEnvironment":
				{
					var spectrograms = sceneObjectList.Where(x => x.name == "Spectrogram" && x.activeInHierarchy);
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
					var construction = sceneObjectList.LastOrDefault(x => x.name == "Construction" && x.transform.parent.name != "PlayersPlace" && x.activeInHierarchy);
					if (construction != null)
					{
						//Stretch it in the y-axis to get rid of the beam above
						construction.transform.localScale = new Vector3(1, 2, 1);
					}

					var tentacles = sceneObjectList.Where(x => x.name.Contains("Tentacle") && x.activeInHierarchy);
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

					var verticalLasers = sceneObjectList.Where(
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

					var glowLines = sceneObjectList.Where(x => x.name.Contains("GlowTopLine") && x.activeInHierarchy);
					foreach (var glowLine in glowLines)
					{
						var pos = glowLine.transform.position;
						glowLine.transform.position = new Vector3(pos.x, 20f, pos.z);
					}

					break;
				}
				case "RocketEnvironment":
				{
					var cars = sceneObjectList.Where(x => x.name.Contains("RocketCar") && x.activeInHierarchy);
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

					var arena = sceneObjectList.LastOrDefault(x => x.name == "RocketArena" && x.activeInHierarchy);
					if (arena != null)
					{
						arena.transform.localScale = new Vector3(1, 2, 1);
					}

					var arenaLight = sceneObjectList.LastOrDefault(x => x.name == "RocketArenaLight" && x.activeInHierarchy);
					if (arenaLight != null)
					{
						arenaLight.transform.position = new Vector3(0, 23, 42);
						arenaLight.transform.localScale = new Vector3(2.5f, 1, 1);
					}

					var gateLight = sceneObjectList.LastOrDefault(x => x.name == "RocketGateLight" && x.activeInHierarchy);
					if (gateLight != null)
					{
						gateLight.transform.position = new Vector3(0, -3, 64);
						gateLight.transform.localScale = new Vector3(2.6f, 1, 4.5f);
					}
					break;
				}
				case "DragonsEnvironment":
				{
					var spectrograms = sceneObjectList.Where(x => x.name == "Spectrogram" && x.activeInHierarchy);
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

					var topConstructionParts = sceneObjectList.Where(x => x.name.Contains("TopConstruction") && x.activeInHierarchy);
					foreach (var topConstruction in topConstructionParts)
					{
						var pos = topConstruction.transform.position;
						topConstruction.transform.position = new Vector3(pos.x, 27.0f, pos.z);
					}

					var hallConstruction = sceneObjectList.LastOrDefault(x => x.name == "HallConstruction" && x.activeInHierarchy);
					if (hallConstruction != null)
					{
						var pos = hallConstruction.transform.position;
						hallConstruction.transform.position = new Vector3(pos.x, 22.0f, pos.z);
					}

					var trackLaneRings = sceneObjectList.Where(x => x.name.Contains("PanelsTrackLaneRing") && x.activeInHierarchy);
					foreach (var ring in trackLaneRings)
					{
						ring.transform.localScale = new Vector3(5f, 5f, 1f);
					}
					break;
				}
				case "LinkinParkEnvironment":
				{
					var logo = sceneObjectList.LastOrDefault(x =>
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

					var environment = sceneObjectList.LastOrDefault(x => x.name == "Environment" && x.activeInHierarchy);
					if (environment != null)
					{
						environment.transform.localScale = environmentScale;
					}

					var trackConstruction = sceneObjectList.LastOrDefault(x => x.name == "TrackConstruction" && x.activeInHierarchy);
					if (trackConstruction != null)
					{
						trackConstruction.transform.position = new Vector3(0.9f, 0f, 106.5f);
						trackConstruction.transform.localScale = invertedScale;
					}

					var trackMirror = sceneObjectList.LastOrDefault(x => x.name == "TrackMirror" && x.activeInHierarchy);
					if (trackMirror != null)
					{
						trackMirror.transform.position = new Vector3(0.3f, 0f, 6.55f);
						trackMirror.transform.localScale = invertedScale;
					}

					var trackShadow = sceneObjectList.LastOrDefault(x => x.name == "TrackShadow" && x.activeInHierarchy);
					if (trackShadow != null)
					{
						trackShadow.transform.position = new Vector3(0f, -0.3f, 126.1f);
						trackShadow.transform.localScale = invertedScale;
					}

					var playersPlace = sceneObjectList.LastOrDefault(x => x.name == "PlayersPlace" && x.activeInHierarchy);
					if (playersPlace != null)
					{
						playersPlace.transform.localScale = invertedScale;
					}

					var playersPlaceShadow = sceneObjectList.LastOrDefault(x => x.name == "PlayersPlaceShadow" && x.activeInHierarchy);
					if (playersPlaceShadow != null)
					{
						playersPlaceShadow.transform.localScale = invertedScale;
					}

					var hud = sceneObjectList.LastOrDefault(x => x.name == "NarrowGameHUD" && x.activeInHierarchy);
					if (hud != null)
					{
						hud.transform.localScale = invertedScale;
					}

					//Use different defaults for this environment
					var placement = new Placement(videoConfig, PlaybackController.Scene.SoloGameplay);
					placement.Position = videoConfig?.screenPosition ?? new Vector3(0f, 6.2f, 52.7f);
					placement.Rotation = videoConfig?.screenRotation ?? Vector3.zero;
					placement.Height = videoConfig?.screenHeight ?? 16f;
					placement.Curvature = videoConfig?.screenCurvature ?? 0f;
					PlaybackController.Instance.VideoPlayer.SetPlacement(placement);
					break;
				}
			}
		}

		public static void VideoConfigSceneModifications(VideoConfig? config)
		{
			if (config?.environment == null || config.environment.Length == 0)
			{
				return;
			}

			if (!config.IsPlayable && (config.forceEnvironmentModifications == null || config.forceEnvironmentModifications == false))
			{
				return;
			}

			var sceneObjectList = Resources.FindObjectsOfTypeAll<GameObject>();

			foreach (var environmentModification in config.environment)
			{
				List<GameObject>? selectedObjectsList;
				if (environmentModification.clonedObject != null)
				{
					selectedObjectsList = new List<GameObject> {environmentModification.clonedObject};
				}
				else
				{
					selectedObjectsList = SelectObjectsFromScene(environmentModification.name, environmentModification.parentName, environmentModification.cloneFrom != null, sceneObjectList);
					if (selectedObjectsList == null || !selectedObjectsList.Any())
					{
						Log.Error($"Failed to find object: name={environmentModification.name}, parentName={environmentModification.parentName}");
						continue;
					}
				}

				foreach (var environmentObject in selectedObjectsList)
				{
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

		private static List<GameObject>? SelectObjectsFromScene(string name, string? parentName, bool clone = false, IEnumerable<GameObject>? sceneObjectList = null)
		{
			name = TranslateNameForBackwardsCompatibility(name);
			if (clone)
			{
				name += CLONED_OBJECT_NAME_SUFFIX;
			}
			IEnumerable<GameObject>? environmentObjects = null;
			try
			{
				sceneObjectList ??= Resources.FindObjectsOfTypeAll<GameObject>();
				environmentObjects = sceneObjectList.Where(x =>
					x.name == name &&
					(parentName == null || x.transform.parent.name == parentName));
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}

			return environmentObjects?.ToList();
		}

		public static void CloneObjects(VideoConfig? config)
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

			var sceneObjectList = Resources.FindObjectsOfTypeAll<GameObject>();

			var cloneCounter = 0;
			foreach (var objectToBeCloned in config.environment)
			{
				if (objectToBeCloned.cloneFrom == null)
				{
					continue;
				}

				var environmentObjectList = SelectObjectsFromScene(objectToBeCloned.cloneFrom!, objectToBeCloned.parentName, false, sceneObjectList);
				if (environmentObjectList == null || !environmentObjectList.Any())
				{
					Log.Error($"Failed to find object while cloning: name={objectToBeCloned.name}, parentName={objectToBeCloned.parentName}");
					continue;
				}

				var originalObject = environmentObjectList.Last();

				var clone = Object.Instantiate(originalObject, originalObject.transform.parent);
				objectToBeCloned.clonedObject = clone;

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
			foreach (var renderer in spectrogramMeshRenderers)
			{
				meshRendererList.Add(renderer);
			}
			component.SetField("_meshRenderers", meshRendererList.ToArray());
		}

		private static string TranslateNameForBackwardsCompatibility(string name)
		{
			return name switch
			{
				"GlowLineL" => "NeonTubeDirectionalL",
				"GlowLineL2" => "NeonTubeDirectionalFL",
				"GlowLineR" => "NeonTubeDirectionalR",
				"GlowLineR2" => "NeonTubeDirectionalFR",
				_ => name
			};
		}
	}
}