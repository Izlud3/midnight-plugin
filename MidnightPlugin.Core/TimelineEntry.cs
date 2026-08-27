namespace MidnightPlugin.Core;

public readonly record struct TimelineEntry(uint ActionId, DateTimeOffset Timestamp);
