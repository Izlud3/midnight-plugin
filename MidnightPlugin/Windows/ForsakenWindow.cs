using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MidnightPlugin.Core;

namespace MidnightPlugin.Windows;

public sealed class ForsakenWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private int observedResultCount = -1;
    private float arenaZoom = 1;
    private Vector2 arenaPan;

    public ForsakenWindow(Plugin plugin) : base("DMU Review###MidnightForsaken")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(650, 620);
        SizeCondition = ImGuiCond.Always;
        SizeConstraints = new() { MinimumSize = new(650, 620), MaximumSize = new(650, 620) };
    }

    public void Dispose() { }

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
                MechanicVerdict.Failure => "✕",
                _ => "?",
            };
            var tabFlags = selectNewest && index == results.Count - 1 ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (!ImGui.BeginTabItem($"{marker} Tower {result.PairNumber}##forsaken-pair-{index}", tabFlags)) continue;
            var color = result.Verdict == MechanicVerdict.Failure ? new Vector4(1, .3f, .3f, 1) : new Vector4(1, .75f, .25f, 1);
            ImGui.Text($"Tower {result.PairNumber} • {result.Elapsed:mm\\:ss\\.fff}");
            var verdict = result.Verdict switch
            {
                MechanicVerdict.Success => "Éxito",
                MechanicVerdict.Failure => "Fallo",
                _ => "Evidencia insuficiente",
            };
            if (result.Verdict == MechanicVerdict.Success) color = new(.25f, .9f, .45f, 1);
            ImGui.TextColored(color, verdict);
            if (result.Snapshot is null)
            {
                foreach (var reason in result.Reasons) ImGui.BulletText(reason);
            }
            else
            {
                DrawSnapshot(result.Snapshot);
            }
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawSnapshot(ArenaSnapshot snapshot)
    {
        var available = ImGui.GetContentRegionAvail();
        var checkCount = snapshot.Towers.Count + snapshot.Stacks.Count + snapshot.Cones.Count;
        var columns = available.X >= 420 ? 2 : 1;
        var rows = (checkCount + columns - 1) / columns;
        var checksHeight = ImGui.GetTextLineHeightWithSpacing() + rows * 66 + Math.Max(0, rows - 1) * ImGui.GetStyle().ItemSpacing.Y;
        var size = Math.Min(360, Math.Min(available.X, Math.Max(220, available.Y - checksHeight)));
        var startX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(startX + Math.Max(0, (available.X - size) / 2));
        DrawArena(snapshot, size);
        ImGui.SetCursorPosX(startX);
        DrawResolutionChecks(snapshot, available.X);
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
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, canvasMax, ImGui.GetColorU32(new Vector4(.07f, .08f, .1f, 1)), 4);
        draw.PushClipRect(origin, canvasMax, true);
        DrawMechanicShapes(draw, snapshot, center, scale);
        draw.AddCircle(center, 20 * scale, ImGui.GetColorU32(new Vector4(.65f, .65f, .7f, 1)), 64, 2);
        foreach (var (tower, index) in snapshot.Towers.Select((tower, index) => (tower, index + 1)))
        {
            var point = center + new Vector2((tower.X - 100) * scale, (tower.Y - 100) * scale);
            var color = tower.IsResolvedCorrectly ? new Vector4(.25f, .9f, .45f, 1) : new Vector4(1, .25f, .25f, 1);
            var fill = color with { W = .2f };
            draw.AddCircleFilled(point, ArenaTower.Radius * scale, ImGui.GetColorU32(fill), 40);
            draw.AddCircle(point, ArenaTower.Radius * scale, ImGui.GetColorU32(color), 40, 2.5f);
            draw.AddText(point - new Vector2(11, 8), ImGui.GetColorU32(Vector4.One), $"T{index}");
        }
        foreach (var player in snapshot.Players.Where(player => player.Position is not null))
        {
            var position = player.Position!.Value;
            var point = center + new Vector2((position.X - 100) * scale, (position.Z - 100) * scale);
            var color = player.Died ? new Vector4(1, .15f, .15f, 1) : player.TookTower ? new Vector4(.35f, .8f, 1, 1) : new Vector4(.7f, .4f, 1, 1);
            draw.AddCircleFilled(point, 5, ImGui.GetColorU32(color));
            draw.AddText(point + new Vector2(6, -7), ImGui.GetColorU32(Vector4.One), player.Job);
        }
        draw.PopClipRect();
    }

    private static void DrawMechanicShapes(ImDrawListPtr draw, ArenaSnapshot snapshot, Vector2 center, float scale)
    {
        foreach (var stack in snapshot.Stacks)
        {
            var source = FindPlayer(snapshot, stack.SourceId);
            var position = source?.Position ?? CenterOfPlayers(snapshot, stack.PlayerIds);
            if (position is not { } stackPosition) continue;

            var point = ToCanvas(stackPosition, center, scale);
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

            DrawCone(draw, sourcePosition, targetPosition, center, scale, cone.IsResolvedCorrectly);
        }
    }

    private static void DrawCone(
        ImDrawListPtr draw,
        EncounterPosition source,
        EncounterPosition target,
        Vector2 arenaCenter,
        float scale,
        bool success)
    {
        const float arenaRadius = 20;
        const int segments = 24;
        var sourceOffset = new Vector2(source.X - 100, source.Z - 100);
        if (sourceOffset.LengthSquared() > arenaRadius * arenaRadius) return;

        var direction = Vector2.Normalize(new Vector2(target.X - source.X, target.Z - source.Z));
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
        return candidates.MinBy(player => DistanceSquared(source.Position!.Value, player.Position!.Value));
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
        var closest = others.MinBy(player => DistanceSquared(candidate.Position!.Value, player.Position!.Value));
        var target = others.Where(player => hitIds.Contains(player.ActorId))
            .MinBy(player => DistanceSquared(candidate.Position!.Value, player.Position!.Value)) ?? closest;
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

    private static Vector2 ToCanvas(EncounterPosition position, Vector2 center, float scale) =>
        center + new Vector2((position.X - 100) * scale, (position.Z - 100) * scale);

    private static float DistanceSquared(EncounterPosition left, EncounterPosition right)
    {
        var x = left.X - right.X;
        var z = left.Z - right.Z;
        return x * x + z * z;
    }

    private static void DrawResolutionChecks(ArenaSnapshot snapshot, float available)
    {
        ImGui.Text("Comprobaciones de resolución");
        var checks = new List<(string Label, string Detail, bool Success, IReadOnlyList<ulong>? PlayerIds, ConeResolution? Cone)>();
        foreach (var (tower, index) in snapshot.Towers.Select((tower, index) => (tower, index + 1)))
            checks.Add(($"Torre {index}", $"{tower.SoakerCount} / {ArenaTower.ExpectedSoakers} soakers", tower.IsResolvedCorrectly, tower.SoakerIds, null));
        foreach (var (stack, index) in snapshot.Stacks.Select((stack, index) => (stack, index + 1)))
            checks.Add(($"Stack {index}", $"{stack.PlayerCount} / {StackResolution.ExpectedPlayers} jugadores", stack.IsResolvedCorrectly, stack.PlayerIds, null));
        foreach (var (cone, index) in snapshot.Cones.Select((cone, index) => (cone, index + 1)))
            checks.Add(($"Cono {index}", $"{cone.PlayerCount} / {ConeResolution.ExpectedPlayers} otros jugadores alcanzados", cone.IsResolvedCorrectly, cone.PlayerIds, cone));

        const float spacing = 8;
        var columns = available >= 420 ? 2 : 1;
        var width = columns == 2 ? (available - spacing) / 2 : available;
        for (var index = 0; index < checks.Count; index++)
        {
            if (index % columns != 0) ImGui.SameLine(0, spacing);
            var check = checks[index];
            var color = check.Success ? new Vector4(.25f, .9f, .45f, 1) : new Vector4(1, .3f, .3f, 1);
            var origin = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(origin, origin + new Vector2(width, 66), ImGui.GetColorU32(new Vector4(.1f, .11f, .14f, 1)), 5);
            draw.AddRect(origin, origin + new Vector2(width, 66), ImGui.GetColorU32(color), 5, ImDrawFlags.None, 1.5f);
            ImGui.SetCursorScreenPos(origin + new Vector2(10, 9));
            ImGui.TextColored(color, check.Success ? $"✓  {check.Label}" : $"✕  {check.Label}");
            ImGui.SetCursorScreenPos(origin + new Vector2(10, 34));
            ImGui.TextDisabled(check.Detail);
            ImGui.SetCursorScreenPos(origin);
            ImGui.InvisibleButton($"forsaken-check-{snapshot.Elapsed.Ticks}-{index}", new(width, 66));
            if (ImGui.IsItemHovered())
            {
                if (check.Cone is { } cone)
                    DrawConeTooltip(snapshot, cone);
                else
                    DrawPlayerTooltip(snapshot, check.PlayerIds);
            }
        }
    }

    private static void DrawConeTooltip(ArenaSnapshot snapshot, ConeResolution cone)
    {
        ImGui.BeginTooltip();
        ImGui.Text("Jugadores");
        if (cone.PlayerIds is null)
        {
            ImGui.TextDisabled("No se capturaron los nombres para este resultado.");
        }
        else if (cone.PlayerIds.Count == 0)
        {
            ImGui.TextDisabled("Ninguno");
        }
        else
        {
            var source = FindPlayer(snapshot, cone.SourceId) ?? InferConeSource(snapshot, cone);
            foreach (var actorId in cone.PlayerIds)
            {
                var target = FindPlayer(snapshot, actorId);
                ImGui.BulletText($"{FormatPlayer(source, cone.SourceId)} > {FormatPlayer(target, actorId)}");
            }
        }
        ImGui.EndTooltip();
    }

    private static string FormatPlayer(ForsakenParticipant? player, ulong actorId) =>
        player is null ? $"Desconocido ({actorId:X})" : $"{player.Name} ({player.Job})";

    private static void DrawPlayerTooltip(ArenaSnapshot snapshot, IReadOnlyList<ulong>? playerIds)
    {
        ImGui.BeginTooltip();
        ImGui.Text("Jugadores");
        if (playerIds is null)
        {
            ImGui.TextDisabled("No se capturaron los nombres para este resultado.");
        }
        else if (playerIds.Count == 0)
        {
            ImGui.TextDisabled("Ninguno");
        }
        else
        {
            foreach (var actorId in playerIds)
            {
                var player = snapshot.Players.FirstOrDefault(candidate => candidate.ActorId == actorId);
                ImGui.BulletText(FormatPlayer(player, actorId));
            }
        }
        ImGui.EndTooltip();
    }
}
