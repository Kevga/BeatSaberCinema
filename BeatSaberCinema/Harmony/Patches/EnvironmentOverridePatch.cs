using System;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace BeatSaberCinema.Patches
{
	[HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO), nameof(StandardLevelScenesTransitionSetupDataSO.Init))]
	[UsedImplicitly]
	// ReSharper disable once InconsistentNaming
	internal static class StandardLevelScenesTransitionSetupDataSOInit
	{
		[UsedImplicitly]
		public static void Prefix(IDifficultyBeatmap difficultyBeatmap, ref OverrideEnvironmentSettings overrideEnvironmentSettings)
		{
			//Wrap all of it in try/catch so an exception would not prevent the player from playing songs
			try
			{
				PlaybackController.Instance.SceneTransitionInitCalled();
				VideoMenu.instance.SetSelectedLevel(difficultyBeatmap.level);

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

				var mapEnvironmentInfoSo = difficultyBeatmap.GetEnvironmentInfo();
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

	[HarmonyPatch(typeof(MissionLevelScenesTransitionSetupDataSO), "Init")]
	[UsedImplicitly]
	// ReSharper disable once InconsistentNaming
	internal static class MissionLevelScenesTransitionSetupDataSOInit
	{
		[UsedImplicitly]
		private static void Prefix(IDifficultyBeatmap difficultyBeatmap)
		{
			try
			{
				var overrideSettings = new OverrideEnvironmentSettings();
				StandardLevelScenesTransitionSetupDataSOInit.Prefix(difficultyBeatmap, ref overrideSettings);
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}
		}
	}
}