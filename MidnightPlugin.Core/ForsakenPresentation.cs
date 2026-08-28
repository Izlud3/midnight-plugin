using System.Numerics;

namespace MidnightPlugin.Core;

[Flags]
public enum ForsakenPlayerEvidence
{
    None = 0,
    Tower = 1 << 0,
    Stack = 1 << 1,
    ConeSource = 1 << 2,
    ConeHit = 1 << 3,
    FailedCheck = 1 << 4,
    Died = 1 << 5,
}

public readonly record struct ForsakenHpBarSegments(
    bool IsAvailable,
    bool ShieldAvailable,
    float HpRatio,
    float ShieldRatio,
    float OverflowShieldRatio,
    ulong? EffectiveHp);

public static class ForsakenPresentation
{
    public const float ArenaCenter = 100;
    public const float TowerRingRadius = 8;

    public static IReadOnlyList<Vector2> TowerCandidateOffsets { get; } = Enumerable.Range(0, 8)
        .Select(index =>
        {
            var radians = index * MathF.PI / 4;
            return new Vector2(TowerRingRadius * MathF.Sin(radians), -TowerRingRadius * MathF.Cos(radians));
        })
        .ToArray();

    public static IReadOnlyList<T> PairStartSnapshot<T>(
        int capturedTowerCount,
        IReadOnlyList<T> currentParty,
        IReadOnlyList<T> existingSnapshot) =>
        capturedTowerCount == 0 ? currentParty.ToArray() : existingSnapshot;

    public static uint? ShieldHpFromPercentage(uint maxHp, byte? shieldPercentage)
    {
        if (maxHp == 0 || shieldPercentage is null) return null;
        var percentage = Math.Clamp((double)shieldPercentage.Value, 0, 100);
        return (uint)Math.Round(maxHp * percentage / 100, MidpointRounding.AwayFromZero);
    }

    public static float NormalizedTowerRotation(IReadOnlyList<ArenaTower> towers)
    {
        if (towers.Count == 0) return 0;
        var midpoint = new Vector2(
            towers.Average(tower => tower.X) - ArenaCenter,
            towers.Average(tower => tower.Y) - ArenaCenter);
        if (midpoint.LengthSquared() < .0001f) return 0;
        return MathF.PI / 2 - MathF.Atan2(midpoint.Y, midpoint.X);
    }

    public static Vector2 Rotate(Vector2 point, float radians)
    {
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new(point.X * cos - point.Y * sin, point.X * sin + point.Y * cos);
    }

    public static ForsakenHpBarSegments HpBarSegments(uint? currentHp, uint? shieldHp, uint? maxHp)
    {
        if (currentHp is null || maxHp is null || maxHp == 0)
            return new(false, shieldHp is not null, 0, 0, 0, null);

        var hpRatio = Math.Clamp((double)currentHp.Value / maxHp.Value, 0, 1);
        if (shieldHp is null)
            return new(true, false, (float)hpRatio, 0, 0, null);

        var rawShieldRatio = Math.Max(0, (double)shieldHp.Value / maxHp.Value);
        var shieldRatio = Math.Min(rawShieldRatio, 1 - hpRatio);
        var overflowRatio = Math.Clamp(rawShieldRatio - shieldRatio, 0, 1);
        return new(
            true,
            true,
            (float)hpRatio,
            (float)shieldRatio,
            (float)overflowRatio,
            (ulong)currentHp.Value + shieldHp.Value);
    }

    public static long? EffectiveHpDelta(ForsakenParticipant player)
    {
        var before = HpBarSegments(player.HpAtPairStart, player.ShieldHpAtPairStart, player.MaxHp).EffectiveHp;
        var after = HpBarSegments(player.HpAtResolution, player.ShieldHpAtResolution, player.MaxHp).EffectiveHp;
        return before is not null && after is not null ? (long)after.Value - (long)before.Value : null;
    }

    public static IReadOnlyList<ForsakenParticipant> OrderedPlayers(ArenaSnapshot snapshot) =>
        snapshot.Players.OrderBy(player => player.PartySlot).ThenBy(player => player.Name, StringComparer.Ordinal).ToArray();

    public static ForsakenPlayerEvidence EvidenceFor(ArenaSnapshot snapshot, ulong actorId)
    {
        var evidence = snapshot.Players.FirstOrDefault(player => player.ActorId == actorId)?.Died == true
            ? ForsakenPlayerEvidence.Died
            : ForsakenPlayerEvidence.None;

        foreach (var tower in snapshot.Towers.Where(tower => tower.SoakerIds?.Contains(actorId) == true))
        {
            evidence |= ForsakenPlayerEvidence.Tower;
            if (!tower.IsResolvedCorrectly) evidence |= ForsakenPlayerEvidence.FailedCheck;
        }

        foreach (var stack in snapshot.Stacks.Where(stack => stack.SourceId == actorId || stack.PlayerIds?.Contains(actorId) == true))
        {
            evidence |= ForsakenPlayerEvidence.Stack;
            if (!stack.IsResolvedCorrectly) evidence |= ForsakenPlayerEvidence.FailedCheck;
        }

        foreach (var cone in snapshot.Cones)
        {
            if (cone.SourceId == actorId) evidence |= ForsakenPlayerEvidence.ConeSource;
            if (cone.PlayerIds?.Contains(actorId) == true) evidence |= ForsakenPlayerEvidence.ConeHit;
            if (!cone.IsResolvedCorrectly &&
                (cone.SourceId == actorId || cone.PlayerIds?.Contains(actorId) == true))
                evidence |= ForsakenPlayerEvidence.FailedCheck;
        }

        return evidence;
    }
}
