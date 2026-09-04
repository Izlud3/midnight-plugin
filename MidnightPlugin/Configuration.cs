using Dalamud.Configuration;
using System;

namespace MidnightPlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 14;
    public const int DefaultTimelineHistoryLimit = 30;

    public int Version { get; set; } = CurrentVersion;

    public bool ShowLiveTimeline { get; set; } = true;
    public float TimelineOpacity { get; set; } = 1f;
    public ReferenceAlertScope ReferenceAlertScope { get; set; } = ReferenceAlertScope.Off;
    public float ReferenceAlertLeadSeconds { get; set; } = 6f;
    public bool LockReferenceAlertPosition { get; set; }
    public Dictionary<string, List<string>> ReferenceAlertActionsByJob { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        if (TimelineOpacity is < 0.1f or > 1f)
        {
            TimelineOpacity = 1f;
        }

        if (ReferenceAlertLeadSeconds is < 1f or > 15f)
            ReferenceAlertLeadSeconds = 6f;
        ReferenceAlertActionsByJob ??= new(StringComparer.OrdinalIgnoreCase);

        Version = CurrentVersion;
    }

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

public enum ReferenceAlertScope
{
    Off,
    DancingMad,
    AnyCombat,
}
