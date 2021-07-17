using System.Runtime.CompilerServices;
using IPA.Config.Stores;
using JetBrains.Annotations;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]
namespace BeatSaberCinema
{
    [UsedImplicitly]
    internal class SettingsStore
    {
        public static SettingsStore Instance { get; set; } = null!;
        public virtual bool PluginEnabled { get; set; } = true;
        public virtual bool OverrideEnvironment { get; set; } = true;
        public virtual bool DisableCustomPlatforms { get; set; } = true;
        public virtual bool Enable360Rotation { get; set; } = true;
        public virtual bool CurvedScreen { get; set; } = true;
        public virtual bool TransparencyEnabled { get; set; } = true;
        public virtual bool CoverEnabled { get; set; }
        public virtual int BloomIntensity { get; set; } = 100;
        public virtual float CornerRoundness { get; set; } = 0;
        public virtual VideoQuality.Mode QualityMode { get; set; } = VideoQuality.Mode.Q720P;
        public virtual bool ForceDisableEnvironmentOverrides { get; set; } = false;
    }
}