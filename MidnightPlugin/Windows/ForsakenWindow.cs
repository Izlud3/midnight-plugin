using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MidnightPlugin.Core;

namespace MidnightPlugin.Windows;

public sealed class ForsakenWindow : Window, IDisposable
{
    private const float JobIconSize = 20;
    private readonly Plugin plugin;
    private readonly JobIconResolver jobIcons = new();
    private int observedResultCount = -1;
    private float arenaZoom = 1;
    private Vector2 arenaPan;
    private int arenaLayers = 2;
    private long activeSnapshotTicks = long.MinValue;

    public ForsakenWindow(Plugin plugin) : base("DMU Review###MidnightForsaken")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(900, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(680, 400), MaximumSize = new(float.MaxValue, float.MaxValue) };
    }

    public void Dispose() => jobIcons.Clear();

    public override void Draw()
    {
        if (!plugin.Configuration.ForsakenFailureCardsEnabled)
        {
            ImGui.TextDisabled("Las tarjetas de fallos de DMU están desactivadas en Configuración.");
            return;
        }

        var pull = plugin.EncounterSessions.ActivePull ?? plugin.EncounterSessions.HistorySnapshot().LastOrDefault();
        var results = pull?.ForsakenResults ?? [];
        if (results.Count == 0)
        {
            ImGui.TextDisabled("Esperando resultados...");
            return;
        }

        var selectNewest = observedResultCount != results.Count;
        observedResultCount = results.Count;
        if (!ImGui.BeginTabBar("forsaken-pairs", ImGuiTabBarFlags.FittingPolicyScroll)) return;
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            var marker = result.Verdict switch
            {
                MechanicVerdict.Success => "✓",
                MechanicVerdict.Failure => "X",
                _ => "?",
            };
            var tabFlags = selectNewest && index == results.Count - 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (!ImGui.BeginTabItem($"{marker} Tower {result.PairNumber}##forsaken-pair-{index}", tabFlags)) continue;
            var color = result.Verdict == MechanicVerdict.Failure ? new Vector4(1, .3f, .3f, 1) : new Vector4(1, .75f, .25f, 1);
            var verdict = result.Verdict switch
            {
                MechanicVerdict.Success => "Éxito",
                MechanicVerdict.Failure => "Fallo",
                _ => "Evidencia insuficiente",
            };
            if (result.Verdict == MechanicVerdict.Success) color = new(.25f, .9f, .45f, 1);
            ImGui.TextColored(color, verdict);
            if (result.Snapshot is not null) DrawSnapshot(result.Snapshot);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawSnapshot(ArenaSnapshot snapshot)
    {
        if (activeSnapshotTicks != snapshot.Elapsed.Ticks)
        {
            activeSnapshotTicks = snapshot.Elapsed.Ticks;
            ResetArenaView();
        }

        DrawArenaControls();
        ImGui.Spacing();
        var available = ImGui.GetContentRegionAvail();
        const float spacing = 8;
        if (available.X >= 820)
        {
            var mapSize = Math.Clamp(available.X * .42f, 300, 400);
            DrawArena(snapshot, mapSize);
            ImGui.SameLine(0, spacing);
            DrawPlayerEvidenceTable(snapshot, new(available.X - mapSize - spacing, mapSize));
        }
        else
        {
            var mapSize = Math.Clamp(Math.Min(available.X, 380), 280, 380);
            var startX = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(startX + Math.Max(0, (available.X - mapSize) / 2));
            DrawArena(snapshot, mapSize);
            ImGui.SetCursorPosX(startX);
            ImGui.Spacing();
            var tableHeight = 28 + ForsakenPresentation.OrderedPlayers(snapshot).Count * 30;
            DrawPlayerEvidenceTable(snapshot, new(available.X, tableHeight));
        }

    }

    private void DrawArenaControls()
    {
        foreach (var (label, value) in new[] { ("Towers", 0), ("Shapes", 1), ("Both", 2) })
        {
            if (value > 0) ImGui.SameLine();
            var selected = arenaLayers == value;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.2f, .48f, .7f, 1));
            if (ImGui.SmallButton($"{label}##forsaken-layer-{value}")) arenaLayers = value;
            if (selected) ImGui.PopStyleColor();
        }
    }

    private void ResetArenaView()
    {
        arenaZoom = 1;
        arenaPan = Vector2.Zero;
    }

    private void DrawArena(ArenaSnapshot snapshot, float size)
    {
        var origin = ImGui.GetCursorScreenPos();
        var canvasMax = origin + new Vector2(size);
        ImGui.InvisibleButton($"forsaken-snapshot-{snapshot.Elapsed.Ticks}", new(size));

        var io = ImGui.GetIO();
        if (ImGui.IsItemHovered() && io.MouseWheel != 0)
            arenaZoom = Math.Clamp(arenaZoom + io.MouseWheel * .1f, .75f, 2.5f);
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            arenaPan += io.MouseDelta;
            var panLimit = size * .75f;
            arenaPan = Vector2.Clamp(arenaPan, new(-panLimit), new(panLimit));
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            arenaZoom = 1;
            arenaPan = Vector2.Zero;
        }

        var center = origin + new Vector2(size / 2) + arenaPan;
        var scale = size / 48f * arenaZoom;
        var rotation = ForsakenPresentation.NormalizedTowerRotation(snapshot.Towers);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, canvasMax, ImGui.GetColorU32(new Vector4(.07f, .08f, .1f, 1)), 4);
        draw.PushClipRect(origin, canvasMax, true);
        if (arenaLayers is 1 or 2) DrawMechanicShapes(draw, snapshot, center, scale, rotation);
        draw.AddCircle(center, 20 * scale, ImGui.GetColorU32(new Vector4(.65f, .65f, .7f, 1)), 64, 2);
        DrawCompass(draw, center, scale, rotation);
        if (arenaLayers is 0 or 2)
        {
            foreach (var candidate in ForsakenPresentation.TowerCandidateOffsets)
            {
                var point = center + ForsakenPresentation.Rotate(candidate, rotation) * scale;
                draw.AddCircleFilled(point, 3, ImGui.GetColorU32(new Vector4(.55f, .58f, .65f, .4f)), 12);
            }
            foreach (var (tower, index) in snapshot.Towers.Select((tower, index) => (tower, index + 1)))
            {
                var point = center + ForsakenPresentation.Rotate(
                    new(tower.X - ForsakenPresentation.ArenaCenter, tower.Y - ForsakenPresentation.ArenaCenter), rotation) * scale;
                var color = tower.IsResolvedCorrectly ? new Vector4(.25f, .9f, .45f, 1) : new Vector4(1, .25f, .25f, 1);
                var fill = color with { W = .2f };
                draw.AddCircleFilled(point, ArenaTower.Radius * scale, ImGui.GetColorU32(fill), 40);
                draw.AddCircle(point, ArenaTower.Radius * scale, ImGui.GetColorU32(color), 40, 2.5f);
                draw.AddText(point - new Vector2(11, 8), ImGui.GetColorU32(Vector4.One), $"T{index}");
            }
        }
        foreach (var player in snapshot.Players.Where(player => player.Position is not null))
        {
            var position = player.Position!.Value;
            var point = ToCanvas(position, center, scale, rotation);
            var evidence = ForsakenPresentation.EvidenceFor(snapshot, player.ActorId);
            var problem = (evidence & (ForsakenPlayerEvidence.Died | ForsakenPlayerEvidence.FailedCheck)) != 0;
            var color = problem ? new Vector4(1, .15f, .15f, 1) : player.TookTower ? new Vector4(.35f, .8f, 1, 1) : new Vector4(.7f, .4f, 1, 1);
            draw.AddCircleFilled(point, 11, ImGui.GetColorU32(new Vector4(.06f, .07f, .09f, .95f)), 24);
            draw.AddCircle(point, 11, ImGui.GetColorU32(color), 24, 2);
            var jobSize = ImGui.CalcTextSize(player.Job);
            draw.AddText(point - jobSize / 2, ImGui.GetColorU32(Vector4.One), player.Job);
        }
        draw.PopClipRect();
    }

    private static void DrawCompass(ImDrawListPtr draw, Vector2 center, float scale, float rotation)
    {
        foreach (var (label, offset) in new[]
                 {
                     ("N", new Vector2(0, -18)), ("E", new Vector2(18, 0)),
                     ("S", new Vector2(0, 18)), ("W", new Vector2(-18, 0)),
                 })
        {
            var point = center + ForsakenPresentation.Rotate(offset, rotation) * scale;
            var textSize = ImGui.CalcTextSize(label);
            draw.AddText(point - textSize / 2, ImGui.GetColorU32(new Vector4(.75f, .77f, .82f, .85f)), label);
        }
    }

    private static void DrawMechanicShapes(ImDrawListPtr draw, ArenaSnapshot snapshot, Vector2 center, float scale, float rotation)
    {
        foreach (var stack in snapshot.Stacks)
        {
            var source = FindPlayer(snapshot, stack.SourceId);
            var position = source?.Position ?? CenterOfPlayers(snapshot, stack.PlayerIds);
            if (position is not { } stackPosition) continue;

            var point = ToCanvas(stackPosition, center, scale, rotation);
            var radius = 5 * scale;
            var color = stack.IsResolvedCorrectly
                ? new Vector4(.2f, .75f, 1, 1)
                : new Vector4(1, .25f, .25f, 1);
            draw.AddCircleFilled(point, radius, ImGui.GetColorU32(color with { W = .18f }), 48);
            draw.AddCircle(point, radius, ImGui.GetColorU32(color), 48, 2);
        }

        foreach (var cone in snapshot.Cones)
        {
            var source = FindPlayer(snapshot, cone.SourceId) ?? InferConeSource(snapshot, cone);
            if (source?.Position is not { } sourcePosition) continue;

            var target = FindConeTarget(snapshot, cone, source);
            if (target?.Position is not { } targetPosition) continue;

            DrawCone(draw, sourcePosition, targetPosition, center, scale, rotation, cone.IsResolvedCorrectly);
        }
    }

    private static void DrawCone(
        ImDrawListPtr draw,
        EncounterPosition source,
        EncounterPosition target,
        Vector2 arenaCenter,
        float scale,
        float rotation,
        bool success)
    {
        const float arenaRadius = 20;
        const int segments = 24;
        var sourceOffset = ForsakenPresentation.Rotate(new(source.X - 100, source.Z - 100), rotation);
        if (sourceOffset.LengthSquared() > arenaRadius * arenaRadius) return;

        var targetOffset = ForsakenPresentation.Rotate(new(target.X - 100, target.Z - 100), rotation);
        var direction = Vector2.Normalize(targetOffset - sourceOffset);
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)) return;

        var sourcePoint = arenaCenter + sourceOffset * scale;
        var color = success ? new Vector4(1, .65f, .15f, 1) : new Vector4(1, .25f, .25f, 1);
        var fill = ImGui.GetColorU32(color with { W = .2f });
        var outline = ImGui.GetColorU32(color);
        var previous = ConeBoundaryPoint(sourceOffset, direction, -MathF.PI / 4, arenaRadius, arenaCenter, scale);
        draw.AddLine(sourcePoint, previous, outline, 2);

        for (var index = 1; index <= segments; index++)
        {
            var angle = -MathF.PI / 4 + MathF.PI / 2 * index / segments;
            var current = ConeBoundaryPoint(sourceOffset, direction, angle, arenaRadius, arenaCenter, scale);
            draw.AddTriangleFilled(sourcePoint, previous, current, fill);
            draw.AddLine(previous, current, outline, 1.5f);
            previous = current;
        }

        draw.AddLine(sourcePoint, previous, outline, 2);
    }

    private static Vector2 ConeBoundaryPoint(
        Vector2 sourceOffset,
        Vector2 direction,
        float angle,
        float arenaRadius,
        Vector2 arenaCenter,
        float scale)
    {
        var sin = MathF.Sin(angle);
        var cos = MathF.Cos(angle);
        var ray = new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
        var projection = Vector2.Dot(sourceOffset, ray);
        var distance = -projection + MathF.Sqrt(projection * projection + arenaRadius * arenaRadius - sourceOffset.LengthSquared());
        return arenaCenter + (sourceOffset + ray * distance) * scale;
    }

    private static ForsakenParticipant? FindConeTarget(
        ArenaSnapshot snapshot,
        ConeResolution cone,
        ForsakenParticipant source)
    {
        var hitIds = cone.PlayerIds?.ToHashSet();
        var candidates = snapshot.Players.Where(player =>
            player.ActorId != source.ActorId &&
            player.Position is not null &&
            (hitIds is null || hitIds.Count == 0 || hitIds.Contains(player.ActorId)));
        return candidates
            .OrderBy(player => DistanceSquared(source.Position!.Value, player.Position!.Value))
            .FirstOrDefault();
    }

    private static ForsakenParticipant? InferConeSource(ArenaSnapshot snapshot, ConeResolution cone)
    {
        var positioned = snapshot.Players.Where(player => player.Position is not null).ToArray();
        var towerCandidates = positioned.Where(player => player.TookTower).ToArray();
        var candidates = towerCandidates.Length > 0 ? towerCandidates : positioned;
        var hitIds = cone.PlayerIds?.ToHashSet() ?? [];

        return candidates
            .Where(candidate => !hitIds.Contains(candidate.ActorId))
            .OrderBy(candidate => ConeSourceMismatch(candidate, positioned, hitIds))
            .ThenBy(candidate => DistanceToClosestHit(candidate, positioned, hitIds))
            .FirstOrDefault();
    }

    private static int ConeSourceMismatch(
        ForsakenParticipant candidate,
        IReadOnlyList<ForsakenParticipant> players,
        IReadOnlySet<ulong> hitIds)
    {
        var others = players.Where(player => player.ActorId != candidate.ActorId).ToArray();
        var closest = others
            .OrderBy(player => DistanceSquared(candidate.Position!.Value, player.Position!.Value))
            .FirstOrDefault();
        var target = others.Where(player => hitIds.Contains(player.ActorId))
            .OrderBy(player => DistanceSquared(candidate.Position!.Value, player.Position!.Value))
            .FirstOrDefault() ?? closest;
        if (target is null) return int.MaxValue;

        var predicted = others.Where(player => IsInsideCone(candidate.Position!.Value, target.Position!.Value, player.Position!.Value))
            .Select(player => player.ActorId)
            .ToHashSet();
        var mismatch = predicted.Count(id => !hitIds.Contains(id)) + hitIds.Count(id => !predicted.Contains(id));
        if (hitIds.Count > 0 && closest is not null && !hitIds.Contains(closest.ActorId)) mismatch += players.Count;
        return mismatch;
    }

    private static float DistanceToClosestHit(
        ForsakenParticipant candidate,
        IReadOnlyList<ForsakenParticipant> players,
        IReadOnlySet<ulong> hitIds) =>
        players.Where(player => hitIds.Contains(player.ActorId))
            .Select(player => DistanceSquared(candidate.Position!.Value, player.Position!.Value))
            .DefaultIfEmpty(float.MaxValue)
            .Min();

    private static bool IsInsideCone(EncounterPosition source, EncounterPosition target, EncounterPosition point)
    {
        var aim = Vector2.Normalize(new Vector2(target.X - source.X, target.Z - source.Z));
        var offset = new Vector2(point.X - source.X, point.Z - source.Z);
        var distanceSquared = offset.LengthSquared();
        if (distanceSquared <= float.Epsilon || distanceSquared > 40 * 40) return false;
        return Vector2.Dot(aim, offset) >= MathF.Sqrt(distanceSquared) * MathF.Cos(MathF.PI / 4);
    }

    private static EncounterPosition? CenterOfPlayers(ArenaSnapshot snapshot, IReadOnlyList<ulong>? playerIds)
    {
        if (playerIds is null || playerIds.Count == 0) return null;
        var ids = playerIds.ToHashSet();
        var positions = snapshot.Players
            .Where(player => ids.Contains(player.ActorId) && player.Position is not null)
            .Select(player => player.Position!.Value)
            .ToArray();
        if (positions.Length == 0) return null;
        return new(
            positions.Average(position => position.X),
            positions.Average(position => position.Y),
            positions.Average(position => position.Z),
            0);
    }

    private static ForsakenParticipant? FindPlayer(ArenaSnapshot snapshot, ulong actorId) =>
        actorId == 0 ? null : snapshot.Players.FirstOrDefault(player => player.ActorId == actorId);

    private static Vector2 ToCanvas(EncounterPosition position, Vector2 center, float scale, float rotation) =>
        center + ForsakenPresentation.Rotate(new(position.X - 100, position.Z - 100), rotation) * scale;

    private static float DistanceSquared(EncounterPosition left, EncounterPosition right)
    {
        var x = left.X - right.X;
        var z = left.Z - right.Z;
        return x * x + z * z;
    }

    private void DrawPlayerEvidenceTable(ArenaSnapshot snapshot, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.07f, .08f, .1f, 1));
        if (ImGui.BeginChild(
                $"forsaken-party-{snapshot.Elapsed.Ticks}",
                size,
                true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (ImGui.BeginTable(
                    $"forsaken-party-table-{snapshot.Elapsed.Ticks}",
                    4,
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV |
                    ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 36);
                ImGui.TableSetupColumn("Antes", ImGuiTableColumnFlags.WidthStretch, 1.35f);
                ImGui.TableSetupColumn("Después", ImGuiTableColumnFlags.WidthStretch, 1.55f);
                ImGui.TableSetupColumn("Evidencia", ImGuiTableColumnFlags.WidthStretch, 1.1f);
                ImGui.TableHeadersRow();

                foreach (var player in ForsakenPresentation.OrderedPlayers(snapshot))
                {
                    var evidence = ForsakenPresentation.EvidenceFor(snapshot, player.ActorId);
                    var problem = (evidence & (ForsakenPlayerEvidence.Died | ForsakenPlayerEvidence.FailedCheck)) != 0;
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 30);
                    if (problem)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(.45f, .08f, .08f, .22f)));

                    ImGui.TableNextColumn();
                    var playerColor = player.Died ? new Vector4(1, .28f, .28f, 1) : problem
                        ? new Vector4(1, .66f, .35f, 1)
                        : Vector4.One;
                    if (jobIcons.TryResolve(player.Job, out var jobTexture) &&
                        jobTexture!.TryGetWrap(out var jobIcon, out _))
                    {
                        ImGui.Image(jobIcon.Handle, new Vector2(JobIconSize));
                    }
                    else
                    {
                        ImGui.TextColored(playerColor, player.Job);
                    }

                    ImGui.TableNextColumn();
                    DrawHpShieldBar(player.HpAtPairStart, player.ShieldHpAtPairStart, player.MaxHp,
                        $"before-{snapshot.Elapsed.Ticks}-{player.ActorId}");

                    ImGui.TableNextColumn();
                    DrawHpShieldBar(player.HpAtResolution, player.ShieldHpAtResolution, player.MaxHp,
                        $"after-{snapshot.Elapsed.Ticks}-{player.ActorId}",
                        showShieldPercentage: false,
                        damageDelta: ForsakenPresentation.EffectiveHpDelta(player));

                    ImGui.TableNextColumn();
                    var labels = EvidenceLabels(snapshot, player.ActorId);
                    if (labels.Count == 0) ImGui.TextDisabled("-");
                    else ImGui.TextColored(problem ? new Vector4(1, .5f, .4f, 1) : new Vector4(.55f, .8f, 1, 1), string.Join(", ", labels));
                }

                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static void DrawHpShieldBar(
        uint? currentHp,
        uint? shieldHp,
        uint? maxHp,
        string id,
        bool showShieldPercentage = true,
        long? damageDelta = null)
    {
        var segments = ForsakenPresentation.HpBarSegments(currentHp, shieldHp, maxHp);
        if (!segments.IsAvailable)
        {
            ImGui.TextDisabled("Unavailable");
            return;
        }

        var width = Math.Max(48, ImGui.GetContentRegionAvail().X);
        var height = 21f;
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);
        ImGui.InvisibleButton($"##{id}", size);
        var draw = ImGui.GetWindowDrawList();
        var end = start + size;
        draw.AddRectFilled(start, end, ImGui.GetColorU32(new Vector4(.13f, .14f, .17f, 1)), 3);

        var hpEnd = start + new Vector2(size.X * segments.HpRatio, size.Y);
        if (segments.HpRatio > 0)
            draw.AddRectFilled(start, hpEnd, ImGui.GetColorU32(new Vector4(.2f, .72f, .38f, 1)), 3);
        if (segments.ShieldRatio > 0)
        {
            var shieldEnd = hpEnd + new Vector2(size.X * segments.ShieldRatio, 0);
            draw.AddRectFilled(hpEnd, shieldEnd, ImGui.GetColorU32(new Vector4(.25f, .72f, .92f, 1)), 3);
        }
        if (segments.OverflowShieldRatio > 0)
        {
            var overflowEnd = start + new Vector2(size.X * segments.OverflowShieldRatio, 4);
            draw.AddRectFilled(start, overflowEnd, ImGui.GetColorU32(new Vector4(.42f, .86f, 1, 1)), 2);
        }
        draw.AddRect(start, end, ImGui.GetColorU32(new Vector4(.48f, .5f, .56f, 1)), 3);

        var hpPercent = maxHp > 0 && currentHp is not null ? 100d * currentHp.Value / maxHp.Value : 0;
        var label = !showShieldPercentage
            ? $"{hpPercent:0}%"
            : shieldHp is null
                ? $"{hpPercent:0}% +?"
                : shieldHp == 0
                    ? $"{hpPercent:0}%"
                    : $"{hpPercent:0}% +{100d * shieldHp.Value / maxHp!.Value:0}%";
        var damageLabel = damageDelta is { } delta ? $" ({FormatSignedCompact(delta)})" : string.Empty;
        var labelSize = ImGui.CalcTextSize(label);
        var damageSize = ImGui.CalcTextSize(damageLabel);
        var textPosition = start + new Vector2(
            Math.Max(3, (size.X - labelSize.X - damageSize.X) / 2),
            Math.Max(1, (size.Y - labelSize.Y) / 2));
        draw.AddText(textPosition + Vector2.One, ImGui.GetColorU32(new Vector4(0, 0, 0, .85f)), label);
        draw.AddText(textPosition, ImGui.GetColorU32(Vector4.One), label);
        if (damageLabel.Length > 0)
        {
            var damagePosition = textPosition + new Vector2(labelSize.X, 0);
            draw.AddText(damagePosition + Vector2.One, ImGui.GetColorU32(new Vector4(0, 0, 0, .85f)), damageLabel);
            draw.AddText(damagePosition, ImGui.GetColorU32(new Vector4(1, .32f, .28f, 1)), damageLabel);
        }
    }

    private static string FormatSignedCompact(long value)
    {
        var magnitude = Math.Abs((double)value);
        var formatted = magnitude >= 1_000_000 ? $"{magnitude / 1_000_000:0.#}m" : magnitude >= 1_000 ? $"{magnitude / 1_000:0.#}k" : $"{magnitude:0}";
        return value > 0 ? $"+{formatted}" : value < 0 ? $"-{formatted}" : "0";
    }

    private static IReadOnlyList<string> EvidenceLabels(ArenaSnapshot snapshot, ulong actorId)
    {
        var labels = new List<string>();
        foreach (var (tower, index) in snapshot.Towers.Select((tower, index) => (tower, index + 1)))
            if (tower.SoakerIds?.Contains(actorId) == true) labels.Add($"T{index}");
        foreach (var (stack, index) in snapshot.Stacks.Select((stack, index) => (stack, index + 1)))
            if (stack.SourceId == actorId || stack.PlayerIds?.Contains(actorId) == true) labels.Add($"S{index}");
        foreach (var (cone, index) in snapshot.Cones.Select((cone, index) => (cone, index + 1)))
        {
            if (cone.SourceId == actorId) labels.Add($"C{index} src");
            if (cone.PlayerIds?.Contains(actorId) == true) labels.Add($"C{index} hit");
        }
        if (snapshot.Players.FirstOrDefault(player => player.ActorId == actorId)?.Died == true) labels.Add("Muerto");
        return labels;
    }

}
