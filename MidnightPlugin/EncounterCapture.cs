using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.Interop;
using MidnightPlugin.Core;

namespace MidnightPlugin;

public sealed class EncounterCapture : IDisposable
{
    // BossMod recognizes UMAD by these BNpcBase IDs rather than by territory.
    // Duty Recorder replays expose the original Sigmascape territory while still
    // populating the object table with the UMAD actors.
    private static readonly IReadOnlySet<uint> DancingMadBossObjectIds = new HashSet<uint>
    {
        0x4C30, // Kefka P1
        0x4C32, // Kefka P2
        0x4C34, // Chaos P3
        0x4C35, // Exdeath P3
        0x482B, // Kefka P4
        0x4C33, // Chaos P4
        0x4C36, // Neo Exdeath P4
        0x4C37, // Kefka P5
    };

    private static readonly IReadOnlySet<uint> ForsakenEncounterActions = new HashSet<uint>
    {
        47804, 47806, 47807, 47808, 47809, 47810,
    };

    private static readonly IReadOnlySet<uint> DancingMadEncounterActions = new HashSet<uint>
    {
        48370, 47804, 47839, 47858, 47847, 49884, 47936, 47938, 47925,
    };

    private readonly EncounterSessionService sessions;
    private readonly LiveForsakenTracker forsaken;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IDutyState dutyState;
    private readonly PersistentDiagnosticLog diagnostics;
    private readonly ActionEffectSource actionEffects;
    private TimeSpan lastPartySample = TimeSpan.MinValue;
    private bool wasDutyRecorderPlayback;
    private bool disposed;

    public EncounterCapture(
        EncounterSessionService sessions,
        LiveForsakenTracker forsaken,
        IFramework framework,
        IObjectTable objectTable,
        IPartyList partyList,
        IClientState clientState,
        ICondition condition,
        IDutyState dutyState,
        PersistentDiagnosticLog diagnostics,
        ActionEffectSource actionEffects)
    {
        this.sessions = sessions;
        this.forsaken = forsaken;
        this.framework = framework;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.clientState = clientState;
        this.condition = condition;
        this.dutyState = dutyState;
        this.diagnostics = diagnostics;
        this.actionEffects = actionEffects;
        wasDutyRecorderPlayback = condition[ConditionFlag.DutyRecorderPlayback];

        sessions.SetTerritory((ushort)clientState.TerritoryType);
        framework.Update += OnFrameworkUpdate;
        clientState.TerritoryChanged += OnTerritoryChanged;
        dutyState.DutyWiped += OnDutyWiped;
        dutyState.DutyCompleted += OnDutyCompleted;
        actionEffects.ActionEffect += OnActionEffect;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        framework.Update -= OnFrameworkUpdate;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        dutyState.DutyWiped -= OnDutyWiped;
        dutyState.DutyCompleted -= OnDutyCompleted;
        actionEffects.ActionEffect -= OnActionEffect;
        sessions.Clear();
        forsaken.Reset();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var territoryId = (ushort)clientState.TerritoryType;
        if (territoryId != sessions.TerritoryId)
        {
            sessions.SetTerritory(territoryId);
        }

        TryRecognizeDancingMadActor();
        if (!sessions.IsDancingMad) return;

        var isDutyRecorderPlayback = condition[ConditionFlag.DutyRecorderPlayback];
        if (wasDutyRecorderPlayback && !isDutyRecorderPlayback && sessions.ActivePull is not null)
        {
            EndPull(PullState.Abandoned, "Duty Recorder playback ended.");
        }
        wasDutyRecorderPlayback = isDutyRecorderPlayback;

        // Actor presence identifies the encounter, but it does not prove that a
        // paused Duty Recorder replay is advancing. Start on an action instead.
        EnsurePullStarted(hasEncounterSignal: false);

        if (sessions.ActivePull is null) return;
        var elapsed = sessions.CurrentElapsed();
        if (lastPartySample == TimeSpan.MinValue || elapsed - lastPartySample >= TimeSpan.FromSeconds(1))
        {
            lastPartySample = elapsed;
            PollParty(elapsed);
        }
        if (forsaken.IsResolutionDue(elapsed))
        {
            // Refresh deaths and final positions after the follow-up shape effects
            // have settled, immediately before producing the retrospective verdict.
            PollParty(elapsed);
            lastPartySample = elapsed;
        }
        forsaken.Advance(elapsed);
    }

    private void PollParty(TimeSpan elapsed)
    {
        var isReplay = condition[ConditionFlag.DutyRecorderPlayback] ||
                       sessions.IsDancingMad && !sessions.IsDancingMadTerritory;
        var observations = isReplay ? PollReplayParty() : PollDalamudParty();

        // Dalamud omits a solo player from PartyList; retain useful capture in that case.
        if (observations.Count == 0 && objectTable.LocalPlayer is { } localPlayer)
        {
            observations.Add(ObservePlayer(localPlayer));
        }

        forsaken.UpdateParty(observations);
    }

    private List<PartyObservation> PollDalamudParty()
    {
        var observations = new List<PartyObservation>();
        foreach (var member in partyList)
        {
            var gameObject = member.GameObject;
            if (gameObject is null) continue;
            var statuses = member.Statuses.Select(status => status.StatusId).Where(id => id != 0).ToHashSet();
            observations.Add(new(
                gameObject.GameObjectId,
                member.Name.ToString(),
                member.ClassJob.IsValid ? member.ClassJob.Value.Abbreviation.ToString() : "?",
                member.CurrentHP,
                member.MaxHP,
                gameObject.IsDead,
                new(gameObject.Position.X, gameObject.Position.Y, gameObject.Position.Z, gameObject.Rotation),
                statuses));
        }
        return observations;
    }

    private unsafe List<PartyObservation> PollReplayParty()
    {
        var players = objectTable.OfType<IPlayerCharacter>()
            .GroupBy(player => (uint)player.GameObjectId)
            .ToDictionary(group => group.Key, group => group.First());
        var observations = new List<PartyObservation>(8);
        var group = GroupManager.Instance()->GetGroup(true);
        var nativeMembers = group == null ? 0 : group->MemberCount;
        if (group != null)
        {
            for (var index = 0; index < group->MemberCount; index++)
            {
                var member = group->PartyMembers.GetPointer(index);
                if (member != null && players.TryGetValue(member->EntityId, out var player))
                observations.Add(ObservePlayer(player));
            }
        }
        var nativeResolved = observations.Count;

        // The native group can be populated before or after replay actors. Keep
        // capture useful during that transition and deduplicate once both exist.
        foreach (var player in players.Values)
        {
            if (observations.All(member => member.ActorId != player.GameObjectId))
                observations.Add(ObservePlayer(player));
        }
        diagnostics.AddOnce(
            $"replay-party:{nativeMembers}:{nativeResolved}:{observations.Count}",
            "Encounter",
            null,
            $"Duty Recorder party snapshot: native members={nativeMembers}, native actors resolved={nativeResolved}, player actors={observations.Count}.");
        return observations;
    }

    private static PartyObservation ObservePlayer(IPlayerCharacter player)
    {
        var statuses = player.StatusList.Select(status => status.StatusId).Where(id => id != 0).ToHashSet();
        return new(
            player.GameObjectId,
            player.Name.ToString(),
            player.ClassJob.IsValid ? player.ClassJob.Value.Abbreviation.ToString() : "?",
            player.CurrentHp,
            player.MaxHp,
            player.IsDead,
            new(player.Position.X, player.Position.Y, player.Position.Z, player.Rotation),
            statuses);
    }

    private void OnActionEffect(ActionEffectSet actionEffect)
    {
        TryRecognizeDancingMadActor();
        if (IsStrongEncounterSignal(actionEffect))
            RecognizeDancingMad($"action {actionEffect.Header.ActionId}");
        var hasEncounterSignal = IsEncounterSignal(actionEffect);
        EnsurePullStarted(hasEncounterSignal);
        if (sessions.ActivePull is null) return;
        var elapsed = sessions.CurrentElapsed();
        if (ForsakenEncounterActions.Contains(actionEffect.Header.ActionId))
        {
            // Capture replay positions and markers at the mechanic event rather
            // than relying on the periodic one-second diagnostic sample.
            PollParty(elapsed);
            lastPartySample = elapsed;
        }
        forsaken.OnActionEffect(actionEffect, elapsed);
    }

    private void OnTerritoryChanged(uint territory)
    {
        sessions.SetTerritory((ushort)territory);
        forsaken.Reset();
        lastPartySample = TimeSpan.MinValue;
    }
    private void OnDutyWiped(IDutyStateEventArgs _) => EndPull(PullState.Wiped, "Duty wipe detected.");
    private void OnDutyCompleted(IDutyStateEventArgs _) => EndPull(PullState.Completed, "Duty completion detected.");

    private void EnsurePullStarted(bool hasEncounterSignal)
    {
        if (sessions.ActivePull is not null) return;

        var isPlayback = condition[ConditionFlag.DutyRecorderPlayback];
        if (!EncounterPullStartPolicy.ShouldStart(
                sessions.IsDancingMad,
                dutyState.IsDutyStarted,
                condition[ConditionFlag.InCombat],
                isPlayback,
                hasEncounterSignal))
            return;

        if (sessions.StartPull(DateTimeOffset.UtcNow) is null) return;

        forsaken.Reset();
        lastPartySample = TimeSpan.MinValue;
        diagnostics.Add(
            "Encounter",
            null,
            isPlayback
                ? "Started Dancing Mad pull capture from a Duty Recorder playback action effect."
                : hasEncounterSignal && !dutyState.IsDutyStarted && !condition[ConditionFlag.InCombat]
                    ? "Started Dancing Mad pull capture from a verified encounter action while duty/combat flags were unavailable (Duty Recorder fallback)."
                    : "Started Dancing Mad pull capture from live duty combat state.");
    }

    private bool IsEncounterSignal(ActionEffectSet actionEffect)
    {
        if (IsStrongEncounterSignal(actionEffect)) return true;

        // Once a UMAD actor has identified a surrogate replay, the first emitted
        // action is the best available indication that playback is actually moving.
        if (sessions.IsDancingMad && !sessions.IsDancingMadTerritory) return true;

        var sourceId = actionEffect.Source?.GameObjectId;
        if (sourceId is null or 0) return false;
        if (sourceId == objectTable.LocalPlayer?.GameObjectId) return false;
        // A generic enemy action is enough inside the real UMAD territory, but not
        // in a surrogate replay territory where it could belong to the original duty.
        return sessions.IsDancingMadTerritory &&
               !partyList.Any(member => member.GameObject?.GameObjectId == sourceId);
    }

    private bool IsStrongEncounterSignal(ActionEffectSet actionEffect) =>
        DancingMadEncounterActions.Contains(actionEffect.Header.ActionId) ||
        ForsakenEncounterActions.Contains(actionEffect.Header.ActionId) ||
        actionEffect.Source is { } source && DancingMadBossObjectIds.Contains(source.BaseId);

    private bool TryRecognizeDancingMadActor()
    {
        if (sessions.IsDancingMadTerritory) return false;
        var boss = objectTable.OfType<IBattleChara>()
            .FirstOrDefault(actor => DancingMadBossObjectIds.Contains(actor.BaseId));
        if (boss is null) return false;
        RecognizeDancingMad($"boss actor {boss.BaseId:X} ({boss.Name})");
        return true;
    }

    private void RecognizeDancingMad(string reason)
    {
        if (!sessions.RecognizeDancingMad()) return;
        diagnostics.Add(
            "Encounter",
            null,
            $"Recognized Dancing Mad in surrogate territory {sessions.TerritoryId} from {reason}. " +
            $"DutyRecorderPlayback={condition[ConditionFlag.DutyRecorderPlayback]}, " +
            $"InCombat={condition[ConditionFlag.InCombat]}, DutyStarted={dutyState.IsDutyStarted}.");
    }

    private void EndPull(PullState state, string reason)
    {
        var ended = sessions.EndPull(state, DateTimeOffset.UtcNow);
        if (ended is not null)
            diagnostics.Add("Encounter", null, $"Ended Dancing Mad pull capture as {state}. {reason}");
        forsaken.Reset();
        lastPartySample = TimeSpan.MinValue;
    }
}
