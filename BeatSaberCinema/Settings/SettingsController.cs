using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using JetBrains.Annotations;

namespace BeatSaberCinema
{
    public class SettingsController: BSMLResourceViewController
    {
        public override string ResourceName => "BeatSaberCinema.Settings.Views.settings.bsml";
        [UIValue("modes")] [UsedImplicitly] private List<object> _qualityModes = VideoQuality.GetModeList();

        [UIValue("show-video")]
        public bool PluginEnabled
        {
            get => SettingsStore.Instance.PluginEnabled;
            set
            {
	            SettingsStore.Instance.PluginEnabled = value;
	            if (value)
	            {
		            PlaybackController.Instance.VideoPlayer.Show();
	            }
	            else
	            {
		            PlaybackController.Instance.VideoPlayer.Hide();
	            }
            }
        }

        [UIValue("override-environment")]
        public bool OverrideEnvironment
        {
	        get => SettingsStore.Instance.OverrideEnvironment;
	        set => SettingsStore.Instance.OverrideEnvironment = value;
        }

        [UIValue("disable-custom-platforms")]
        public bool DisableCustomPlatforms
        {
	        get => SettingsStore.Instance.DisableCustomPlatforms;
	        set => SettingsStore.Instance.DisableCustomPlatforms = value;
        }

        [UIValue("bloom-intensity")]
        public int BloomIntensity
        {
	        get => SettingsStore.Instance.BloomIntensity;
	        set => SettingsStore.Instance.BloomIntensity = value;
        }

        [UIValue("curved-screen")]
        public bool CurvedScreen
        {
	        get => SettingsStore.Instance.CurvedScreen;
	        set
	        {
		        SettingsStore.Instance.CurvedScreen = value;
		        PlaybackController.Instance.VideoPlayer.SetDefaultMenuPlacement();
	        }
        }

        [UIValue("transparency-enabled")]
        public bool TransparencyEnabled
        {
	        get => SettingsStore.Instance.TransparencyEnabled;
	        set
	        {
		        SettingsStore.Instance.TransparencyEnabled = value;
		        if (value)
		        {
			        PlaybackController.Instance.VideoPlayer.HideScreenBody();
		        }
		        else
		        {
			        PlaybackController.Instance.VideoPlayer.ShowScreenBody();
		        }

	        }
        }

        [UIValue("cover-enabled")]
        public bool CoverEnabled
        {
	        get => SettingsStore.Instance.CoverEnabled;
	        set => SettingsStore.Instance.CoverEnabled = value;
        }

        [UIValue("quality")]
        public string QualityMode
        {
            get => VideoQuality.ToName(SettingsStore.Instance.QualityMode);
            set => SettingsStore.Instance.QualityMode = VideoQuality.FromName(value);
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (!SettingsStore.Instance.PluginEnabled)
            {
	            return;
            }

            PlaybackController.Instance.VideoPlayer.Show();
            PlaybackController.Instance.VideoPlayer.SetDefaultMenuPlacement();
            PlaybackController.Instance.StopPlayback();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            try
            {
	            //Throws NRE if the settings menu is open while the plugin gets disabled (e.g. by closing the game)
	            PlaybackController.Instance.VideoPlayer.Hide();
	            PlaybackController.Instance.VideoPlayer.SetDefaultMenuPlacement();
            }
            catch (Exception e)
            {
	            Plugin.Logger.Debug(e);
            }
        }
    }
}