using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;

namespace BeatSaberCinema
{
    internal class SettingsUI
    {
        private static readonly MenuButton MenuButton = new MenuButton("Cinema", "Cinema Settings", ShowFlow);

        public static SettingsFlowCoordinator? FlowCoordinator;
        public static bool Created;

        public static void CreateMenu()
        {
            if (!Created)
            {
                MenuButtons.instance.RegisterButton(MenuButton);
                Created = true;
            }
        }

        public static void RemoveMenu()
        {
            if (Created)
            {
                MenuButtons.instance.UnregisterButton(MenuButton);
                Created = false;
            }
        }

        public static void ShowFlow()
        {
            if (FlowCoordinator == null)
            {
                FlowCoordinator = BeatSaberUI.CreateFlowCoordinator<SettingsFlowCoordinator>();
            }

            BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(FlowCoordinator);
        }
    }
}