using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace BeatSaberCinema.Patches
{
	[HarmonyPatch]
	[UsedImplicitly]
	// ReSharper disable once InconsistentNaming
	internal static class StandardLevelScenesTransitionSetupDataSOInit
	{
		private static MethodInfo TargetMethod() => AccessTools.FirstMethod(typeof(StandardLevelScenesTransitionSetupDataSO),
			m => m.Name == nameof(StandardLevelScenesTransitionSetupDataSO.Init) &&
			     m.GetParameters().All(p => p.ParameterType != typeof(IBeatmapLevelData)));

		[UsedImplicitly]
		public static void Prefix(BeatmapLevel beatmapLevel, BeatmapKey beatmapKey, ref OverrideEnvironmentSettings overrideEnvironmentSettings)
		{
			//Wrap all of it in try/catch so an exception would not prevent the player from playing songs
			try
			{
				PlaybackController.Instance.SceneTransitionInitCalled();
				VideoMenu.instance.SetSelectedLevel(beatmapLevel);

				if (!SettingsStore.Instance.PluginEnabled || SettingsStore.Instance.ForceDisableEnvironmentOverrides)
				{
					Log.Info($"Cinema disabled: {!SettingsStore.Instance.PluginEnabled}, environment override force disabled: {SettingsStore.Instance.ForceDisableEnvironmentOverrides}");
					return;
				}

				var video = PlaybackController.Instance.VideoConfig;
				if (video == null || (!video.IsPlayable && video.forceEnvironmentModifications != true))
				{
					Log.Debug($"No video or not playable, DownloadState: {video?.DownloadState}");
					return;
				}

				if (video.environmentName != null)
				{
					var overrideSettings = GetOverrideEnvironmentSettingsFor(video.environmentName);
					if (overrideSettings != null)
					{
						overrideEnvironmentSettings = overrideSettings;
						Log.Debug($"Overriding environment to {video.environmentName} as configured");
						return;
					}
				}

				if (video.EnvironmentModified)
				{
					Log.Debug("Environment is modified, disabling environment override");
					overrideEnvironmentSettings = null!;
					return;
				}

				var overrideEnvironmentEnabled = SettingsStore.Instance.OverrideEnvironment;
				if (!overrideEnvironmentEnabled)
				{
					Log.Debug("Cinema's environment override disallowed by user");
					return;
				}

				var environmentWhitelist = new[]
				{
					"BigMirrorEnvironment",
					"OriginsEnvironment",
					"BTSEnvironment",
					"KDAEnvironment",
					"RocketEnvironment",
					"DragonsEnvironment",
					"Dragons2Environment",
					"LinkinParkEnvironment",
					"KaleidoscopeEnvironment",
					"GlassDesertEnvironment",
					"MonstercatEnvironment",
					"CrabRaveEnvironment",
					"SkrillexEnvironment",
					"WeaveEnvironment",
					"PyroEnvironment",
					"EDMEnvironment",
					"LizzoEnvironment",
				};

				var environmentName = beatmapLevel.GetEnvironmentName(beatmapKey.beatmapCharacteristic, beatmapKey.difficulty);
				// Kind of ugly way to get the EnvironmentsListModel but it's either that or changing both patches.
				var customLevelLoader = (CustomLevelLoader)VideoLoader.BeatmapLevelsModel._customLevelLoader;
				var mapEnvironmentInfoSo = customLevelLoader._environmentsListModel.GetEnvironmentInfoBySerializedNameSafe(environmentName);
				if (overrideEnvironmentSettings is { overrideEnvironments: true })
				{
					var overrideEnvironmentInfo = overrideEnvironmentSettings.GetOverrideEnvironmentInfoForType(mapEnvironmentInfoSo.environmentType);
					if (environmentWhitelist.Contains(overrideEnvironmentInfo.serializedName))
					{
						Log.Debug("Environment override by user is in whitelist, allowing override");
						return;
					}
				}

				if (environmentWhitelist.Contains(mapEnvironmentInfoSo.serializedName))
				{
					Log.Debug("Environment chosen by mapper is in whitelist");
					overrideEnvironmentSettings = null!;
					return;
				}

				var bigMirrorOverrideSettings = GetOverrideEnvironmentSettingsFor("BigMirrorEnvironment");
				if (bigMirrorOverrideSettings == null)
				{
					return;
				}

				overrideEnvironmentSettings = bigMirrorOverrideSettings;
				Log.Info("Overwriting environment to Big Mirror");
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}
		}

		private static OverrideEnvironmentSettings? GetOverrideEnvironmentSettingsFor(string serializedName)
		{
			var environmentInfo = GetEnvironmentInfoFor(serializedName);
			if (environmentInfo == null)
			{
				Log.Error($"Could not find environment environment info for {serializedName}");
				return null;
			}

			var overrideSettings = new OverrideEnvironmentSettings {overrideEnvironments = true};
			overrideSettings.SetEnvironmentInfoForType(environmentInfo.environmentType, environmentInfo);
			return overrideSettings;
		}

		private static EnvironmentInfoSO? GetEnvironmentInfoFor(string serializedName)
		{
			return Resources.FindObjectsOfTypeAll<EnvironmentInfoSO>().FirstOrDefault(x => x.serializedName == serializedName);
		}
	}

	[HarmonyPatch]
	[UsedImplicitly]
	// ReSharper disable once InconsistentNaming
	internal static class MissionLevelScenesTransitionSetupDataSOInit
	{
		private static MethodInfo TargetMethod() => AccessTools.FirstMethod(typeof(MissionLevelScenesTransitionSetupDataSO),
			m => m.Name == nameof(MissionLevelScenesTransitionSetupDataSO.Init) &&
			     m.GetParameters().All(p => p.ParameterType != typeof(IBeatmapLevelData)));

		[UsedImplicitly]
		private static void Prefix(BeatmapLevel beatmapLevel, BeatmapKey beatmapKey)
		{
			try
			{
				var overrideSettings = new OverrideEnvironmentSettings();
				StandardLevelScenesTransitionSetupDataSOInit.Prefix(beatmapLevel, beatmapKey, ref overrideSettings);
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}
		}
	}
}