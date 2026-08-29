using Dalamud.Configuration;
using System;

namespace MidnightPlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 13;
    public const int DefaultTimelineHistoryLimit = 30;

    public int Version { get; set; } = CurrentVersion;

    public bool ShowLiveTimeline { get; set; } = true;
    public float TimelineOpacity { get; set; } = 1f;

    public void Normalize()
    {
        if (TimelineOpacity is < 0.1f or > 1f)
        {
            TimelineOpacity = 1f;
        }

        Version = CurrentVersion;
    }

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
