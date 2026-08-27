using MidnightPlugin.Core;
using Xunit;

namespace MidnightPlugin.Tests;

public sealed class DiagnosticLogTests
{
    [Fact]
    public void BufferRetainsOnlyTheNewestEntries()
    {
        var buffer = new DiagnosticLogBuffer(2);

        buffer.Add(new DiagnosticLogEntry(DateTimeOffset.UtcNow, "Capture", 1, "first"));
        buffer.Add(new DiagnosticLogEntry(DateTimeOffset.UtcNow, "Capture", 2, "second"));
        buffer.Add(new DiagnosticLogEntry(DateTimeOffset.UtcNow, "Capture", 3, "third"));

        Assert.Equal([2u, 3u], buffer.Snapshot().Select(entry => entry.ActionId));
    }

    [Fact]
    public void AddOnceSuppressesRepeatedSessionEntries()
    {
        var buffer = new DiagnosticLogBuffer(10);
        var entry = new DiagnosticLogEntry(DateTimeOffset.UtcNow, "Metadata", 100, "unknown");

        Assert.True(buffer.AddOnce("metadata:100", entry));
        Assert.False(buffer.AddOnce("metadata:100", entry));
        Assert.Single(buffer.Snapshot());
    }

    [Fact]
    public void ReplaceLoadsOnlyTheNewestEntriesWhenInputExceedsCapacity()
    {
        var buffer = new DiagnosticLogBuffer(2);

        buffer.Replace(
        [
            new DiagnosticLogEntry(DateTimeOffset.UtcNow, "Lifecycle", null, "one"),
            new DiagnosticLogEntry(DateTimeOffset.UtcNow, "Lifecycle", null, "two"),
            new DiagnosticLogEntry(DateTimeOffset.UtcNow, "Lifecycle", null, "three"),
        ]);

        Assert.Equal(["two", "three"], buffer.Snapshot().Select(entry => entry.Message));
    }
}
