using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using MidnightPlugin.Core;
using MidnightPlugin.Windows;
using System.Diagnostics;
using System.IO;

namespace MidnightPlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

    private const string CommandName = "/midnighttimeline";

    public Configuration Configuration { get; init; }
    public IActionTimelineService Timeline { get; }
    public PracticeReferenceProvider References { get; }
    public PracticeReferenceLoadResult SelectedReference { get; private set; } = new(null, "No reference selected.");
    public PracticeSessionService? Practice { get; private set; }
    public ISharedImmediateTexture BrandIcon { get; }
    public EncounterSessionService EncounterSessions { get; }
    public LiveForsakenTracker ForsakenTracker { get; }
    public PersistentDiagnosticLog Diagnostics { get; }

    public readonly WindowSystem WindowSystem = new("MidnightTimeline");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private TimelineWindow TimelineWindow { get; init; }
    private DiagnosticsWindow DiagnosticsWindow { get; init; }
    private ForsakenWindow ForsakenWindow { get; init; }
    private ForsakenPromptWindow ForsakenPromptWindow { get; init; }
    private ActionEventCapture ActionEventCapture { get; init; }
    private EncounterCapture EncounterCapture { get; init; }
    private ActionEffectSource ActionEffects { get; init; }
    private string? selectedPracticeJob;
    private TimeSpan practiceStartOffset;
    private bool wasInCombat;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var previousConfigurationVersion = Configuration.Version;
        Configuration.Normalize();
        if (previousConfigurationVersion != Configuration.Version)
        {
            Configuration.Save();
        }

        var iconPath = Path.Combine(PluginInterface.AssemblyLocation.DirectoryName!, "Assets", "icon.png");
        BrandIcon = TextureProvider.GetFromFile(iconPath);

        Diagnostics = new PersistentDiagnosticLog(PluginInterface.GetPluginConfigDirectory());
        Diagnostics.Add("Lifecycle", null, "Plugin instance initialized.");
        ActionEffects = new ActionEffectSource(SigScanner, GameInteropProvider, ObjectTable, Log);
        if (!ActionEffects.IsAvailable)
        {
            Diagnostics.Add("Capture", null, "Action-effect signature was not found; timeline and encounter capture are unavailable.");
        }

        Timeline = new ActionTimelineService(Configuration.DefaultTimelineHistoryLimit);
        References = new PracticeReferenceProvider(
            PluginInterface.AssemblyLocation.DirectoryName!,
            PluginInterface.GetPluginConfigDirectory());
        LogReferenceErrors();
        SelectPracticeReference(References.References.FirstOrDefault()?.Job, force: true);
        EncounterSessions = new EncounterSessionService(MonotonicNow);
        ForsakenTracker = new LiveForsakenTracker(EncounterSessions, OnForsakenResult);
        EncounterCapture = new EncounterCapture(
            EncounterSessions,
            ForsakenTracker,
            Framework,
            ObjectTable,
            PartyList,
            ClientState,
            Condition,
            DutyState,
            Diagnostics,
            ActionEffects);
        // Subscribe encounter capture first so a Duty Recorder action effect can
        // open the pull before the local-action subscriber records that same effect.
        ActionEventCapture = new ActionEventCapture(
            Timeline,
            () => Practice,
            ObjectTable,
            PlayerState,
            ClientState,
            Log,
            Diagnostics,
            ActionEffects);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        TimelineWindow = new TimelineWindow(this);
        DiagnosticsWindow = new DiagnosticsWindow(this);
        ForsakenWindow = new ForsakenWindow(this);
        ForsakenPromptWindow = new ForsakenPromptWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(TimelineWindow);
        WindowSystem.AddWindow(DiagnosticsWindow);
        WindowSystem.AddWindow(ForsakenWindow);
        WindowSystem.AddWindow(ForsakenPromptWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alterna el timeline de acciones. Subcomandos: forsaken, log."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += DrawUi;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("{PluginName} initialized.", PluginInterface.Manifest.Name);
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        TimelineWindow.Dispose();
        DiagnosticsWindow.Dispose();
        ForsakenWindow.Dispose();
        ForsakenPromptWindow.Dispose();
        EncounterCapture.Dispose();
        ActionEventCapture.Dispose();
        ActionEffects.Dispose();
        Diagnostics.Add("Lifecycle", null, "Plugin instance disposed.");
        Diagnostics.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        var subcommand = args.Trim();
        if (subcommand.Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticsWindow.Toggle();
            return;
        }

        if (subcommand.Equals("forsaken", StringComparison.OrdinalIgnoreCase))
        {
            ToggleForsakenUi();
            return;
        }

        TimelineWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleTimelineUi() => TimelineWindow.Toggle();
    public void ToggleDiagnosticsUi() => DiagnosticsWindow.Toggle();
    public bool IsForsakenUiOpen => ForsakenWindow.IsOpen;

    public void ToggleForsakenUi()
    {
        if (ForsakenWindow.IsOpen)
        {
            ForsakenWindow.IsOpen = false;
            return;
        }

        ForsakenWindow.SelectPull(EncounterSessions.LatestReviewablePull()?.Id);
        ForsakenPromptWindow.Dismiss();
        ForsakenWindow.IsOpen = true;
    }

    public void OpenForsakenReview(Guid pullId)
    {
        ForsakenWindow.SelectPull(EncounterSessions.FindReviewablePull(pullId)?.Id ??
                                   EncounterSessions.LatestReviewablePull()?.Id);
        ForsakenPromptWindow.Dismiss();
        ForsakenWindow.IsOpen = true;
    }

    public bool IsPracticeEligible
    {
        get
        {
            var localPlayer = ObjectTable.LocalPlayer;
            return Practice is { } practice && PlayerState.IsLoaded && localPlayer is not null &&
                   localPlayer.ClassJob.IsValid &&
                   localPlayer.ClassJob.Value.Abbreviation.ToString().Equals(practice.Rotation.Job, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void DrawUi()
    {
        RefreshPracticeReference();
        Diagnostics.FlushIfDue(TimeSpan.FromSeconds(1));
        if (Practice is { } practice)
        {
            if (!IsPracticeEligible)
            {
                practice.Reset();
            }
            else
            {
                UpdateAutomaticPracticeStart(practice);
                practice.Advance();
            }
        }

        TimelineWindow.BgAlpha = Configuration.TimelineOpacity;
        WindowSystem.Draw();
    }

    public void ReloadPracticeReferences()
    {
        References.Reload();
        LogReferenceErrors();
        selectedPracticeJob = null;
        RefreshPracticeReference(force: true);
    }

    public void SetLiveTimelineVisible(bool visible)
    {
        if (Configuration.ShowLiveTimeline == visible) return;
        Configuration.ShowLiveTimeline = visible;
        Configuration.Save();
        TimelineWindow.RefreshWindowSize();
    }

    public void SetPracticeStartOffset(TimeSpan offset)
    {
        practiceStartOffset = offset < TimeSpan.Zero ? TimeSpan.Zero : offset;
        ArmPractice();
    }

    private void LogReferenceErrors()
    {
        foreach (var error in References.Errors)
        {
            Diagnostics.Add("Practice", null, error);
        }
    }

    private void RefreshPracticeReference(bool force = false)
    {
        var currentJob = ObjectTable.LocalPlayer is { ClassJob.IsValid: true } player
            ? player.ClassJob.Value.Abbreviation.ToString().ToUpperInvariant()
            : selectedPracticeJob ?? References.References.FirstOrDefault()?.Job;
        SelectPracticeReference(currentJob, force);
    }

    private void SelectPracticeReference(string? job, bool force)
    {
        if (!force && string.Equals(job, selectedPracticeJob, StringComparison.OrdinalIgnoreCase)) return;

        Practice?.Reset();
        selectedPracticeJob = job;
        SelectedReference = References.GetForJob(job);
        Practice = SelectedReference.IsValid
            ? new PracticeSessionService(SelectedReference.Rotation!, MonotonicNow)
            : null;
        ResetAutomaticStartTracking();
        ArmPractice();

        if (SelectedReference.IsValid)
        {
            Diagnostics.Add(
                "Practice",
                null,
                $"Loaded {SelectedReference.Rotation!.Job} practice reference with {SelectedReference.Rotation.Actions.Count} actions.");
        }
        else
        {
            Diagnostics.Add("Practice", null, SelectedReference.Error ?? "Practice reference unavailable.");
        }
    }

    private void OnForsakenResult(ForsakenPairResult result)
    {
        Diagnostics.Add("Forsaken", null, $"Pair {result.PairNumber}: {result.Verdict}. {string.Join(" ", result.Reasons)}");
        if (result.Verdict != MechanicVerdict.Failure || !Configuration.ForsakenFailureCardsEnabled) return;
        if (EncounterSessions.ActivePull is not { } pull) return;

        ForsakenWindow.SelectPull(pull.Id);
        if (ForsakenWindow.IsOpen)
            ForsakenPromptWindow.Dismiss();
        else
            ForsakenPromptWindow.Show(pull.Id);
    }

    private void ArmPractice()
    {
        Practice?.Start(startOffset: practiceStartOffset);
    }

    private void UpdateAutomaticPracticeStart(PracticeSessionService practice)
    {
        if (practice.State == PracticeState.Idle)
        {
            ArmPractice();
        }

        var inCombat = Condition[ConditionFlag.InCombat];
        if (wasInCombat && !inCombat &&
            practice.State is PracticeState.WaitingForFirstAction or PracticeState.Running or PracticeState.Paused or PracticeState.Completed)
        {
            ArmPractice();
        }

        if (inCombat && practice.State == PracticeState.WaitingForCombat)
        {
            practice.ConfirmCombatStarted();
        }

        wasInCombat = inCombat;
    }

    private void ResetAutomaticStartTracking()
    {
        wasInCombat = false;
    }

    private static TimeSpan MonotonicNow() => TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
}
