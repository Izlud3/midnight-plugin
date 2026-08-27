using MidnightPlugin.Core;
using Xunit;

namespace MidnightPlugin.Tests;

public sealed class ActionTimelineServiceTests
{
    [Fact]
    public void TryRecordRejectsZeroActionId()
    {
        var service = new ActionTimelineService(3);

        Assert.False(service.TryRecord(0, DateTimeOffset.UtcNow));
        Assert.Empty(service.Snapshot());
    }

    [Fact]
    public void SnapshotPreservesChronologicalInsertionOrder()
    {
        var service = new ActionTimelineService(3);
        var first = DateTimeOffset.UtcNow;
        var second = first.AddMilliseconds(250);

        service.TryRecord(100, first);
        service.TryRecord(200, second);

        Assert.Equal(
            [new TimelineEntry(100, first), new TimelineEntry(200, second)],
            service.Snapshot());
    }

    [Fact]
    public void OldestEntryIsRemovedWhenCapacityIsReached()
    {
        var service = new ActionTimelineService(2);
        var timestamp = DateTimeOffset.UtcNow;

        service.TryRecord(100, timestamp);
        service.TryRecord(200, timestamp.AddSeconds(1));
        service.TryRecord(300, timestamp.AddSeconds(2));

        Assert.Equal(
            [new TimelineEntry(200, timestamp.AddSeconds(1)), new TimelineEntry(300, timestamp.AddSeconds(2))],
            service.Snapshot());
    }

    [Fact]
    public void ClearRemovesAllEntries()
    {
        var service = new ActionTimelineService(3);
        service.TryRecord(100, DateTimeOffset.UtcNow);

        service.Clear();

        Assert.Empty(service.Snapshot());
    }

    [Fact]
    public void SetCapacityTrimsOldestEntries()
    {
        var service = new ActionTimelineService(3);
        var timestamp = DateTimeOffset.UtcNow;

        service.TryRecord(100, timestamp);
        service.TryRecord(200, timestamp.AddSeconds(1));
        service.TryRecord(300, timestamp.AddSeconds(2));
        service.SetCapacity(2);

        Assert.Equal(
            [new TimelineEntry(200, timestamp.AddSeconds(1)), new TimelineEntry(300, timestamp.AddSeconds(2))],
            service.Snapshot());
    }

    [Fact]
    public void ConstructorRejectsInvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionTimelineService(0));
    }
}
