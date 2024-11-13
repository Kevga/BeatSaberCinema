using System.Linq;
using IPA.Loader;
using HiveVersion = Hive.Versioning.Version;

namespace BeatSaberCinema
{
	/// <summary>
	/// Declares a set of soft dependencies by whether they are installed.
	///	true: is installed / false: is not installed
	/// </summary>
	internal static class InstalledMods
	{
		public static bool BetterSongList { get; } = IsModInstalled("BetterSongList", "0.3.2");
		public static bool BeatSaberPlaylistsLib { get; } = IsModInstalled("BeatSaberPlaylistsLib", "1.7.0");
		public static bool CustomPlatforms { get; } = IsModInstalled("CustomPlatforms");
		public static bool MusicVideoPlayer { get; } = IsModInstalled("Music Video Player");
		public static bool Heck { get; } = IsModInstalled("_Heck");

		private static bool IsModInstalled(string modId, string? minimumVersion = null)
		{
			return minimumVersion == null ? PluginManager.EnabledPlugins.Any(x => x.Id == modId)
				: PluginManager.EnabledPlugins.Any(x => x.Id == modId && x.HVersion >= new HiveVersion(minimumVersion));
		}
	}
}