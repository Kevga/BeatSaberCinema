namespace BeatSaberCinema
{
	public class LevelSelectedArgs
	{
		public readonly IPreviewBeatmapLevel? PreviewBeatmapLevel;

		public LevelSelectedArgs(IPreviewBeatmapLevel? level)
		{
			PreviewBeatmapLevel = level;
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