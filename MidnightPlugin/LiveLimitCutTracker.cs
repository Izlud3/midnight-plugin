using System.Numerics;
using MidnightPlugin.Core;

namespace MidnightPlugin;

public sealed class LiveLimitCutTracker
{
    private static readonly TimeSpan ResolutionDelay = TimeSpan.FromMilliseconds(750);

    private readonly EncounterSessionService sessions;
    private readonly Action<LimitCutResult> resultAvailable;
    private readonly Dictionary<ulong, int> assignments = [];
    private readonly List<LimitCutBlasterCast> rotatingBlasters = [];
    private readonly List<LimitCutBlasterCast> finalBlasters = [];
    private IReadOnlyList<PartyObservation> party = [];
    private IReadOnlyList<PartyObservation> finalParty = [];
    private TimeSpan? lastFinalBlasterAt;
    private bool active;

    public LiveLimitCutTracker(EncounterSessionService sessions, Action<LimitCutResult> resultAvailable)
    {
        this.sessions = sessions;
        this.resultAvailable = resultAvailable;
    }

    public void Reset()
    {
        assignments.Clear();
        rotatingBlasters.Clear();
        finalBlasters.Clear();
        finalParty = [];
        lastFinalBlasterAt = null;
        active = false;
    }

    public void UpdateParty(IReadOnlyList<PartyObservation> value) => party = value;

    public void OnTargetIcon(TargetIconEvent targetIcon)
    {
        if (LimitCutAnalyzer.TryGetNumber(targetIcon.MarkerId, out var number))
            assignments[targetIcon.ActorId] = number;
    }

    public void OnActionEffect(ActionEffectSet actionEffect, TimeSpan elapsed)
    {
        var actionId = actionEffect.Header.ActionId;
        if (actionId == LimitCutAnalyzer.RotatingBlasterActionId)
        {
            if (sessions.ActivePull?.LimitCutResult is not null) return;
            active = true;
            AddBlaster(rotatingBlasters, actionEffect, elapsed);
            return;
        }

        if (!active || actionId != LimitCutAnalyzer.FinalBlasterActionId) return;
        if (finalBlasters.Count == 0) finalParty = party.ToArray();
        AddBlaster(finalBlasters, actionEffect, elapsed);
        lastFinalBlasterAt = elapsed;
    }

    public bool IsResolutionDue(TimeSpan elapsed) =>
        active && finalBlasters.Count > 0 && lastFinalBlasterAt is { } last && elapsed - last >= ResolutionDelay;

    public void Advance(TimeSpan elapsed)
    {
        if (!IsResolutionDue(elapsed)) return;

        var currentByActor = party.ToDictionary(member => member.ActorId);
        var captured = finalParty.Count > 0 ? finalParty : party;
        var participants = captured
            .GroupBy(member => member.ActorId)
            .Select(group => group.First())
            .Select(member => new LimitCutParticipant(
                member.ActorId,
                member.Name,
                member.Job,
                member.PartySlot,
                member.IsDead || currentByActor.GetValueOrDefault(member.ActorId)?.IsDead == true,
                member.Position))
            .OrderBy(member => member.PartySlot)
            .ToArray();
        var result = LimitCutAnalyzer.Analyze(
            elapsed,
            rotatingBlasters,
            finalBlasters,
            participants,
            assignments);
        if (sessions.TryRecordLimitCutResult(result)) resultAvailable(result);
        active = false;
    }

    private static void AddBlaster(List<LimitCutBlasterCast> target, ActionEffectSet actionEffect, TimeSpan elapsed)
    {
        var world = actionEffect.Source?.Position ?? actionEffect.Position;
        var position = new Vector2(world.X - LimitCutAnalyzer.ArenaCenter, world.Z - LimitCutAnalyzer.ArenaCenter);
        if (target.Any(existing => elapsed - existing.Elapsed < TimeSpan.FromMilliseconds(50) &&
                                   Vector2.DistanceSquared(existing.Position, position) < .01f))
            return;
        target.Add(new(elapsed, position, LimitCutAnalyzer.AngleOf(position)));
    }
}
