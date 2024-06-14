using BeatmapEditor3D.DataModels;

namespace BeatSaberCinema
{
	public class LevelSelectedArgs
	{
		public readonly BeatmapLevel? BeatmapLevel;
		public readonly BeatmapDataModel? BeatmapData;
		public readonly string? OriginalPath;

		public LevelSelectedArgs(BeatmapLevel? level, BeatmapDataModel? beatmapData = null, string? originalPath = null)
		{
			BeatmapLevel = level;
			BeatmapData = beatmapData;
			OriginalPath = originalPath;
		}
	}

	public class ExtraSongDataArgs
	{
		public readonly SongCore.Data.ExtraSongData? SongData;
		public readonly SongCore.Data.ExtraSongData.DifficultyData? SelectedDifficultyData;

		public ExtraSongDataArgs(SongCore.Data.ExtraSongData? songData, SongCore.Data.ExtraSongData.DifficultyData? selectedDifficultyData)
		{
			SongData = songData;
			SelectedDifficultyData = selectedDifficultyData;
		}
	}
}