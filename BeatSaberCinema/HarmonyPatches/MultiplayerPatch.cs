using HarmonyLib;
using IPA.Utilities;
using JetBrains.Annotations;

namespace BeatSaberCinema
{
    [HarmonyPatch(typeof(MultiplayerLobbyConnectionController), "connectionType", MethodType.Setter)]
    [UsedImplicitly]
    internal class MultiplayerPatch
    {
	    private static MultiplayerLobbyConnectionController.LobbyConnectionType _connectionType;

        public static bool IsMultiplayer => _connectionType != MultiplayerLobbyConnectionController.LobbyConnectionType.None;

        /// <summary>
        /// Gets the current lobby type.
        /// </summary>
        [UsedImplicitly]
        // ReSharper disable once InconsistentNaming
        private static void Prefix(MultiplayerLobbyConnectionController __instance)
        {
	        _connectionType = __instance.GetProperty<MultiplayerLobbyConnectionController.LobbyConnectionType, MultiplayerLobbyConnectionController>("connectionType");
	        if (!IsMultiplayer)
	        {
		        return;
	        }

	        Events.SetSelectedLevel(null);
        }
    }
}
