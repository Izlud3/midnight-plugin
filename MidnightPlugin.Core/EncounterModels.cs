namespace MidnightPlugin.Core;

public enum PullState
{
    Active,
    Completed,
    Wiped,
    Abandoned,
}

public readonly record struct EncounterPosition(float X, float Y, float Z, float Heading);

public sealed class PullSession
{
    private readonly object syncRoot = new();
    private readonly List<ForsakenPairResult> forsakenResults = [];

    internal PullSession(Guid id, DateTimeOffset startedAt, TimeSpan clockOrigin)
    {
        Id = id;
        StartedAt = startedAt;
        ClockOrigin = clockOrigin;
        State = PullState.Active;
    }

    public Guid Id { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt { get; internal set; }
    public TimeSpan ClockOrigin { get; }
    public TimeSpan Duration { get; internal set; }
    public PullState State { get; internal set; }
    public IReadOnlyList<ForsakenPairResult> ForsakenResults { get { lock (syncRoot) return forsakenResults.ToArray(); } }

    internal bool AddForsakenResult(ForsakenPairResult value)
    {
        lock (syncRoot)
        {
            if (forsakenResults.Any(result => result.Verdict == MechanicVerdict.Failure)) return false;
            forsakenResults.Add(value);
            return true;
        }
    }

    internal void ClearForsakenResults()
    {
        lock (syncRoot) forsakenResults.Clear();
    }
}
