namespace MidnightPlugin.Core;

public readonly record struct DiagnosticLogEntry(
    DateTimeOffset Timestamp,
    string Stage,
    uint? ActionId,
    string Message);

public sealed class DiagnosticLogBuffer
{
    private readonly object syncRoot = new();
    private readonly List<DiagnosticLogEntry> entries = [];
    private readonly List<string?> entryKeys = [];
    private readonly HashSet<string> onceKeys = [];

    public DiagnosticLogBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Diagnostic log capacity must be greater than zero.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public void Add(DiagnosticLogEntry entry)
    {
        lock (syncRoot)
        {
            entries.Add(entry);
            entryKeys.Add(null);
            TrimToCapacity();
        }
    }

    public bool AddOnce(string key, DiagnosticLogEntry entry)
    {
        lock (syncRoot)
        {
            if (!onceKeys.Add(key))
            {
                return false;
            }

            entries.Add(entry);
            entryKeys.Add(key);
            TrimToCapacity();
            return true;
        }
    }

    public IReadOnlyList<DiagnosticLogEntry> Snapshot()
    {
        lock (syncRoot)
        {
            return entries.ToArray();
        }
    }

    public void Replace(IEnumerable<DiagnosticLogEntry> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        lock (syncRoot)
        {
            entries.Clear();
            entryKeys.Clear();
            onceKeys.Clear();
            foreach (var entry in replacement)
            {
                entries.Add(entry);
                entryKeys.Add(null);
            }

            TrimToCapacity();
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            entries.Clear();
            entryKeys.Clear();
            onceKeys.Clear();
        }
    }

    private void TrimToCapacity()
    {
        var removeCount = entries.Count - Capacity;
        if (removeCount <= 0)
        {
            return;
        }

        entries.RemoveRange(0, removeCount);
        for (var index = 0; index < removeCount; index++)
        {
            var key = entryKeys[index];
            if (key is not null)
            {
                onceKeys.Remove(key);
            }
        }

        entryKeys.RemoveRange(0, removeCount);
    }
}
