using HarmonyLib;
using IPA.Utilities;
using JetBrains.Annotations;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(MultiplayerLobbyConnectionController), nameof(MultiplayerLobbyConnectionController.connectionType), MethodType.Setter)]
	[UsedImplicitly]
	internal class MultiplayerPatch
	{
		private static MultiplayerLobbyConnectionController.LobbyConnectionType? _connectionType;

		public static bool IsMultiplayer => _connectionType != null && _connectionType != MultiplayerLobbyConnectionController.LobbyConnectionType.None;

		/// <summary>
		/// Gets the current lobby type.
		/// </summary>
		[UsedImplicitly]
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

	[HarmonyPatch(typeof(MultiplayerController), nameof(MultiplayerController.StartSceneLoadSync))]
	[UsedImplicitly]
	internal class MultiplayerStartLoadPatch
	{
		private static MultiplayerPlayersManager? _playersManager;

		public static int PlayerCount => _playersManager == null ? 0 : _playersManager.allActiveAtGameStartPlayers.Count;

		[UsedImplicitly]
		private static void Prefix(MultiplayerController __instance, MultiplayerPlayersManager ____playersManager)
		{
			_playersManager = ____playersManager;
			Events.SetSelectedLevel(null);
		}
	}

	[HarmonyPatch(typeof(MultiplayerController), nameof(MultiplayerController.EndGameplay))]
	[UsedImplicitly]
	internal class MultiplayerEndGameplayPatch
	{
		[UsedImplicitly]
		private static void Prefix(MultiplayerController __instance)
		{
			Events.SetSelectedLevel(null);
		}
	}
}
