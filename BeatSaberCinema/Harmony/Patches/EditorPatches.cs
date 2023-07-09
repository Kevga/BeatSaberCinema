using System.IO;
using BeatmapEditor.Commands;
using BeatmapEditor3D;
using BeatmapEditor3D.BpmEditor;
using BeatmapEditor3D.BpmEditor.Commands;
using BeatmapEditor3D.DataModels;
using HarmonyLib;
using JetBrains.Annotations;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
{
	[HarmonyPatch(typeof(BeatmapProjectManager), nameof(BeatmapProjectManager.LoadBeatmapProject))]
	public class EditorSelectionPatch
	{
		[UsedImplicitly]
		public static void Postfix(BeatmapProjectManager __instance, IBeatmapDataModel ____beatmapDataModel)
		{
			try
			{
				var originalPath = __instance._originalBeatmapProject;
				Events.SetSelectedLevel(____beatmapDataModel, originalPath);
			} catch (System.Exception e)
			{
				Log.Error(e);
				Events.SetSelectedLevel(null);
			}
		}
	}

	[HarmonyPatch(typeof(SetPlayHeadCommand), nameof(SetPlayHeadCommand.Execute))]
	public class SetPlayHead
	{
		[UsedImplicitly]
		public static void Prefix(SetPlayHeadSignal ____signal, BpmEditorSongPreviewController ____bpmEditorSongPreviewController)
		{
			var mapTime = AudioTimeHelper.SamplesToSeconds(____signal.sample, ____bpmEditorSongPreviewController.audioClip.frequency);
			PlaybackController.Instance.ResyncVideo(mapTime != 0 ? mapTime : (float?) null);
		}
	}

	[HarmonyPatch(typeof(UpdatePlayHeadCommand), nameof(UpdatePlayHeadCommand.Execute))]
	public class UpdatePlayHead
	{
		[UsedImplicitly]
		public static void Prefix(UpdatePlayHeadSignal ____signal, IBeatmapDataModel ____beatmapDataModel)
		{
			if (PlaybackController.Instance.VideoPlayer.IsPrepared && !PlaybackController.Instance.VideoPlayer.IsPlaying)
			{
				var mapTime = AudioTimeHelper.SamplesToSeconds(____signal.sample, ____beatmapDataModel.audioClip.frequency);
				PlaybackController.Instance.ResyncVideo(mapTime != 0 ? mapTime : (float?) null);
				PlaybackController.Instance.VideoPlayer.UpdateScreenContent();
			}
		}
	}

	[HarmonyPatch(typeof(UpdatePlaybackSpeedCommand), nameof(UpdatePlaybackSpeedCommand.Execute))]
	public class UpdatePlaybackSpeed
	{
		[UsedImplicitly]
		public static void Prefix(UpdatePlaybackSpeedSignal ____signal)
		{
			//TODO: Breaks stuff. May be related to that Unity bug.
			//PlaybackController.Instance.ResyncVideo(null, ____signal.playbackSpeed);
		}
	}

	[HarmonyPatch(typeof(BeatmapProjectManager), nameof(BeatmapProjectManager.SaveBeatmapLevel))]
	public class SavingFixPatch
	{
		[UsedImplicitly]
		public static void Postfix(bool clearDirty)
		{
			if (!clearDirty || PlaybackController.Instance.VideoConfig == null)
			{
				return;
			}

			VideoLoader.StopFileSystemWatcher();
			var config = PlaybackController.Instance.VideoConfig;
			Log.Info("Editor is creating backup, path: "+config.ConfigPath);
			if (File.Exists(config.ConfigPath))
			{
				return;
			}

			Log.Info("Restoring config...");
			VideoLoader.SaveVideoConfig(config);
		}
	}

	[HarmonyPatch(typeof(BeatmapProjectManager), nameof(BeatmapProjectManager.SaveBeatmapProject))]
	public class RestoreConfigPatch
	{
		[UsedImplicitly]
		public static void Postfix(string ____lastBackup, bool clearDirty)
		{
			if (!clearDirty || PlaybackController.Instance.VideoConfig == null)
			{
				return;
			}

			Log.Debug("Editor save complete");
		}
	}
}