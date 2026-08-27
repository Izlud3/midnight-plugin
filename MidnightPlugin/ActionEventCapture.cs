using Dalamud.Game.ClientState;
using Dalamud.Plugin.Services;
using MidnightPlugin.Core;

namespace MidnightPlugin;

public sealed class ActionEventCapture : IDisposable
{
    private readonly IActionTimelineService timeline;
    private readonly Func<PracticeSessionService?> practiceProvider;
    private readonly IObjectTable objectTable;
    private readonly IPlayerState playerState;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly PersistentDiagnosticLog diagnostics;
    private readonly ActionEffectSource actionEffects;
    private readonly ActionClassificationResolver classificationResolver = new();
    private bool disposed;

    public ActionEventCapture(
        IActionTimelineService timeline,
        Func<PracticeSessionService?> practiceProvider,
        IObjectTable objectTable,
        IPlayerState playerState,
        IClientState clientState,
        IPluginLog log,
        PersistentDiagnosticLog diagnostics,
        ActionEffectSource actionEffects)
    {
        this.timeline = timeline;
        this.practiceProvider = practiceProvider;
        this.objectTable = objectTable;
        this.playerState = playerState;
        this.clientState = clientState;
        this.log = log;
        this.diagnostics = diagnostics;
        this.actionEffects = actionEffects;

        actionEffects.ActionEffect += OnActionEffect;
        clientState.Logout += OnLogout;
        clientState.ZoneInit += OnZoneInit;
        diagnostics.Add("Lifecycle", null, "Action effect capture subscribed.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        actionEffects.ActionEffect -= OnActionEffect;
        clientState.Logout -= OnLogout;
        clientState.ZoneInit -= OnZoneInit;
        diagnostics.Add("Lifecycle", null, "Action effect capture unsubscribed.");
        classificationResolver.Clear();
        timeline.Clear();
    }

    private void OnActionEffect(ActionEffectSet actionEffect)
    {
        var actionId = actionEffect.Header.ActionId;
        if (!playerState.IsLoaded)
        {
            diagnostics.AddOnce(
                "player-not-loaded",
                "Capture",
                actionId,
                "Ignored action effect because the local player is not loaded.");
            return;
        }

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer is null)
        {
            diagnostics.AddOnce(
                "local-player-null",
                "Capture",
                actionId,
                "Ignored action effect because ObjectTable.LocalPlayer is null.");
            return;
        }

        if (actionEffect.Source is null)
        {
            diagnostics.AddOnce(
                $"source-null:{actionId}",
                "Capture",
                actionId,
                "Ignored action effect because its source object is null.");
            return;
        }

        var sourceId = actionEffect.Source.GameObjectId;
        if (sourceId != localPlayer.GameObjectId)
        {
            diagnostics.AddOnce(
                $"source-mismatch:{sourceId}:{actionId}",
                "Capture",
                actionId,
                $"Ignored action effect from source {sourceId}; local player source is {localPlayer.GameObjectId}.");
            return;
        }

        if (actionId == 0)
        {
            diagnostics.Add("Capture", actionId, "Ignored local-player action effect because ActionID was zero.");
            return;
        }

        var classification = classificationResolver.Resolve(actionId);
        diagnostics.AddOnce(
            $"classification:{actionId}",
            "Metadata",
            actionId,
            $"Resolved '{classification.Name}'. RowFound={classification.IsResolved}, Category={classification.ActionCategoryId}, IsPlayerAction={classification.IsPlayerAction}, TimingClass={classification.TimingClass}.");

        if (classification.TimingClass == ActionTimingClass.Unknown)
        {
            diagnostics.AddOnce(
                $"unsupported-timing:{actionId}",
                "Capture",
                actionId,
                $"Ignored '{classification.Name}' because it is neither a GCD nor an oGCD action.");
            return;
        }

        if (timeline.TryRecord(actionId, DateTimeOffset.UtcNow))
        {
            var practice = practiceProvider();
            var currentJob = localPlayer.ClassJob.IsValid
                ? localPlayer.ClassJob.Value.Abbreviation.ToString()
                : null;
            if (practice is not null &&
                currentJob?.Equals(practice.Rotation.Job, StringComparison.OrdinalIgnoreCase) == true)
            {
                practice.TryRecordAction(actionId, classification.Name, classification.TimingClass);
            }

            diagnostics.Add(
                "Capture",
                actionId,
                $"Recorded local action. Source={sourceId}.");
            log.Debug("Recorded local action {ActionId}.", actionId);
        }
    }

    private void OnLogout(int type, int code)
    {
        timeline.Clear();
        practiceProvider()?.Reset();
        diagnostics.Add("Lifecycle", null, "Timeline cleared because the client logged out.");
    }

    private void OnZoneInit(ZoneInitEventArgs args)
    {
        timeline.Clear();
        practiceProvider()?.Reset();
        diagnostics.Add("Lifecycle", null, "Timeline cleared because a zone transition started.");
    }
}
