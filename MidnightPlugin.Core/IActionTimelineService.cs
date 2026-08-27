namespace MidnightPlugin.Core;

public interface IActionTimelineService
{
    IReadOnlyList<TimelineEntry> Snapshot();

    bool TryRecord(uint actionId, DateTimeOffset timestamp);

    void SetCapacity(int capacity);

    void Clear();
}
