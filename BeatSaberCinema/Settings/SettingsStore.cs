using System;
using System.Runtime.CompilerServices;
using BS_Utils.Utilities;
using IPA.Config.Stores;
using JetBrains.Annotations;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]
namespace BeatSaberCinema
{
    [UsedImplicitly]
    internal class SettingsStore
    {
        public static SettingsStore Instance { get; set; } = null!;
        public virtual bool PlaybackEnabled { get; set; } = true;
        public virtual bool OverrideEnvironment { get; set; } = true;
        public virtual bool CurvedScreen { get; set; } = true;
        public virtual bool TransparencyEnabled { get; set; }
        public virtual VideoQuality.Mode QualityMode { get; set; } = VideoQuality.Mode.Q720P;
    }
}