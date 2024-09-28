using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;

namespace BeatSaberCinema
{
    internal static class SettingsUI
    {
        private static readonly MenuButton MenuButton = new MenuButton("Cinema", "Cinema Settings", ShowFlow);

        private static SettingsFlowCoordinator? _flowCoordinator;

        public static void CreateMenu()
        {
	        MenuButtons.Instance.RegisterButton(MenuButton);
        }

        public static void RemoveMenu()
        {
	        MenuButtons.Instance.UnregisterButton(MenuButton);
        }

        private static void ShowFlow()
        {
            if (_flowCoordinator == null)
            {
                _flowCoordinator = BeatSaberUI.CreateFlowCoordinator<SettingsFlowCoordinator>();
            }

            BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(_flowCoordinator);
        }
    }
}