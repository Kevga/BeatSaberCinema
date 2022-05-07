using System;
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


		/// <summary>
		/// Raises an event, wrapping each delegate in a try/catch.
		/// Exceptions thrown are logged, using <paramref name="eventName"/> to provide the name of the event the exception was thrown from.
		/// Yoinked and adapted from BS_Utils.
		/// </summary>
		public static void InvokeSafe<T>(this Action<T>? e, T arg, string eventName)
		{
			if (e == null)
			{
				return;
			}

			Action<T>[] handlers = e.GetInvocationList().Select(d => (Action<T>)d).ToArray();
			foreach (var handler in handlers)
			{
				try
				{
					handler.Invoke(arg);
				}
				catch (Exception ex)
				{
					Log.Error($"Exception thrown in '{eventName}' handler '{handler.Method.Name}': {ex.Message}");
					Log.Debug(ex);
				}
			}
		}
	}
}