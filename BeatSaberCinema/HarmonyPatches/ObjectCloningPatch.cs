using HarmonyLib;
using JetBrains.Annotations;

// ReSharper disable once InconsistentNaming
namespace BeatSaberCinema
{
	[HarmonyPatch(typeof(AudioTimeSyncController), "Start")]
	[UsedImplicitly]

	internal static class AudioTimeSyncControllerStart
	{
		[UsedImplicitly]
		public static void Postfix(AudioTimeSyncController __instance)
		{
			//Why we clone here: The ATSC starts after all the lights have been registered and before Chroma grabs a list of all the lights.
			//This ensures the newly cloned objects/lights don't change the lightIDs of existing lights and also that the cloned lights are registered before Chroma indexes them
			EnvironmentController.CloneObjects(PlaybackController.Instance.VideoConfig);
			Log.Debug("Started "+__instance.name);
		}
	}
}