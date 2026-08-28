using Dalamud.Configuration;
using System;

namespace MidnightPlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 12;
    public const int DefaultTimelineHistoryLimit = 30;

    public int Version { get; set; } = CurrentVersion;

    public bool ForsakenFailureCardsEnabled { get; set; } = true;
    public bool ShowLiveTimeline { get; set; } = true;
    public bool StopOnMistake { get; set; }
    public float TimelineOpacity { get; set; } = 1f;

    // Retained only so existing saved configurations migrate to StopOnMistake.
    public bool StopAndMinimizeOnMistake { get; set; }

    public void Normalize()
    {
        if (StopAndMinimizeOnMistake)
        {
            StopOnMistake = true;
            StopAndMinimizeOnMistake = false;
        }

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
