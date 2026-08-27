namespace MidnightPlugin.Core;

public sealed class ActionTimelineService : IActionTimelineService
{
    private readonly object syncRoot = new();
    private readonly Queue<TimelineEntry> entries;

    public ActionTimelineService(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Timeline capacity must be greater than zero.");
        }

        Capacity = capacity;
        entries = new Queue<TimelineEntry>(capacity);
    }

    public int Capacity { get; private set; }

    public bool TryRecord(uint actionId, DateTimeOffset timestamp)
    {
        if (actionId == 0)
        {
            return false;
        }

        lock (syncRoot)
        {
            if (entries.Count == Capacity)
            {
                entries.Dequeue();
            }

            entries.Enqueue(new TimelineEntry(actionId, timestamp));
            return true;
        }
    }

    public IReadOnlyList<TimelineEntry> Snapshot()
    {
        lock (syncRoot)
        {
            return entries.ToArray();
        }
    }

    public void SetCapacity(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Timeline capacity must be greater than zero.");
        }

        lock (syncRoot)
        {
            Capacity = capacity;
            while (entries.Count > capacity)
            {
                entries.Dequeue();
            }
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            entries.Clear();
        }
    }
}
