using System;
using System.Linq;
using CustomJSONData;
using CustomJSONData.CustomBeatmap;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace BeatSaberCinema
{
	[HarmonyBefore("com.kyle1413.BeatSaber.BS-Utils")]
	[HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO), "Init")]
	[UsedImplicitly]
	// ReSharper disable once InconsistentNaming
	internal static class StandardLevelScenesTransitionSetupDataSOInit
	{
		[UsedImplicitly]
		private static void Prefix(IDifficultyBeatmap difficultyBeatmap, ref OverrideEnvironmentSettings overrideEnvironmentSettings)
		{
			//Wrap all of it in try/catch so an exception would not prevent the player from playing songs
			try
			{
				var overrideEnvironmentEnabled = SettingsStore.Instance.OverrideEnvironment;

				var environmentInfoSo = difficultyBeatmap.GetEnvironmentInfo();

				// ReSharper disable once ConditionIsAlwaysTrueOrFalse
				if (overrideEnvironmentSettings != null && overrideEnvironmentSettings.overrideEnvironments)
				{
					environmentInfoSo = overrideEnvironmentSettings.GetOverrideEnvironmentInfoForType(environmentInfoSo.environmentType);
				}

				var environmentWhitelist = new[] {"BigMirrorEnvironment", "OriginsEnvironment", "BTSEnvironment", "KDAEnvironment", "RocketEnvironment", "DragonsEnvironment"};
				if (environmentWhitelist.Contains(environmentInfoSo.serializedName))
				{
					overrideEnvironmentEnabled = false;
				}

				var video = VideoLoader.GetConfigForLevel(difficultyBeatmap.level);
				if (video == null || !video.IsPlayable)
				{
					overrideEnvironmentEnabled = false;
				} else if (video.disableBigMirrorOverride != null && video.disableBigMirrorOverride == true)
				{
					overrideEnvironmentEnabled = false;
				}

				//Disable override if Chroma needs a specific environment for the map
				if (difficultyBeatmap.beatmapData is CustomBeatmapData customBeatmapData)
				{
					if (Trees.at(customBeatmapData.beatmapCustomData, "_environmentRemoval") != null)
					{
						overrideEnvironmentEnabled = false;
					}
				}

				if (!overrideEnvironmentEnabled)
				{
					Plugin.Logger.Debug("Skipping environment override");
					return;
				}

				var bigMirrorEnvInfo = Resources.FindObjectsOfTypeAll<EnvironmentInfoSO>().First(x => x.serializedName == "BigMirrorEnvironment");
				if (bigMirrorEnvInfo == null)
				{
					Plugin.Logger.Warn("Did not find big mirror env");
					return;
				}

				var bigMirrorOverrideSettings = new OverrideEnvironmentSettings {overrideEnvironments = true};
				bigMirrorOverrideSettings.SetEnvironmentInfoForType(bigMirrorEnvInfo.environmentType, bigMirrorEnvInfo);
				overrideEnvironmentSettings = bigMirrorOverrideSettings;
				Plugin.Logger.Info("Overwriting environment to Big Mirror");
			}
			catch (Exception e)
			{
				Plugin.Logger.Warn(e);
			}
		}
	}
}