using System.IO;
using BeatmapEditor.Commands;
using BeatmapEditor3D;
using BeatmapEditor3D.BpmEditor;
using BeatmapEditor3D.BpmEditor.Commands;
using BeatmapEditor3D.Controller;
using BeatmapEditor3D.DataModels;
using HarmonyLib;
using IPA.Utilities.Async;
using JetBrains.Annotations;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
{
	[HarmonyPatch(typeof(EditBeatmapLevelViewController), "DidActivate")]
	public class EditBeatmapLevelViewControllerDidActivate
	{
		[UsedImplicitly]
		public static void Postfix()
		{
			Log.Debug("EditBeatmapLevelViewControllerDidActivate");
			//PlaybackController.Instance.GameSceneLoaded();
		}
	}

	[HarmonyPatch(typeof(EditBeatmapLevelViewController),"TogglePlayPause")]
	public class EditBeatmapLevelViewControllerPlayPause
	{
		[UsedImplicitly]
		public static void Prefix(ISongPreviewController ____songPreviewController)
		{
			//TODO: This method doesn't seem to be called at all
			if (____songPreviewController.isPlaying)
			{
				PlaybackController.Instance.PauseVideo();
			}
			else
			{
				PlaybackController.Instance.ResumeVideo();
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

	[HarmonyPatch(typeof(BeatmapProjectManager), nameof(BeatmapProjectManager.SaveBeatmapProject))]
	public class SavingFixPatch
	{
		internal static FileSystemWatcher? deletionFSWatcher;

		[UsedImplicitly]
		public static void Prefix(bool clearDirty)
		{
			deletionFSWatcher?.Dispose();

			if (!clearDirty || PlaybackController.Instance.VideoConfig == null)
			{
				return;
			}

			Log.Info("Editor is creating backup...");
			VideoLoader.StopFileSystemWatcher();
			var config = PlaybackController.Instance.VideoConfig;
			deletionFSWatcher = new FileSystemWatcher();
			deletionFSWatcher.Path = Path.GetDirectoryName(config.ConfigPath);
			deletionFSWatcher.Filter = Path.GetFileName(config.ConfigPath);
			deletionFSWatcher.EnableRaisingEvents = true;

			deletionFSWatcher.Deleted -= configFileDeleted;
			deletionFSWatcher.Deleted += configFileDeleted;
		}

		private static void configFileDeleted(object sender, FileSystemEventArgs e)
		{
			UnityMainThreadTaskScheduler.Factory.StartNew(onConfigDeleted);
		}

		private static void onConfigDeleted()
		{
			Log.Debug("Restoring config and fs watcher after editor save...");
			var config = PlaybackController.Instance.VideoConfig;
			if (config == null)
			{
				Log.Debug("Config was null");
				return;
			}
			config.NeedsToSave = true;
			VideoLoader.SaveVideoConfig(config);
			deletionFSWatcher?.Dispose();
			if (config.videoFile != null && config.VideoPath != null)
			{
				VideoLoader.SetupFileSystemWatcher(config.VideoPath);
			}
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