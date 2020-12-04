using HarmonyLib;
using IPA.Utilities;
using JetBrains.Annotations;

// See https://github.com/pardeike/Harmony/wiki for a full reference on Harmony.
namespace BeatSaberCinema
{
    [HarmonyPatch(typeof(MultiplayerLobbyConnectionController), "connectionType", MethodType.Setter)]
    [UsedImplicitly]
    internal class MultiplayerPatch
    {
        public static MultiplayerLobbyConnectionController.LobbyConnectionType ConnectionType;

        public static bool IsMultiplayer => ConnectionType != MultiplayerLobbyConnectionController.LobbyConnectionType.None;

        /// <summary>
        /// Gets the current lobby type.
        /// </summary>
        [UsedImplicitly]
        // ReSharper disable once InconsistentNaming
        private static void Prefix(MultiplayerLobbyConnectionController __instance)
        {
            ConnectionType = __instance.GetProperty<MultiplayerLobbyConnectionController.LobbyConnectionType, MultiplayerLobbyConnectionController>("connectionType");
            if (IsMultiplayer)
            {
                VideoMenu.instance.RemoveTab();
            }
            else
            {
	            VideoMenu.instance.AddTab();
            }
        }
    }
}
