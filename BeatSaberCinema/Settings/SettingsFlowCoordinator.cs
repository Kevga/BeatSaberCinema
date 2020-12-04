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
                if (firstActivation)
                {
                    SetTitle("Cinema Settings");
                    showBackButton = true;
                    ProvideInitialViewControllers(_controller);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.Error(ex);
            }
        }

        protected override void BackButtonWasPressed(ViewController viewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}