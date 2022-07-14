using BeatmapEditor3D.DataModels;

namespace BeatSaberCinema
{
	public class LevelSelectedArgs
	{
		public readonly IPreviewBeatmapLevel? PreviewBeatmapLevel;
		public readonly IBeatmapDataModel? BeatmapData;
		public readonly string? OriginalPath;

		public LevelSelectedArgs(IPreviewBeatmapLevel? level, IBeatmapDataModel? beatmapData = null, string? originalPath = null)
		{
			PreviewBeatmapLevel = level;
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