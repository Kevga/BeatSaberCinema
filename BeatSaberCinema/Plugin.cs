using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPA.Utilities;
using JetBrains.Annotations;

namespace BeatSaberCinema
{
	[Plugin(RuntimeOptions.DynamicInit)]
	[UsedImplicitly]
	public class Plugin
	{
		private const string HARMONY_ID = "com.github.kevga.cinema";
		internal const string CAPABILITY = "Cinema";
		private Harmony _harmonyInstance = null!;
		public static bool Enabled;

		[Init]
		[UsedImplicitly]
		public void Init(IPA.Logging.Logger ipaLogger, Config config)
		{
			Log.IpaLogger = ipaLogger;
			SettingsStore.Instance = config.Generated<SettingsStore>();
			VideoMenu.instance.AddTab();
			Log.Debug("Plugin initialized");
		}

		[OnStart]
		[UsedImplicitly]
		public void OnApplicationStart()
		{
			BS_Utils.Utilities.BSEvents.OnLoad();
			_harmonyInstance = new Harmony(HARMONY_ID);
			VideoLoader.Init();
		}

		private static void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO scenesTransition)
		{
			PlaybackController.Create();
			VideoMenu.instance.Init();
			SongPreviewPlayerController.Init();
		}

		[OnEnable]
		[UsedImplicitly]
		public void OnEnable()
		{
			Enabled = true;
			BS_Utils.Utilities.BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
			ApplyHarmonyPatches();
			SettingsUI.CreateMenu();
			VideoMenu.instance.AddTab();
			EnvironmentController.Init();
			SongCore.Collections.RegisterCapability(CAPABILITY);
			Log.Info($"{nameof(BeatSaberCinema)} enabled");
			if (File.Exists(Path.Combine(UnityGame.InstallPath, "dxgi.dll")))
			{
				Log.Warn("dxgi.dll is present, video may fail to play. To fix this, delete the file dxgi.dll from your main Beat Saber folder (not in Plugins).");
			}
		}

		[OnDisable]
		[UsedImplicitly]
		public void OnDisable()
		{
			Enabled = false;
			BS_Utils.Utilities.BSEvents.lateMenuSceneLoadedFresh -= OnMenuSceneLoadedFresh;
			RemoveHarmonyPatches();
			SettingsUI.RemoveMenu();

			//TODO Destroying and re-creating the PlaybackController messes up the VideoMenu without any exceptions in the log. Investigate.
			//PlaybackController.Destroy();

			VideoMenu.instance.RemoveTab();
			EnvironmentController.Disable();
			VideoLoader.StopFileSystemWatcher();
			SongCore.Collections.DeregisterizeCapability(CAPABILITY);
			Log.Info($"{nameof(BeatSaberCinema)} disabled");
		}

		private void ApplyHarmonyPatches()
		{
			try
			{
				Log.Debug("Applying Harmony patches");
				_harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
			}
			catch (Exception ex)
			{
				Log.Error("Error applying Harmony patches: " + ex.Message);
				Log.Debug(ex);
			}
		}

		private void RemoveHarmonyPatches()
		{
			try
			{
				Log.Debug("Removing Harmony patches");
				_harmonyInstance.UnpatchAll(HARMONY_ID);
			}
			catch (Exception ex)
			{
				Log.Error("Error removing Harmony patches: " + ex.Message);
				Log.Debug(ex);
			}
		}
	}
}