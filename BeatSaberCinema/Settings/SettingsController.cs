using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using JetBrains.Annotations;
// ReSharper disable UnusedMember.Global -- The getter functions are used by BSML

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
		            SetSettingsTexture();
		            PlaybackController.Instance.VideoPlayer.FadeIn(0.3f);
	            }
	            else
	            {
		            PlaybackController.Instance.VideoPlayer.FadeOut(0.3f);
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

        [UIValue("corner-roundness")]
        public int CornerRoundness
        {
	        get => (int) Math.Round(SettingsStore.Instance.CornerRoundness * 100);
	        set
	        {
		        SettingsStore.Instance.CornerRoundness = value / 100f;
		        PlaybackController.Instance.VideoPlayer.SetVignette();
	        }
        }

        [UIValue("curved-screen")]
        public bool CurvedScreen
        {
	        get => SettingsStore.Instance.CurvedScreen;
	        set
	        {
		        SettingsStore.Instance.CurvedScreen = value;
		        if (PluginEnabled)
		        {
			        SetSettingsTexture();
		        }
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

        private void SetSettingsTexture()
        {
	        PlaybackController.Instance.VideoPlayer.SetStaticTexture(Util.LoadPNGFromResources("BeatSaberCinema.Resources.beat-saber-logo-landscape.png"));
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (!SettingsStore.Instance.PluginEnabled)
            {
	            return;
            }

            PlaybackController.Instance.VideoPlayer.FadeIn(0.3f);
            PlaybackController.Instance.StopPlayback();
            SetSettingsTexture();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            try
            {
	            //Throws NRE if the settings menu is open while the plugin gets disabled (e.g. by closing the game)
	            PlaybackController.Instance.VideoPlayer.FadeOut(0.3f);
	            PlaybackController.Instance.VideoPlayer.SetDefaultMenuPlacement();
            }
            catch (Exception e)
            {
	            BeatSaberCinema.Log.Debug(e);
            }
        }
    }
}