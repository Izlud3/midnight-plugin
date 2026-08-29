using System.Numerics;

namespace MidnightPlugin.Core;

public enum LimitCutRotation
{
    Clockwise,
    CounterClockwise,
}

public sealed record LimitCutBlasterCast(TimeSpan Elapsed, Vector2 Position, double Angle);

public sealed record LimitCutGap(int Number, double Angle, Vector2 Position);

public sealed record LimitCutParticipant(
    ulong ActorId,
    string Name,
    string Job,
    int PartySlot,
    bool Died,
    EncounterPosition? Position);

public sealed record LimitCutPlayerResult(
    ulong ActorId,
    string Name,
    string Job,
    int PartySlot,
    int? Number,
    bool Died,
    EncounterPosition? Position,
    double? Angle,
    double? ExpectedAngle,
    Vector2? ExpectedPosition,
    double? AngleError);

public sealed record LimitCutResult(
    MechanicVerdict Verdict,
    TimeSpan Elapsed,
    IReadOnlyList<string> Reasons,
    double? KefkaStartAngle,
    string? KefkaStartName,
    LimitCutRotation? KefkaRotation,
    LimitCutRotation? PlayerRotation,
    double? PlayerStartAngle,
    IReadOnlyList<LimitCutBlasterCast> RotatingBlasters,
    IReadOnlyList<LimitCutBlasterCast> FinalBlasters,
    IReadOnlyList<Vector2> BlasterSpots,
    IReadOnlyList<LimitCutGap> Gaps,
    IReadOnlyList<LimitCutPlayerResult> Players,
    double WallRadius);

public static class LimitCutAnalyzer
{
    public const uint RotatingBlasterActionId = 47843;
    public const uint FinalBlasterActionId = 47844;
    public const double WarningAngle = 11.25;
    public const double FailureAngle = 22.5;
    public const float ArenaCenter = 100;

    private static readonly IReadOnlyDictionary<uint, int> NumberByMarker = new Dictionary<uint, int>
    {
        [336] = 1,
        [337] = 2,
        [338] = 3,
        [339] = 4,
        [437] = 5,
        [438] = 6,
        [439] = 7,
        [440] = 8,
    };

    private static readonly IReadOnlyDictionary<int, string> SpotNames = new Dictionary<int, string>
    {
        [0] = "N (A)",
        [45] = "NE (2)",
        [90] = "E (B)",
        [135] = "SE (3)",
        [180] = "S (C)",
        [225] = "SW (4)",
        [270] = "W (D)",
        [315] = "NW (1)",
    };

    public static bool TryGetNumber(uint markerId, out int number) => NumberByMarker.TryGetValue(markerId, out number);

    public static LimitCutResult Analyze(
        TimeSpan elapsed,
        IReadOnlyList<LimitCutBlasterCast> rotatingBlasters,
        IReadOnlyList<LimitCutBlasterCast> finalBlasters,
        IReadOnlyList<LimitCutParticipant> participants,
        IReadOnlyDictionary<ulong, int> assignments)
    {
        var wallRadius = Median(rotatingBlasters
            .Concat(finalBlasters)
            .Select(cast => (double)cast.Position.Length())
            .Where(radius => radius > 8));
        if (wallRadius <= 0) wallRadius = 20;

        double? startAngle = null;
        LimitCutRotation? kefkaRotation = null;
        LimitCutRotation? playerRotation = null;
        double? playerStartAngle = null;

        var valid = BuildIndexedAngles(rotatingBlasters);
        if (valid.Count > 0)
        {
            (startAngle, kefkaRotation) = FitStartRotation(valid);
            playerRotation = kefkaRotation == LimitCutRotation.Clockwise
                ? LimitCutRotation.CounterClockwise
                : LimitCutRotation.Clockwise;
            var direction = playerRotation == LimitCutRotation.Clockwise ? 1 : -1;
            playerStartAngle = Normalize(startAngle.Value + 180 + direction * 22.5);
        }

        var blasterSpots = Enumerable.Range(0, 8)
            .Select(index => PositionAtAngle(index * 45, wallRadius))
            .ToArray();
        var gaps = playerStartAngle is { } first && playerRotation is { } rotation
            ? Enumerable.Range(0, 8).Select(index =>
            {
                var direction = rotation == LimitCutRotation.Clockwise ? 1 : -1;
                var angle = Normalize(first + index * 45 * direction);
                return new LimitCutGap(index + 1, angle, PositionAtAngle(angle, wallRadius));
            }).ToArray()
            : [];

        var duplicateNumbers = assignments.Values
            .Where(number => number is >= 1 and <= 8)
            .GroupBy(number => number)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var players = participants.Select(participant =>
        {
            var number = assignments.TryGetValue(participant.ActorId, out var assigned) ? assigned : (int?)null;
            var position = participant.Position;
            var angle = position is { } actual
                ? AngleOf(new(actual.X - ArenaCenter, actual.Z - ArenaCenter))
                : (double?)null;
            var expectedAngle = number is { } value && !duplicateNumbers.Contains(value) &&
                                playerStartAngle is { } start && playerRotation is { } rotation
                ? Normalize(start + (value - 1) * 45 * (rotation == LimitCutRotation.Clockwise ? 1 : -1))
                : (double?)null;
            return new LimitCutPlayerResult(
                participant.ActorId,
                participant.Name,
                participant.Job,
                participant.PartySlot,
                number,
                participant.Died,
                position,
                angle,
                expectedAngle,
                expectedAngle is { } target ? PositionAtAngle(target, wallRadius) : null,
                angle is { } stood && expectedAngle is { } expected
                    ? Math.Abs(AngleDifference(stood, expected))
                    : null);
        }).OrderBy(player => player.Number ?? 99).ThenBy(player => player.PartySlot).ToArray();

        var reasons = new List<string>();
        foreach (var player in players)
        {
            if (player.Died) reasons.Add($"{player.Name} murió durante Limit Cut.");
            if (player.AngleError is >= FailureAngle)
                reasons.Add($"{player.Name} estuvo a {player.AngleError.Value:0.#}° de su posición asignada.");
        }

        var completeAssignments = players.Length == 8 &&
                                  players.All(player => player.Number is >= 1 and <= 8) &&
                                  players.Select(player => player.Number).Distinct().Count() == 8;
        var completeEvidence = startAngle is not null && completeAssignments &&
                               players.All(player => player.Position is not null && player.AngleError is not null);
        var verdict = reasons.Count > 0
            ? MechanicVerdict.Failure
            : completeEvidence
                ? MechanicVerdict.Success
                : MechanicVerdict.InsufficientEvidence;
        if (verdict == MechanicVerdict.InsufficientEvidence)
            reasons.Add("No se capturó evidencia completa de rotación, números o posiciones del grupo.");

        var snappedStart = startAngle is { } angleValue ? (int)Snap45(angleValue) : (int?)null;
        return new(
            verdict,
            elapsed,
            reasons,
            startAngle,
            snappedStart is { } spot ? SpotNames.GetValueOrDefault(spot, $"{spot}°") : null,
            kefkaRotation,
            playerRotation,
            playerStartAngle,
            rotatingBlasters.ToArray(),
            finalBlasters.ToArray(),
            blasterSpots,
            gaps,
            players,
            wallRadius);
    }

    public static double AngleOf(Vector2 position) => Normalize(Math.Atan2(position.X, -position.Y) * 180 / Math.PI);

    public static Vector2 PositionAtAngle(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180;
        return new((float)(radius * Math.Sin(radians)), (float)(-radius * Math.Cos(radians)));
    }

    public static double Normalize(double angle) => ((angle % 360) + 360) % 360;

    public static double AngleDifference(double first, double second)
    {
        var difference = Normalize(first - second);
        return difference > 180 ? difference - 360 : difference;
    }

    private static IReadOnlyList<IndexedAngle> BuildIndexedAngles(IReadOnlyList<LimitCutBlasterCast> casts)
    {
        if (casts.Count == 0) return [];
        var ordered = casts.OrderBy(cast => cast.Elapsed).ToArray();
        var differences = ordered.Skip(1)
            .Select((cast, index) => (cast.Elapsed - ordered[index].Elapsed).TotalMilliseconds)
            .Where(value => value > 50)
            .ToArray();
        var step = differences.Length > 0 ? Median(differences) : 2000;
        var start = ordered[0].Elapsed;
        return ordered.Select(cast => new IndexedAngle(
                step > 0 ? (int)Math.Round((cast.Elapsed - start).TotalMilliseconds / step) : 0,
                cast.Angle,
                cast.Position.Length()))
            .Where(cast => cast.Radius > 8)
            .ToArray();
    }

    private static (double StartAngle, LimitCutRotation Rotation) FitStartRotation(IReadOnlyList<IndexedAngle> valid)
    {
        Fit? best = null;
        foreach (var direction in new[] { 1, -1 })
        {
            var mode = valid
                .GroupBy(entry => Snap45(Normalize(entry.Angle - entry.Index * 45 * direction)))
                .Select(group => new { Start = group.Key, Count = group.Count() })
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.Start)
                .First();
            if (best is null || mode.Count > best.Agreement)
                best = new(direction, mode.Start, mode.Count);
        }

        return (best!.StartAngle,
            best.Direction == 1 ? LimitCutRotation.Clockwise : LimitCutRotation.CounterClockwise);
    }

    private static double Snap45(double angle) => Normalize(Math.Round(angle / 45) * 45);

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }

    private sealed record IndexedAngle(int Index, double Angle, float Radius);
    private sealed record Fit(int Direction, double StartAngle, int Agreement);
}
