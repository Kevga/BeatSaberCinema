using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;

namespace BeatSaberCinema
{
    internal static class SettingsUI
    {
        private static readonly MenuButton MenuButton = new MenuButton("Cinema", "Cinema Settings", ShowFlow);

        private static SettingsFlowCoordinator? _flowCoordinator;
        private static bool _created;

        public static void CreateMenu()
        {
	        if (_created)
	        {
		        return;
	        }

	        MenuButtons.Instance.RegisterButton(MenuButton);
	        _created = true;
        }

        public static void RemoveMenu()
        {
	        if (!_created)
	        {
		        return;
	        }

	        MenuButtons.Instance.UnregisterButton(MenuButton);
	        _created = false;
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