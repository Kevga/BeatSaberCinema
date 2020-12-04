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
        public bool PlaybackEnabled
        {
            get => SettingsStore.Instance.PlaybackEnabled;
            set
            {
	            SettingsStore.Instance.PlaybackEnabled = value;
	            if (value)
	            {
		            PlaybackController.Instance.ShowScreen();
	            }
	            else
	            {
		            PlaybackController.Instance.HideScreen();
	            }
            }
        }

        [UIValue("override-environment")]
        public bool OverrideEnvironment
        {
	        get => SettingsStore.Instance.OverrideEnvironment;
	        set => SettingsStore.Instance.OverrideEnvironment = value;
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
            if (SettingsStore.Instance.PlaybackEnabled)
            {
	            PlaybackController.Instance.ShowScreen();
            }
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            try
            {
	            //Throws NRE if the settings menu is open while the plugin gets disabled (e.g. by closing the game)
	            PlaybackController.Instance.HideScreen();
            }
            catch (Exception e)
            {
	            Plugin.Logger.Debug(e);
            }
        }
    }
}