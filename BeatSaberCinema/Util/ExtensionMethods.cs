using System.Linq;
using SongCore.Data;

namespace BeatSaberCinema
{
	public static class ExtensionMethods
	{
		public static bool HasCinemaSuggestion(this ExtraSongData.DifficultyData difficultyData)
		{
			return difficultyData.additionalDifficultyData._suggestions.Any(suggestion => suggestion == Plugin.CAPABILITY);
		}

		public static bool HasCinemaRequirement(this ExtraSongData.DifficultyData difficultyData)
		{
			return difficultyData.additionalDifficultyData._requirements.Any(requirement => requirement == Plugin.CAPABILITY);
		}

		public static bool HasCinema(this ExtraSongData.DifficultyData difficultyData)
		{
			return difficultyData.HasCinemaSuggestion() || difficultyData.HasCinemaRequirement();
		}

		public static bool HasCinemaSuggestionInAnyDifficulty(this ExtraSongData songData)
		{
			return songData._difficulties.Any(difficulty => difficulty.HasCinemaSuggestion());
		}

		public static bool HasCinemaRequirementInAnyDifficulty(this ExtraSongData songData)
		{
			return songData._difficulties.Any(difficulty => difficulty.HasCinemaRequirement());
		}

		public static bool HasCinemaInAnyDifficulty(this ExtraSongData songData)
		{
			return songData.HasCinemaSuggestionInAnyDifficulty() || songData.HasCinemaRequirementInAnyDifficulty();
		}
	}
}