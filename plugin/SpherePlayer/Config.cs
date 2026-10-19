using System.Collections.Generic;
using Dalamud.Configuration;

namespace SpherePlayer;

public enum PlayMode { Random, Omakase }

[System.Serializable]
public sealed class Config : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public bool Enabled { get; set; } = true;
    public int Volume { get; set; } = 30;
    public float WindowAlpha { get; set; } = 0.8f;
    public PlayMode Mode { get; set; } = PlayMode.Omakase;
    public bool MuteGameMusic { get; set; }
    public bool KeepPlayingHidden { get; set; } = true;
    public float FadeSeconds { get; set; } = 2.5f;
    public bool ResumeArea { get; set; } = true;
    public bool MediaKeys { get; set; } = true;
    public bool ClickableVideo { get; set; } = true;
    public int VideoWidth { get; set; } = 356;
    public HashSet<string> DisabledGames { get; set; } = new();
    public HashSet<string> BannedSongs { get; set; } = new();
    public Dictionary<Category, string> ForcedSongs { get; set; } = new();
}
