using MidnightPlugin.Core;

namespace MidnightPlugin;

public sealed record PartyObservation(
    ulong ActorId,
    string Name,
    string Job,
    uint CurrentHp,
    uint MaxHp,
    bool IsDead,
    EncounterPosition Position,
    IReadOnlySet<uint> Statuses);

public sealed class LiveForsakenTracker
{
    private const uint Forsaken = 47804;
    private const uint PathOfLight = 47806;
    private const uint RiverOfLight = 47807;
    private const uint Spelldriver = 47808;
    private const uint Spellscatter = 47809;
    private const uint Spellwave = 47810;

    private readonly EncounterSessionService sessions;
    private readonly Action<ForsakenPairResult> resultAvailable;
    private readonly List<TowerHit> towers = [];
    private readonly List<StackHit> stacks = [];
    private readonly List<ConeHit> cones = [];
    private IReadOnlyList<PartyObservation> party = [];
    private IReadOnlyList<PartyObservation> pairStartParty = [];
    private TimeSpan? lastPairEvidenceAt;
    private bool active;
    private int pairNumber;

    public LiveForsakenTracker(
        EncounterSessionService sessions,
        Action<ForsakenPairResult> resultAvailable)
    {
        this.sessions = sessions;
        this.resultAvailable = resultAvailable;
    }

    public void Reset()
    {
        active = false;
        pairNumber = 0;
        towers.Clear();
        stacks.Clear();
        cones.Clear();
        pairStartParty = [];
        lastPairEvidenceAt = null;
    }

    public void UpdateParty(IReadOnlyList<PartyObservation> value) => party = value;
    public bool IsResolutionDue(TimeSpan elapsed) =>
        active && towers.Count >= 2 && lastPairEvidenceAt is { } evidenceAt && elapsed - evidenceAt >= TimeSpan.FromMilliseconds(750);

    public void Advance(TimeSpan elapsed)
    {
        if (IsResolutionDue(elapsed)) ResolvePair(elapsed);
    }

    public void OnActionEffect(ActionEffectSet actionEffect, TimeSpan elapsed)
    {
        var actionId = actionEffect.Header.ActionId;
        if (actionId == Forsaken)
        {
            // Duty Recorder can restart or seek without ending the active pull.
            // A fresh Forsaken opener starts a new eight-pair review sequence.
            sessions.ClearActiveForsakenResults();
            Reset();
            active = true;
            pairStartParty = party.ToArray();
            return;
        }

        if (!active) return;
        if (actionId == Spelldriver)
        {
            var stackSourceId = actionEffect.Source?.GameObjectId ?? 0;
            var targets = AffectedTargets(actionEffect).Distinct().ToArray();
            if (!stacks.Any(stack => stack.SourceId == stackSourceId && elapsed - stack.Elapsed < TimeSpan.FromMilliseconds(100)))
                stacks.Add(new(stackSourceId, elapsed, targets));
            if (towers.Count >= 2) lastPairEvidenceAt = elapsed;
        }
        else if (actionId == Spellwave)
        {
            var coneSourceId = actionEffect.Source?.GameObjectId ?? 0;
            var partyIds = party.Select(member => member.ActorId).ToHashSet();
            var targets = AffectedTargets(actionEffect)
                .Where(targetId => targetId != coneSourceId && partyIds.Contains(targetId))
                .Distinct()
                .ToArray();
            if (!cones.Any(cone => cone.SourceId == coneSourceId && elapsed - cone.Elapsed < TimeSpan.FromMilliseconds(100)))
                cones.Add(new(coneSourceId, elapsed, targets));
            if (towers.Count >= 2) lastPairEvidenceAt = elapsed;
        }
        else if (actionId == Spellscatter && towers.Count >= 2)
        {
            lastPairEvidenceAt = elapsed;
        }

        if (actionId is not PathOfLight and not RiverOfLight) return;
        if (towers.Count > 0 && elapsed - towers[0].Elapsed > TimeSpan.FromSeconds(3))
        {
            towers.Clear();
            stacks.Clear();
            cones.Clear();
            lastPairEvidenceAt = null;
        }
        var sourceId = actionEffect.Source?.GameObjectId ?? 0;
        if (towers.Any(tower => tower.SourceId == sourceId && tower.ActionId == actionId &&
                                Math.Abs(tower.X - actionEffect.Position.X) < .1f && Math.Abs(tower.Y - actionEffect.Position.Z) < .1f)) return;
        var position = actionEffect.Source?.Position ?? actionEffect.Position;
        towers.Add(new TowerHit(sourceId, actionId, position.X, position.Z, elapsed, AffectedTargets(actionEffect).ToArray()));
        if (towers.Count >= 2) lastPairEvidenceAt = elapsed;
    }

    private void ResolvePair(TimeSpan elapsed)
    {
        pairNumber++;
        var soaked = towers.SelectMany(tower => tower.Targets).ToHashSet();
        var participants = party.Select(member => new ForsakenParticipant(
            member.ActorId,
            member.Name,
            member.Job,
            soaked.Contains(member.ActorId),
            member.IsDead,
            member.Position,
            null,
            pairStartParty.FirstOrDefault(start => start.ActorId == member.ActorId)?.CurrentHp,
            member.CurrentHp,
            member.MaxHp,
            member.Statuses)).ToArray();
        var arenaTowers = towers.Take(2).Select(tower => new ArenaTower(tower.X, tower.Y, tower.Targets.Count, tower.Targets)).ToArray();
        var stackResults = stacks.Select(stack => new StackResolution(stack.Targets.Count, stack.Targets, stack.SourceId)).ToArray();
        var coneResults = cones.Select(cone => new ConeResolution(cone.Targets.Count, cone.Targets, cone.SourceId)).ToArray();
        var evidenceComplete = pairNumber <= 8 && party.Count == 8 && arenaTowers.Length == 2;
        var result = ForsakenAnalyzer.Analyze(pairNumber, elapsed, participants, arenaTowers, stackResults, evidenceComplete, coneResults);
        sessions.TryRecordForsakenResult(result);
        resultAvailable(result);
        towers.Clear();
        stacks.Clear();
        cones.Clear();
        lastPairEvidenceAt = null;
        if (pairNumber >= 8) active = false;
    }

    private static IEnumerable<ulong> AffectedTargets(ActionEffectSet set)
    {
        foreach (var target in set.TargetEffects)
        {
            if (target.HasEffect) yield return target.TargetId;
        }
    }

    private sealed record TowerHit(ulong SourceId, uint ActionId, float X, float Y, TimeSpan Elapsed, IReadOnlyList<ulong> Targets);
    private sealed record StackHit(ulong SourceId, TimeSpan Elapsed, IReadOnlyList<ulong> Targets);
    private sealed record ConeHit(ulong SourceId, TimeSpan Elapsed, IReadOnlyList<ulong> Targets);
}
