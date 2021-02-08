using System;
using BeatSaberMarkupLanguage;
using HMUI;

namespace BeatSaberCinema
{
    public class SettingsFlowCoordinator: FlowCoordinator
    {
        private SettingsController? _controller;

        public void Awake()
        {
            if (!_controller)
            {
                _controller = BeatSaberUI.CreateViewController<SettingsController>();
            }
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            try
            {
	            if (!firstActivation)
	            {
		            return;
	            }

	            SetTitle("Cinema Settings");
	            showBackButton = true;
	            ProvideInitialViewControllers(_controller);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        protected override void BackButtonWasPressed(ViewController viewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}