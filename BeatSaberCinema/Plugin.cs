using System;
using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using JetBrains.Annotations;

namespace BeatSaberCinema
{
	[Plugin(RuntimeOptions.DynamicInit)]
	[UsedImplicitly]
	public class Plugin
	{
		private const string HARMONY_ID = "com.github.kevga.cinema";
		private const string CAPABILITY = "Cinema";
		private Harmony _harmonyInstance = null!;
		public static bool Enabled;
		internal static Logger Logger = null!;

		[Init]
		[UsedImplicitly]
		public void Init(IPA.Logging.Logger ipaLogger, Config config)
		{
			Logger = new Logger(ipaLogger);
			SettingsStore.Instance = config.Generated<SettingsStore>();
			VideoMenu.instance.AddTab(); //Do this in Init (asap) to get a good spot in the menu
			Logger.Debug("Plugin initialized");
		}

		[OnStart]
		[UsedImplicitly]
		public void OnApplicationStart()
		{
			BS_Utils.Utilities.BSEvents.OnLoad();
			BS_Utils.Utilities.BSEvents.lateMenuSceneLoadedFresh += OnMenuSceneLoadedFresh;
			_harmonyInstance = new Harmony(HARMONY_ID);
			VideoLoader.Init();
		}

		private static void OnMenuSceneLoadedFresh(ScenesTransitionSetupDataSO scenesTransition)
		{
			PlaybackController.Create();
			VideoMenu.instance.Init();
		}

		[OnEnable]
		[UsedImplicitly]
		public void OnEnable()
		{
			Enabled = true;
			ApplyHarmonyPatches();
			SettingsUI.CreateMenu();
			VideoMenu.instance.AddTab();
			SongCore.Collections.RegisterCapability(CAPABILITY);
			Logger.Info($"{nameof(BeatSaberCinema)} enabled");
		}

		[OnDisable]
		[UsedImplicitly]
		public void OnDisable()
		{
			Enabled = false;
			RemoveHarmonyPatches();
			SettingsUI.RemoveMenu();
			VideoMenu.instance.RemoveTab();
			VideoLoader.StopFileSystemWatcher();
			SongCore.Collections.DeregisterizeCapability(CAPABILITY);
			Logger.Info($"{nameof(BeatSaberCinema)} disabled");
		}

		private void ApplyHarmonyPatches()
		{
			try
			{
				Logger.Debug("Applying Harmony patches");
				_harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
			}
			catch (Exception ex)
			{
				Logger.Error("Error applying Harmony patches: " + ex.Message);
				Logger.Debug(ex);
			}
		}

		private void RemoveHarmonyPatches()
		{
			try
			{
				Logger.Debug("Removing Harmony patches");
				_harmonyInstance.UnpatchAll(HARMONY_ID);
			}
			catch (Exception ex)
			{
				Logger.Error("Error removing Harmony patches: " + ex.Message);
				Logger.Debug(ex);
			}
		}
	}
}