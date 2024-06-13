using HarmonyLib;
using JetBrains.Annotations;

// ReSharper disable InconsistentNaming

namespace BeatSaberCinema.Patches
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
			_connectionType = __instance.connectionType;
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
		private static void Prefix(MultiplayerController __instance)
		{
			_playersManager = __instance._playersManager;
			Events.SetSelectedLevel(null);
		}
	}

	[HarmonyPatch(typeof(MultiplayerController), nameof(MultiplayerController.EndGameplay))]
	[UsedImplicitly]
	internal class MultiplayerEndGameplayPatch
	{
		[UsedImplicitly]
		private static void Prefix()
		{
			Events.SetSelectedLevel(null);
		}
	}
}
