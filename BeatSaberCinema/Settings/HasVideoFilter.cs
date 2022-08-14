using System.Threading;
using System.Threading.Tasks;
using BetterSongList.FilterModels;
using BetterSongList.Interfaces;

namespace BeatSaberCinema
{
	public class HasVideoFilter : IFilter, ITransformerPlugin
	{
		public bool isReady => true;
		public string name => "Cinema";
		public bool visible { get; } = Plugin.Enabled && SettingsStore.Instance.PluginEnabled;

		public bool GetValueFor(IPreviewBeatmapLevel level)
		{
			return VideoLoader.MapsWithVideo.TryGetValue(level.levelID, out _);
		}

		public Task Prepare(CancellationToken cancelToken)
		{
			return Task.CompletedTask;
		}

		public void ContextSwitch(SelectLevelCategoryViewController.LevelCategory levelCategory, IAnnotatedBeatmapLevelCollection? playlist)
		{
			//Not needed
		}
	}
}