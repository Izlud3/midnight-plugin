namespace MidnightPlugin.Core;

public sealed class EncounterSessionService
{
    public const ushort DancingMadTerritoryId = 1331;
    public const int DefaultHistoryCapacity = 10;

    private readonly object syncRoot = new();
    private readonly Func<TimeSpan> monotonicClock;
    private readonly List<PullSession> history = [];
    private ushort territoryId;
    private bool dancingMadRecognized;

    public EncounterSessionService(
        Func<TimeSpan> monotonicClock,
        int historyCapacity = DefaultHistoryCapacity)
    {
        this.monotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
        if (historyCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(historyCapacity));
        HistoryCapacity = historyCapacity;
    }

    public int HistoryCapacity { get; }
    public ushort TerritoryId => territoryId;
    public bool IsDancingMadTerritory => territoryId == DancingMadTerritoryId;
    public bool IsDancingMad => IsDancingMadTerritory || dancingMadRecognized;
    public PullSession? ActivePull { get; private set; }

    public void SetTerritory(ushort value)
    {
        lock (syncRoot)
        {
            if (territoryId == value) return;
            if (ActivePull is not null)
            {
                EndPull(PullState.Abandoned, DateTimeOffset.UtcNow);
            }

            territoryId = value;
            dancingMadRecognized = false;
        }
    }

    public bool RecognizeDancingMad()
    {
        lock (syncRoot)
        {
            if (IsDancingMad) return false;
            dancingMadRecognized = true;
            return true;
        }
    }

    public PullSession? StartPull(DateTimeOffset startedAt)
    {
        lock (syncRoot)
        {
            if (!IsDancingMad) return null;
            if (ActivePull is not null) return ActivePull;

            var origin = monotonicClock();
            ActivePull = new PullSession(Guid.NewGuid(), startedAt, origin);
            return ActivePull;
        }
    }

    public PullSession? EndPull(PullState state, DateTimeOffset endedAt)
    {
        if (state == PullState.Active)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "A pull must end in a terminal state.");
        }

        lock (syncRoot)
        {
            if (ActivePull is null) return null;
            var elapsed = CurrentElapsedUnsafe();
            ActivePull.Duration = elapsed;
            ActivePull.EndedAt = endedAt;
            ActivePull.State = state;
            history.Add(ActivePull);
            if (history.Count > HistoryCapacity) history.RemoveAt(0);
            var result = ActivePull;
            ActivePull = null;
            return result;
        }
    }

    public bool TryRecordForsakenResult(ForsakenPairResult result)
    {
        lock (syncRoot)
        {
            if (ActivePull is null) return false;
            return ActivePull.AddForsakenResult(result);
        }
    }

    public bool ClearActiveForsakenResults()
    {
        lock (syncRoot)
        {
            if (ActivePull is null) return false;
            ActivePull.ClearForsakenResults();
            return true;
        }
    }

    public TimeSpan CurrentElapsed()
    {
        lock (syncRoot) return ActivePull is null ? TimeSpan.Zero : CurrentElapsedUnsafe();
    }

    public IReadOnlyList<PullSession> HistorySnapshot()
    {
        lock (syncRoot) return history.ToArray();
    }

    public PullSession? FindReviewablePull(Guid pullId)
    {
        lock (syncRoot)
        {
            if (ActivePull is { } active && active.Id == pullId && active.ForsakenResults.Count > 0)
                return active;

            return history.LastOrDefault(pull => pull.Id == pullId && pull.ForsakenResults.Count > 0);
        }
    }

    public PullSession? LatestReviewablePull()
    {
        lock (syncRoot)
        {
            if (ActivePull is { } active && active.ForsakenResults.Count > 0)
                return active;

            return history.LastOrDefault(pull => pull.ForsakenResults.Count > 0);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            ActivePull = null;
            history.Clear();
        }
    }

    private TimeSpan CurrentElapsedUnsafe() => ClampElapsed(monotonicClock() - ActivePull!.ClockOrigin);
    private static TimeSpan ClampElapsed(TimeSpan elapsed) => elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
}
