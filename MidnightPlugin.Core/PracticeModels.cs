using System.Text.Json;

namespace MidnightPlugin.Core;

public enum PracticeState
{
    Idle,
    WaitingForFirstAction,
    WaitingForCountdown,
    Countdown,
    Running,
    Paused,
    Completed,
}

public enum PracticeStartMode
{
    Countdown,
    FirstCombatAction,
    GameCountdown,
}

public enum PracticeMatchKind
{
    Hit,
    Missed,
    Wrong,
    Extra,
}

public static class PracticeActionIgnoreList
{
    private static readonly IReadOnlySet<uint> IgnoredActionIds = new HashSet<uint>
    {
        7533,  // Provoke
        7382,  // Intervention
        25746, // Holy Sheltron
        3540,  // Divine Veil
        36920, // Guardian
        7385,  // Passage of Arms
        7531,  // Rampart
        7535,  // Reprisal
        7537,  // Shirk
        22,    // Bulwark
        30,    // Hallowed Ground
    };

    private static readonly IReadOnlySet<string> IgnoredActionNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "provoke",
        "intervention",
        "holysheltron",
        "divineveil",
        "guardian",
        "passageofarms",
        "rampart",
        "reprisal",
        "shirk",
        "bulwark",
        "hallowedground",
    };

    public static bool Contains(uint actionId, string? actionName)
    {
        return IgnoredActionIds.Contains(actionId) ||
               IgnoredActionNames.Contains(ActionNameNormalizer.Normalize(actionName));
    }
}

public readonly record struct PracticeReferenceAction(
    uint ActionId,
    string ActionName,
    ActionTimingClass TimingClass,
    TimeSpan Offset);

public readonly record struct PracticeExpectedResult(
    int ReferenceIndex,
    PracticeMatchKind Kind,
    TimeSpan? ActualElapsed,
    int? DeltaMilliseconds,
    uint? ActualActionId,
    string? ActualActionName);

public readonly record struct PracticeAttempt(
    int? ReferenceIndex,
    uint ActionId,
    string ActionName,
    ActionTimingClass TimingClass,
    TimeSpan Elapsed,
    PracticeMatchKind Kind,
    int? DeltaMilliseconds);

public sealed record PracticeReferenceRotation(
    string EncounterKey,
    ushort TerritoryId,
    string Job,
    string Patch,
    string Version,
    string ProvenancePlayer,
    int? ProvenanceRank,
    string ProvenanceSource,
    IReadOnlyList<PracticeReferenceAction> Actions);

public sealed record PracticeReferenceLoadResult(
    PracticeReferenceRotation? Rotation,
    string? Error)
{
    public bool IsValid => Rotation is not null && Error is null;
}

public static class PracticeReferenceCatalog
{
    public const int SupportedSchemaVersion = 1;
    public const int MaxReferenceJsonLength = 2_000_000;
    public const int MaxActionsPerReference = 2048;

    public static PracticeReferenceLoadResult Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(null, "Reference JSON was empty.");
        }

        if (json.Length > MaxReferenceJsonLength)
        {
            return new(null, "Reference JSON exceeds the supported size limit.");
        }

        try
        {
            var document = JsonSerializer.Deserialize<ReferenceDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            });

            if (document is null)
            {
                return new(null, "Reference JSON was empty.");
            }

            if (document.SchemaVersion != SupportedSchemaVersion)
            {
                return new(null, $"Unsupported reference schema {document.SchemaVersion}; expected {SupportedSchemaVersion}.");
            }

            var normalizedJob = document.Job?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedJob) ||
                normalizedJob.Length > 8 ||
                normalizedJob.Any(character => !char.IsAsciiLetterOrDigit(character)))
            {
                return new(null, $"Reference job '{document.Job ?? "missing"}' is invalid.");
            }

            if (document.Actions is null || document.Actions.Count == 0)
            {
                return new(null, "Reference contains no actions.");
            }

            if (document.Actions.Count > MaxActionsPerReference)
            {
                return new(null, $"Reference contains more than {MaxActionsPerReference} actions.");
            }

            var previousTimeMs = int.MinValue;
            var includedActions = new List<(ReferenceActionDocument Action, ActionTimingClass TimingClass)>();
            for (var index = 0; index < document.Actions.Count; index++)
            {
                var action = document.Actions[index];
                if (action is null)
                {
                    return new(null, $"Reference action {index} is null.");
                }

                if (action.ActionId == 0 && string.IsNullOrWhiteSpace(action.ActionName))
                {
                    return new(null, $"Reference action {index} has neither an action ID nor a name.");
                }

                if (action.TimeMs < 0)
                {
                    return new(null, $"Reference action {index} has a negative timestamp.");
                }

                if (action.TimeMs < previousTimeMs)
                {
                    return new(null, "Reference actions are not ordered by time.");
                }

                if (!TryParseTimingClass(action.TimingClass, out var timingClass))
                {
                    return new(null, $"Reference action {index} has unsupported timing class '{action.TimingClass ?? "missing"}'.");
                }

                if (PracticeActionIgnoreList.Contains(action.ActionId, action.ActionName))
                {
                    previousTimeMs = action.TimeMs;
                    continue;
                }

                includedActions.Add((action, timingClass));
                previousTimeMs = action.TimeMs;
            }

            if (includedActions.Count == 0)
            {
                return new(null, "Reference contains no practice actions after applying the ignore list.");
            }

            var firstTimeMs = includedActions[0].Action.TimeMs;
            var actions = includedActions
                .Select(item => new PracticeReferenceAction(
                    item.Action.ActionId,
                    item.Action.ActionName?.Trim() ?? $"Action {item.Action.ActionId}",
                    item.TimingClass,
                    TimeSpan.FromMilliseconds(item.Action.TimeMs - firstTimeMs)))
                .ToArray();

            var provenance = document.Provenance ?? new ReferenceProvenanceDocument();
            return new(
                new PracticeReferenceRotation(
                    document.EncounterKey ?? "unknown",
                    document.TerritoryId,
                    normalizedJob,
                    document.Patch ?? "unknown",
                    document.Version ?? "unknown",
                    provenance.Player ?? "Unknown player",
                    provenance.Rank,
                    provenance.Source ?? "Unknown source",
                    actions.ToArray()),
                null);
        }
        catch (JsonException exception)
        {
            return new(null, $"Reference JSON is invalid: {exception.Message}");
        }
    }

    private static bool TryParseTimingClass(string? value, out ActionTimingClass timingClass)
    {
        if (string.Equals(value, "gcd", StringComparison.OrdinalIgnoreCase))
        {
            timingClass = ActionTimingClass.Gcd;
            return true;
        }

        if (string.Equals(value, "ogcd", StringComparison.OrdinalIgnoreCase))
        {
            timingClass = ActionTimingClass.Ogcd;
            return true;
        }

        timingClass = ActionTimingClass.Unknown;
        return false;
    }

    private sealed class ReferenceDocument
    {
        public int SchemaVersion { get; set; }
        public string? EncounterKey { get; set; }
        public ushort TerritoryId { get; set; }
        public string? Job { get; set; }
        public string? Patch { get; set; }
        public string? Version { get; set; }
        public ReferenceProvenanceDocument? Provenance { get; set; }
        public List<ReferenceActionDocument?>? Actions { get; set; }
    }

    private sealed class ReferenceProvenanceDocument
    {
        public string? Player { get; set; }
        public int? Rank { get; set; }
        public string? Source { get; set; }
    }

    private sealed class ReferenceActionDocument
    {
        public uint ActionId { get; set; }
        public string? ActionName { get; set; }
        public string? TimingClass { get; set; }
        public int TimeMs { get; set; }
    }
}

public sealed record PracticeSnapshot(
    PracticeState State,
    TimeSpan Elapsed,
    TimeSpan CountdownRemaining,
    int StartReferenceIndex,
    int NextReferenceIndex,
    IReadOnlyList<PracticeReferenceAction> ReferenceActions,
    IReadOnlyList<PracticeExpectedResult> ExpectedResults,
    IReadOnlyList<PracticeAttempt> Attempts,
    int HitCount,
    int MissCount,
    int WrongCount,
    int ExtraCount,
    bool StoppedOnMistake)
{
    public int TotalCount => ReferenceActions.Count - StartReferenceIndex;
    public int ResolvedCount => ExpectedResults.Count;
}

public sealed class PracticeSessionService
{
    public const int CountdownMilliseconds = 3_000;
    public const int TimingToleranceMilliseconds = 500;
    public const int ReferenceStartDelayMilliseconds = 200;
    public const int StopAfterMistakes = 3;

    private readonly object syncRoot = new();
    private readonly PracticeReferenceRotation rotation;
    private readonly Func<TimeSpan> monotonicClock;
    private readonly List<PracticeExpectedResult> expectedResults = [];
    private readonly List<PracticeAttempt> attempts = [];
    private PracticeState state;
    private PracticeState pausedFromState;
    private TimeSpan countdownStartedAt;
    private TimeSpan runningStartedAt;
    private TimeSpan completedElapsed;
    private TimeSpan pauseStartedAt;
    private TimeSpan pausedElapsed;
    private TimeSpan pausedCountdownRemaining;
    private TimeSpan startingOffset;
    private int mistakeLimit;
    private bool stoppedOnMistake;
    private int startReferenceIndex;
    private int nextReferenceIndex;

    public PracticeSessionService(PracticeReferenceRotation rotation, Func<TimeSpan> monotonicClock)
    {
        this.rotation = rotation ?? throw new ArgumentNullException(nameof(rotation));
        this.monotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
        if (rotation.Actions.Count == 0) throw new ArgumentException("A practice rotation must contain at least one action.", nameof(rotation));
    }

    public PracticeReferenceRotation Rotation => rotation;
    public PracticeState State { get { lock (syncRoot) { AdvanceUnsafe(); return state; } } }
    public bool IsStoppedOnMistake
    {
        get
        {
            lock (syncRoot)
            {
                AdvanceUnsafe();
                return state == PracticeState.Completed && stoppedOnMistake;
            }
        }
    }

    public void Start(
        PracticeStartMode startMode = PracticeStartMode.Countdown,
        int mistakeLimit = 0,
        TimeSpan startOffset = default)
    {
        if (startOffset < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset), startOffset, "Practice start offset cannot be negative.");
        }

        lock (syncRoot)
        {
            ResetUnsafe();
            this.mistakeLimit = mistakeLimit;
            startingOffset = startOffset > rotation.Actions[^1].Offset
                ? rotation.Actions[^1].Offset
                : startOffset;
            startReferenceIndex = rotation.Actions
                .TakeWhile(action => action.Offset < startingOffset)
                .Count();
            nextReferenceIndex = startReferenceIndex;
            if (startMode == PracticeStartMode.FirstCombatAction)
            {
                state = PracticeState.WaitingForFirstAction;
            }
            else if (startMode == PracticeStartMode.GameCountdown)
            {
                state = PracticeState.WaitingForCountdown;
            }
            else
            {
                state = PracticeState.Countdown;
                countdownStartedAt = Now();
            }
        }
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            ResetUnsafe();
        }
    }

    public void Advance()
    {
        lock (syncRoot)
        {
            AdvanceUnsafe();
        }
    }

    public bool BeginFromTrigger()
    {
        lock (syncRoot)
        {
            AdvanceUnsafe();
            if (state is not (PracticeState.WaitingForFirstAction or PracticeState.WaitingForCountdown)) return false;

            state = PracticeState.Running;
            runningStartedAt = Now() - startingOffset;
            return true;
        }
    }

    public bool Pause()
    {
        lock (syncRoot)
        {
            AdvanceUnsafe();
            if (state is not (PracticeState.WaitingForFirstAction or PracticeState.WaitingForCountdown or PracticeState.Countdown or PracticeState.Running)) return false;

            pausedFromState = state;
            pauseStartedAt = Now();
            pausedElapsed = state == PracticeState.Running ? ElapsedUnsafe() : startingOffset;
            pausedCountdownRemaining = state == PracticeState.Countdown
                ? Max(TimeSpan.Zero, TimeSpan.FromMilliseconds(CountdownMilliseconds) - (pauseStartedAt - countdownStartedAt))
                : TimeSpan.Zero;
            state = PracticeState.Paused;
            return true;
        }
    }

    public bool Resume()
    {
        lock (syncRoot)
        {
            if (state != PracticeState.Paused) return false;

            var pausedDuration = Max(TimeSpan.Zero, Now() - pauseStartedAt);
            if (pausedFromState == PracticeState.Countdown)
            {
                countdownStartedAt += pausedDuration;
            }
            else if (pausedFromState == PracticeState.Running)
            {
                runningStartedAt += pausedDuration;
            }

            state = pausedFromState;
            pausedFromState = PracticeState.Idle;
            pauseStartedAt = TimeSpan.Zero;
            pausedElapsed = TimeSpan.Zero;
            pausedCountdownRemaining = TimeSpan.Zero;
            AdvanceUnsafe();
            return true;
        }
    }

    public bool TryRecordAction(uint actionId, string actionName, ActionTimingClass timingClass)
    {
        if (actionId == 0 && string.IsNullOrWhiteSpace(actionName)) return false;
        if (PracticeActionIgnoreList.Contains(actionId, actionName)) return false;

        lock (syncRoot)
        {
            AdvanceUnsafe();
            if (state == PracticeState.WaitingForFirstAction)
            {
                BeginFromTriggerUnsafe();
            }

            if (state != PracticeState.Running) return false;

            var elapsed = ElapsedUnsafe();
            if (nextReferenceIndex >= rotation.Actions.Count)
            {
                attempts.Add(new PracticeAttempt(null, actionId, actionName, timingClass, elapsed, PracticeMatchKind.Extra, null));
                if (ShouldStopOnMistakeUnsafe())
                {
                    CompleteOnMistakeUnsafe(elapsed);
                }

                return true;
            }

            var reference = rotation.Actions[nextReferenceIndex];
            var deltaMilliseconds = (int)Math.Round((elapsed - reference.Offset).TotalMilliseconds);
            var identityMatches = Matches(reference, actionId, actionName);
            if (identityMatches && Math.Abs(deltaMilliseconds) <= TimingToleranceMilliseconds)
            {
                expectedResults.Add(new PracticeExpectedResult(
                    nextReferenceIndex,
                    PracticeMatchKind.Hit,
                    elapsed,
                    deltaMilliseconds,
                    actionId,
                    actionName));
                attempts.Add(new PracticeAttempt(
                    nextReferenceIndex,
                    actionId,
                    actionName,
                    timingClass,
                    elapsed,
                    PracticeMatchKind.Hit,
                    deltaMilliseconds));
                nextReferenceIndex++;
                return true;
            }

            var withinCurrentWindow = Math.Abs(deltaMilliseconds) <= TimingToleranceMilliseconds;
            var kind = withinCurrentWindow ? PracticeMatchKind.Wrong : PracticeMatchKind.Extra;
            attempts.Add(new PracticeAttempt(
                withinCurrentWindow ? nextReferenceIndex : null,
                actionId,
                actionName,
                timingClass,
                elapsed,
                kind,
                withinCurrentWindow ? deltaMilliseconds : null));
            if (ShouldStopOnMistakeUnsafe())
            {
                CompleteOnMistakeUnsafe(elapsed);
            }

            return true;
        }
    }

    public PracticeSnapshot Snapshot()
    {
        lock (syncRoot)
        {
            AdvanceUnsafe();
            var elapsed = state switch
            {
                PracticeState.Running => ElapsedUnsafe(),
                PracticeState.Paused => pausedElapsed,
                PracticeState.Completed => completedElapsed,
                PracticeState.WaitingForFirstAction or PracticeState.WaitingForCountdown or PracticeState.Countdown => startingOffset,
                _ => TimeSpan.Zero,
            };
            var countdownRemaining = state == PracticeState.Countdown
                ? Max(TimeSpan.Zero, TimeSpan.FromMilliseconds(CountdownMilliseconds) - (Now() - countdownStartedAt))
                : state == PracticeState.Paused ? pausedCountdownRemaining
                : TimeSpan.Zero;

            return new PracticeSnapshot(
                state,
                elapsed,
                countdownRemaining,
                startReferenceIndex,
                nextReferenceIndex,
                rotation.Actions,
                expectedResults.ToArray(),
                attempts.ToArray(),
                expectedResults.Count(result => result.Kind == PracticeMatchKind.Hit),
                expectedResults.Count(result => result.Kind == PracticeMatchKind.Missed),
                attempts.Count(attempt => attempt.Kind == PracticeMatchKind.Wrong),
                attempts.Count(attempt => attempt.Kind == PracticeMatchKind.Extra),
                stoppedOnMistake);
        }
    }

    private void AdvanceUnsafe()
    {
        if (state == PracticeState.Paused) return;

        if (state == PracticeState.Countdown)
        {
            var countdownElapsed = Now() - countdownStartedAt;
            if (countdownElapsed >= TimeSpan.FromMilliseconds(CountdownMilliseconds))
            {
                state = PracticeState.Running;
                runningStartedAt = countdownStartedAt + TimeSpan.FromMilliseconds(CountdownMilliseconds) - startingOffset;
            }
        }

        if (state != PracticeState.Running) return;

        var elapsed = ElapsedUnsafe();
        while (nextReferenceIndex < rotation.Actions.Count &&
               elapsed > rotation.Actions[nextReferenceIndex].Offset + TimeSpan.FromMilliseconds(TimingToleranceMilliseconds))
        {
            expectedResults.Add(new PracticeExpectedResult(
                nextReferenceIndex,
                PracticeMatchKind.Missed,
                null,
                null,
                null,
                null));
            nextReferenceIndex++;
            if (ShouldStopOnMistakeUnsafe())
            {
                CompleteOnMistakeUnsafe(elapsed);
                return;
            }
        }

        var completionOffset = rotation.Actions[^1].Offset + TimeSpan.FromMilliseconds(TimingToleranceMilliseconds);
        if (nextReferenceIndex == rotation.Actions.Count && elapsed > completionOffset)
        {
            completedElapsed = elapsed;
            state = PracticeState.Completed;
        }
    }

    private void CompleteOnMistakeUnsafe(TimeSpan elapsed)
    {
        completedElapsed = elapsed;
        stoppedOnMistake = true;
        state = PracticeState.Completed;
    }

    private void BeginFromTriggerUnsafe()
    {
        state = PracticeState.Running;
        runningStartedAt = Now() - startingOffset;
    }

    private bool Matches(PracticeReferenceAction reference, uint actionId, string actionName)
    {
        if (reference.ActionId != 0 && actionId == reference.ActionId) return true;
        return ActionNameNormalizer.Normalize(reference.ActionName) == ActionNameNormalizer.Normalize(actionName);
    }

    private TimeSpan ElapsedUnsafe() =>
        Max(TimeSpan.Zero, Now() - runningStartedAt - TimeSpan.FromMilliseconds(ReferenceStartDelayMilliseconds));
    private TimeSpan Now() => monotonicClock();

    private void ResetUnsafe()
    {
        state = PracticeState.Idle;
        pausedFromState = PracticeState.Idle;
        countdownStartedAt = TimeSpan.Zero;
        runningStartedAt = TimeSpan.Zero;
        completedElapsed = TimeSpan.Zero;
        pauseStartedAt = TimeSpan.Zero;
        pausedElapsed = TimeSpan.Zero;
        pausedCountdownRemaining = TimeSpan.Zero;
        startingOffset = TimeSpan.Zero;
        mistakeLimit = 0;
        stoppedOnMistake = false;
        startReferenceIndex = 0;
        nextReferenceIndex = 0;
        expectedResults.Clear();
        attempts.Clear();
    }

    private bool ShouldStopOnMistakeUnsafe() =>
        mistakeLimit > 0 && CurrentMistakeCountUnsafe() >= mistakeLimit;

    private int CurrentMistakeCountUnsafe() =>
        attempts.Count(attempt => attempt.Kind is PracticeMatchKind.Wrong or PracticeMatchKind.Extra) +
        expectedResults.Count(result => result.Kind == PracticeMatchKind.Missed);

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
}
