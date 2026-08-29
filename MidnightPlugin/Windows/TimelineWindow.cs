using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using MidnightPlugin.Core;

namespace MidnightPlugin.Windows;

public sealed class TimelineWindow : Window, IDisposable
{
    private const float WindowWidth = 760f;
    private const float CollapsedWindowHeight = 208f;
    private const float ExpandedWindowHeight = 390f;
    private const float LabelWidth = 76f;
    private const float AxisHeight = 28f;
    private const float LaneHeight = 48f;
    private const float CanvasHeight = AxisHeight + LaneHeight * 2f;
    private const float IconSize = 34f;
    private const double ViewHistorySeconds = 10d;
    private const double ViewFutureSeconds = 3d;
    private const double MaxTimelinePanSeconds = 3600d;

    private static readonly Vector4[] LaneColors =
    {
        new(0.78f, 0.46f, 0.22f, 1f),
        new(0.30f, 0.48f, 0.82f, 1f),
    };

    private static readonly string[] LaneLabels = { "oGCD", "GCD" };

    private readonly Plugin plugin;
    private readonly ActionIconResolver metadataResolver = new();
    private bool practiceExpanded;
    private double liveViewOffsetSeconds;
    private double practiceViewOffsetSeconds;

    public TimelineWindow(Plugin plugin)
        : base("Practice Timeline##MidnightTimelineWindowV2", ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        SetWindowSize();
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
        metadataResolver.Clear();
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, plugin.Configuration.TimelineOpacity);
        try
        {
            DrawHeader();

            if (plugin.Configuration.ShowLiveTimeline)
            {
                var entries = plugin.Timeline.Snapshot();
                DrawTimeline(entries, DateTimeOffset.UtcNow, "ActionTimelineCanvas");
            }

            if (practiceExpanded)
            {
                DrawPracticePanel();
            }
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    private void DrawHeader()
    {
        var disclosureIcon = practiceExpanded ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown;
        if (IconButtonHelper.IconTextButton(disclosureIcon, "Práctica", "###practice-disclosure", iconAfter: true))
        {
            SetPracticeExpanded(!practiceExpanded);
        }

        ImGui.SameLine();
        var showLiveTimeline = plugin.Configuration.ShowLiveTimeline;
        if (ImGui.Checkbox("Timeline en vivo", ref showLiveTimeline))
        {
            plugin.SetLiveTimelineVisible(showLiveTimeline);
        }

        if (plugin.Practice is null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(plugin.SelectedReference.Error ?? "Referencia no disponible");
        }
        else if (!plugin.IsPracticeEligible)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"La práctica requiere {plugin.Practice.Rotation.Job}");
        }
    }

    private void DrawPracticePanel()
    {
        ImGui.Separator();
        var practice = plugin.Practice;
        if (practice is null)
        {
            ImGui.TextUnformatted("Práctica de referencia");
            ImGui.TextDisabled(plugin.SelectedReference.Error ?? "La referencia de práctica no está disponible.");
            return;
        }

        var rotation = practice.Rotation;
        ImGui.TextUnformatted($"Práctica de referencia {rotation.Job}");
        var snapshot = practice.Snapshot();
        if (!plugin.IsPracticeEligible)
        {
            ImGui.TextDisabled($"Cambia a {rotation.Job} para iniciar la práctica.");
            return;
        }

        var drewControl = false;
        if (snapshot.State is PracticeState.Running or PracticeState.Paused)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Space) && !ImGui.IsAnyItemActive())
            {
                if (snapshot.State == PracticeState.Paused)
                {
                    practice.Resume();
                }
                else
                {
                    practice.Pause();
                }

                snapshot = practice.Snapshot();
            }

            ImGui.SameLine();
            var pauseLabel = snapshot.State == PracticeState.Paused ? "Reanudar" : "Pausar";
            if (ImGui.Button(pauseLabel))
            {
                if (snapshot.State == PracticeState.Paused)
                {
                    practice.Resume();
                }
                else
                {
                    practice.Pause();
                }

                snapshot = practice.Snapshot();
            }

            drewControl = true;
        }
        if (drewControl) ImGui.SameLine();
        ImGui.TextDisabled(FormatPracticeStatus(snapshot));

        ImGui.SameLine();
        ImGui.TextDisabled($"{snapshot.HitCount} aciertos  {snapshot.MissCount} fallos  {snapshot.WrongCount} errores");

        DrawPracticeTimeline(snapshot);
    }

    private TimelineCanvas BeginCanvas(string canvasId, out ImDrawListPtr drawList)
    {
        var available = ImGui.GetContentRegionAvail();
        var canvasSize = new Vector2(MathF.Max(available.X, 520f), CanvasHeight);
        ImGui.InvisibleButton(canvasId, canvasSize);

        var canvasMin = ImGui.GetItemRectMin();
        var canvasMax = ImGui.GetItemRectMax();
        drawList = ImGui.GetWindowDrawList();
        var axisMin = new Vector2(canvasMin.X + LabelWidth, canvasMin.Y);
        var axisMax = new Vector2(canvasMax.X, canvasMin.Y + AxisHeight);
        var lanesMin = new Vector2(canvasMin.X, axisMax.Y);
        var lanesMax = new Vector2(canvasMax.X, lanesMin.Y + LaneHeight * 2f);

        drawList.AddRectFilled(canvasMin, canvasMax, ImGui.GetColorU32(new Vector4(0.055f, 0.065f, 0.08f, 0.96f)), 4f);
        drawList.AddRect(canvasMin, canvasMax, ImGui.GetColorU32(new Vector4(0.27f, 0.30f, 0.36f, 1f)), 4f);

        return new TimelineCanvas(
            canvasMin,
            canvasMax,
            axisMin,
            lanesMin,
            lanesMax,
            axisMax.X - axisMin.X);
    }

    private readonly record struct TimelineCanvas(
        Vector2 CanvasMin,
        Vector2 CanvasMax,
        Vector2 AxisMin,
        Vector2 LanesMin,
        Vector2 LanesMax,
        float TimelineWidth);

    private void DrawTimeline(
        IReadOnlyList<TimelineEntry> entries,
        DateTimeOffset now,
        string canvasId)
    {
        var canvas = BeginCanvas(canvasId, out var drawList);
        UpdateTimelinePan(ref liveViewOffsetSeconds, canvas.TimelineWidth, ImGuiMouseButton.Right);

        DrawAxis(drawList, canvas.AxisMin, canvas.LanesMax.Y, canvas.TimelineWidth, liveViewOffsetSeconds);
        DrawLanes(drawList, canvas.LanesMin, canvas.LanesMax);
        DrawEntries(drawList, entries, now, liveViewOffsetSeconds, canvas.AxisMin, canvas.LanesMin, canvas.TimelineWidth);
    }

    private void DrawPracticeTimeline(PracticeSnapshot snapshot)
    {
        var canvas = BeginCanvas("PracticeActionTimelineCanvas", out var drawList);
        if (UpdateTimelinePan(ref practiceViewOffsetSeconds, canvas.TimelineWidth, ImGuiMouseButton.Left))
        {
            var lastReferenceSecond = snapshot.ReferenceActions[^1].Offset.TotalSeconds;
            var selectedSecond = Math.Clamp(
                snapshot.Elapsed.TotalSeconds + practiceViewOffsetSeconds,
                0d,
                lastReferenceSecond);
            practiceViewOffsetSeconds = selectedSecond - snapshot.Elapsed.TotalSeconds;
            plugin.SetPracticeStartOffset(TimeSpan.FromSeconds(selectedSecond));
            snapshot = plugin.Practice?.Snapshot() ?? snapshot;
            practiceViewOffsetSeconds = 0d;
        }

        DrawAxis(drawList, canvas.AxisMin, canvas.LanesMax.Y, canvas.TimelineWidth, practiceViewOffsetSeconds);
        DrawLanes(drawList, canvas.LanesMin, canvas.LanesMax);
        DrawPracticeEntries(drawList, snapshot, practiceViewOffsetSeconds, canvas.AxisMin, canvas.LanesMin, canvas.TimelineWidth);

        if (snapshot.State is PracticeState.WaitingForCombat or PracticeState.WaitingForFirstAction)
        {
            var label = snapshot.State == PracticeState.WaitingForCombat
                ? "ESPERANDO COMBATE"
                : "ESPERANDO PRIMERA ACCIÓN DE REFERENCIA";
            var textSize = ImGui.CalcTextSize(label);
            var center = (canvas.CanvasMin + canvas.CanvasMax) / 2f;
            drawList.AddRectFilled(canvas.CanvasMin, canvas.CanvasMax, ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.04f, 0.58f)), 4f);
            drawList.AddText(
                center - textSize / 2f,
                ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.25f, 1f)),
                label);
        }

        if (snapshot.State == PracticeState.Paused)
        {
            var label = "PAUSADO";
            var textSize = ImGui.CalcTextSize(label);
            var center = (canvas.CanvasMin + canvas.CanvasMax) / 2f;
            drawList.AddRectFilled(canvas.CanvasMin, canvas.CanvasMax, ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.04f, 0.68f)), 4f);
            drawList.AddText(
                center - textSize / 2f,
                ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.25f, 1f)),
                label);
        }
    }

    private static void DrawAxis(
        ImDrawListPtr drawList,
        Vector2 axisMin,
        float bottom,
        float timelineWidth,
        double viewOffsetSeconds)
    {
        var gridColor = ImGui.GetColorU32(new Vector4(0.22f, 0.25f, 0.30f, 0.55f));
        var labelColor = ImGui.GetColorU32(new Vector4(0.65f, 0.68f, 0.74f, 1f));
        var nowColor = ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 0.35f, 1f));
        var totalSeconds = ViewHistorySeconds + ViewFutureSeconds;
        var firstSecond = (int)Math.Floor(viewOffsetSeconds - ViewHistorySeconds);
        var lastSecond = (int)Math.Ceiling(viewOffsetSeconds + ViewFutureSeconds);

        for (var second = firstSecond; second <= lastSecond; second++)
        {
            var normalized = (second - viewOffsetSeconds + ViewHistorySeconds) / totalSeconds;
            if (normalized < 0d || normalized > 1d) continue;
            var x = axisMin.X + (float)normalized * timelineWidth;
            var isNow = second == 0;
            var color = isNow ? nowColor : gridColor;
            drawList.AddLine(new Vector2(x, axisMin.Y), new Vector2(x, bottom), color, isNow ? 2f : 1f);

            var label = second == 0 ? "0s" : second > 0 ? $"+{second}s" : $"{second}s";
            var labelX = x + 3f;
            var labelWidth = ImGui.CalcTextSize(label).X;
            labelX = MathF.Min(labelX, axisMin.X + timelineWidth - labelWidth - 3f);
            drawList.AddText(new Vector2(labelX, axisMin.Y + 5f), isNow ? nowColor : labelColor, label);
        }
    }

    private static void DrawLanes(ImDrawListPtr drawList, Vector2 lanesMin, Vector2 lanesMax)
    {
        for (var index = 0; index < LaneLabels.Length; index++)
        {
            var top = lanesMin.Y + index * LaneHeight;
            var bottom = top + LaneHeight;
            var background = index % 2 == 0
                ? new Vector4(0.09f, 0.10f, 0.13f, 0.72f)
                : new Vector4(0.07f, 0.08f, 0.11f, 0.72f);

            drawList.AddRectFilled(
                new Vector2(lanesMin.X, top),
                new Vector2(lanesMax.X, bottom),
                ImGui.GetColorU32(background));
            drawList.AddLine(
                new Vector2(lanesMin.X, bottom),
                new Vector2(lanesMax.X, bottom),
                ImGui.GetColorU32(new Vector4(0.20f, 0.22f, 0.27f, 0.8f)));
            drawList.AddText(
                new Vector2(lanesMin.X + 10f, top + 17f),
                ImGui.GetColorU32(LaneColors[index]),
                LaneLabels[index]);
        }
    }

    private void DrawEntries(
        ImDrawListPtr drawList,
        IReadOnlyList<TimelineEntry> entries,
        DateTimeOffset now,
        double viewOffsetSeconds,
        Vector2 axisMin,
        Vector2 lanesMin,
        float timelineWidth)
    {
        TimelineEntry? hoveredEntry = null;
        ActionMetadata hoveredMetadata = ActionMetadata.Unknown(0);

        foreach (var entry in entries)
        {
            if (!metadataResolver.TryResolve(entry.ActionId, out var actionMetadata) ||
                !TimelineLaneResolver.TryResolve(actionMetadata.TimingClass, out var lane))
            {
                if (!actionMetadata.IsResolved)
                {
                    plugin.Diagnostics.AddOnce(
                        $"metadata-missing:{entry.ActionId}",
                        "Metadata",
                        entry.ActionId,
                        $"Hidden from timeline because the Lumina Action row was not found. Name='{actionMetadata.Name}'.");
                }
                else
                {
                    plugin.Diagnostics.AddOnce(
                        $"metadata-unknown:{entry.ActionId}",
                        "Metadata",
                        entry.ActionId,
                        $"Hidden from timeline because classification is Unknown. Name='{actionMetadata.Name}', Category={actionMetadata.ActionCategoryId}, IsPlayerAction={actionMetadata.IsPlayerAction}.");
                }

                continue;
            }

            if (!TimelinePosition((entry.Timestamp - now).TotalSeconds, viewOffsetSeconds, out var normalized))
            {
                continue;
            }

            var laneIndex = (int)lane;
            var x = axisMin.X + (float)normalized * timelineWidth;
            var top = lanesMin.Y + laneIndex * LaneHeight + (LaneHeight - IconSize) / 2f;
            var iconMin = new Vector2(x - IconSize / 2f, top);
            var iconMax = new Vector2(x + IconSize / 2f, top + IconSize);
            var laneColor = GetLaneColor(lane);

            drawList.AddRectFilled(iconMin, iconMax, ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.035f, 0.92f)), 4f);
            drawList.AddRect(iconMin, iconMax, laneColor, 4f, ImDrawFlags.None, 2f);

            if (actionMetadata.Texture is not null)
            {
                var texture = actionMetadata.Texture.GetWrapOrEmpty();
                drawList.AddImage(texture.Handle, iconMin + new Vector2(1f), iconMax - new Vector2(1f));
            }
            else
            {
                var questionSize = ImGui.CalcTextSize("?");
                drawList.AddText(
                    new Vector2(x - questionSize.X / 2f, top + (IconSize - questionSize.Y) / 2f),
                    laneColor,
                    "?");
            }

            if (ImGui.IsMouseHoveringRect(iconMin, iconMax))
            {
                hoveredEntry = entry;
                hoveredMetadata = actionMetadata;
            }
        }

        if (hoveredEntry is { } hoveredTimelineEntry)
        {
            ImGui.BeginTooltip();
            ImGui.Text(hoveredMetadata.Name);
            ImGui.TextDisabled($"{hoveredMetadata.TimingClass}  |  ID {hoveredTimelineEntry.ActionId}");
            ImGui.TextDisabled(hoveredTimelineEntry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"));
            ImGui.EndTooltip();
        }
    }

    private void DrawPracticeEntries(
        ImDrawListPtr drawList,
        PracticeSnapshot snapshot,
        double viewOffsetSeconds,
        Vector2 axisMin,
        Vector2 lanesMin,
        float timelineWidth)
    {
        var resultByReference = snapshot.ExpectedResults.ToDictionary(result => result.ReferenceIndex);
        string? hoveredText = null;
        string? hoveredDetail = null;

        for (var index = 0; index < snapshot.ReferenceActions.Count; index++)
        {
            var reference = snapshot.ReferenceActions[index];
            if (!TimelinePosition((reference.Offset - snapshot.Elapsed).TotalSeconds, viewOffsetSeconds, out var normalized)) continue;
            if (!TimelineLaneResolver.TryResolve(reference.TimingClass, out var lane)) continue;

            var laneIndex = (int)lane;
            var x = axisMin.X + (float)normalized * timelineWidth;
            var top = lanesMin.Y + laneIndex * LaneHeight + (LaneHeight - IconSize) / 2f;
            var iconMin = new Vector2(x - IconSize / 2f, top);
            var iconMax = new Vector2(x + IconSize / 2f, top + IconSize);
            var laneColor = GetLaneColor(lane);
            var statusColor = laneColor;
            var statusLabel = "Próximo";
            var isMissed = false;

            if (resultByReference.TryGetValue(index, out var result))
            {
                var isHit = result.Kind == PracticeMatchKind.Hit;
                statusColor = isHit
                    ? ImGui.GetColorU32(new Vector4(0.30f, 0.90f, 0.45f, 1f))
                    : ImGui.GetColorU32(new Vector4(1f, 0.30f, 0.30f, 1f));
                statusLabel = PracticeKindLabel(result.Kind);
                isMissed = result.Kind == PracticeMatchKind.Missed;
            }

            drawList.AddRectFilled(iconMin, iconMax, ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.035f, 0.92f)), 4f);
            drawList.AddRect(iconMin, iconMax, statusColor, 4f, ImDrawFlags.None, 2f);

            var metadata = metadataResolver.ResolveReference(reference);
            if (metadata.Texture is not null)
            {
                var texture = metadata.Texture.GetWrapOrEmpty();
                drawList.AddImage(texture.Handle, iconMin + new Vector2(1f), iconMax - new Vector2(1f));
            }
            else
            {
                var questionSize = ImGui.CalcTextSize("?");
                drawList.AddText(
                    new Vector2(x - questionSize.X / 2f, top + (IconSize - questionSize.Y) / 2f),
                    laneColor,
                    "?");
            }

            if (isMissed)
            {
                drawList.AddText(iconMin + new Vector2(11f, 8f), ImGui.GetColorU32(Vector4.One), "X");
            }

            if (ImGui.IsMouseHoveringRect(iconMin, iconMax))
            {
                hoveredText = reference.ActionName;
                hoveredDetail = $"{reference.TimingClass}  |  {reference.Offset.TotalSeconds:0.000}s";
            }
        }

        foreach (var attempt in snapshot.Attempts)
        {
            if (attempt.Kind == PracticeMatchKind.Hit ||
                !TimelinePosition((attempt.Elapsed - snapshot.Elapsed).TotalSeconds, viewOffsetSeconds, out var normalized) ||
                !TimelineLaneResolver.TryResolve(attempt.TimingClass, out var lane)) continue;

            var laneIndex = (int)lane;
            var x = axisMin.X + (float)normalized * timelineWidth;
            var y = lanesMin.Y + laneIndex * LaneHeight + LaneHeight / 2f;
            var markerMin = new Vector2(x - 8f, y - 8f);
            var markerMax = new Vector2(x + 8f, y + 8f);
            var color = ImGui.GetColorU32(new Vector4(1f, 0.25f, 0.25f, 1f));
            drawList.AddCircleFilled(new Vector2(x, y), 7f, color, 20);
            drawList.AddText(new Vector2(x - 4f, y - 8f), ImGui.GetColorU32(Vector4.One), "!");

            if (ImGui.IsMouseHoveringRect(markerMin, markerMax))
            {
                hoveredText = attempt.ActionName;
                hoveredDetail = $"Jugador: {PracticeKindLabel(attempt.Kind)}  |  {attempt.TimingClass}  |  {attempt.Elapsed.TotalSeconds:0.000}s";
            }
        }

        if (hoveredText is not null)
        {
            ImGui.BeginTooltip();
            ImGui.Text(hoveredText);
            ImGui.TextDisabled(hoveredDetail ?? string.Empty);
            ImGui.EndTooltip();
        }
    }

    private static bool TimelinePosition(double relativeSeconds, double viewOffsetSeconds, out double normalized)
    {
        var totalSeconds = ViewHistorySeconds + ViewFutureSeconds;
        normalized = (relativeSeconds - viewOffsetSeconds + ViewHistorySeconds) / totalSeconds;
        return normalized >= 0d && normalized <= 1d;
    }

    private static bool UpdateTimelinePan(
        ref double viewOffsetSeconds,
        float timelineWidth,
        ImGuiMouseButton mouseButton)
    {
        if (timelineWidth <= 0f || !ImGui.IsItemHovered()) return false;
        if (!ImGui.IsMouseDragging(mouseButton)) return false;

        var totalSeconds = ViewHistorySeconds + ViewFutureSeconds;
        var dragSeconds = ImGui.GetIO().MouseDelta.X / timelineWidth * totalSeconds;
        viewOffsetSeconds = Math.Clamp(viewOffsetSeconds - dragSeconds, -MaxTimelinePanSeconds, MaxTimelinePanSeconds);
        return true;
    }

    private static string FormatPracticeStatus(PracticeSnapshot snapshot)
    {
        return snapshot.State switch
        {
            PracticeState.Idle => "Listo",
            PracticeState.WaitingForCombat => FormatWaitingStatus("Esperando combate", snapshot.Elapsed),
            PracticeState.WaitingForFirstAction => FormatWaitingStatus("Esperando primera acción de referencia", snapshot.Elapsed),
            PracticeState.Running => $"En marcha {snapshot.Elapsed.TotalSeconds:0.0}s  ({snapshot.ResolvedCount}/{snapshot.TotalCount})",
            PracticeState.Paused => $"Pausado  {snapshot.Elapsed.TotalSeconds:0.0}s  ({snapshot.ResolvedCount}/{snapshot.TotalCount})",
            PracticeState.Completed => $"Completado  ({snapshot.HitCount}/{snapshot.TotalCount})",
            _ => string.Empty,
        };
    }

    private static string FormatWaitingStatus(string label, TimeSpan offset) =>
        offset > TimeSpan.Zero ? $"{label} en {offset.TotalSeconds:0.0}s" : label;

    private static string PracticeKindLabel(PracticeMatchKind kind) => kind switch
    {
        PracticeMatchKind.Hit => "Acierto",
        PracticeMatchKind.Missed => "Fallado",
        PracticeMatchKind.Wrong => "Error",
        PracticeMatchKind.Extra => "Extra",
        _ => kind.ToString(),
    };

    private static uint GetLaneColor(TimelineLane lane)
    {
        var index = (int)lane;
        return index >= 0 && index < LaneColors.Length
            ? ImGui.GetColorU32(LaneColors[index])
            : ImGui.GetColorU32(new Vector4(0.48f, 0.50f, 0.56f, 1f));
    }

    private void SetPracticeExpanded(bool expanded)
    {
        practiceExpanded = expanded;
        SetWindowSize();
    }

    public void RefreshWindowSize()
    {
        SetWindowSize();
    }

    private void SetWindowSize()
    {
        var liveTimelineHeight = plugin.Configuration.ShowLiveTimeline ? CanvasHeight : 0f;
        var collapsedHeight = CollapsedWindowHeight - CanvasHeight + liveTimelineHeight;
        var expandedHeight = ExpandedWindowHeight - CanvasHeight + liveTimelineHeight;
        var height = practiceExpanded ? expandedHeight : collapsedHeight;
        Size = new Vector2(WindowWidth, height);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(WindowWidth, height),
            MaximumSize = new Vector2(WindowWidth, height),
        };
    }
}
