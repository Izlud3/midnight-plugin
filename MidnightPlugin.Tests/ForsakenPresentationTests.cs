using System.Numerics;
using MidnightPlugin.Core;
using Xunit;

namespace MidnightPlugin.Tests;

public sealed class ForsakenPresentationTests
{
    [Fact]
    public void FirstTowerOfEveryPairRefreshesThePairStartSnapshot()
    {
        IReadOnlyList<int> existing = [10];

        var firstPair = ForsakenPresentation.PairStartSnapshot(0, [20], existing);
        var duplicateEvidence = ForsakenPresentation.PairStartSnapshot(1, [30], firstPair);
        var nextPair = ForsakenPresentation.PairStartSnapshot(0, [40], duplicateEvidence);

        Assert.Equal([20], firstPair);
        Assert.Same(firstPair, duplicateEvidence);
        Assert.Equal([40], nextPair);
    }

    [Theory]
    [InlineData(200_001u, (byte)20, 40_000u)]
    [InlineData(101u, (byte)50, 51u)]
    [InlineData(100u, (byte)0, 0u)]
    public void ShieldHpUsesRoundedGamePercentage(uint maxHp, byte percentage, uint expected)
    {
        Assert.Equal(expected, ForsakenPresentation.ShieldHpFromPercentage(maxHp, percentage));
    }

    [Fact]
    public void ShieldHpIsUnavailableWithoutActorDataOrMaxHp()
    {
        Assert.Null(ForsakenPresentation.ShieldHpFromPercentage(100, null));
        Assert.Null(ForsakenPresentation.ShieldHpFromPercentage(0, 20));
    }

    [Fact]
    public void TowerCandidatesFormEightRadiusEightSlots()
    {
        Assert.Equal(8, ForsakenPresentation.TowerCandidateOffsets.Count);
        Assert.All(ForsakenPresentation.TowerCandidateOffsets,
            point => Assert.InRange(point.Length(), 7.999f, 8.001f));
        Assert.Equal(8, ForsakenPresentation.TowerCandidateOffsets.Distinct().Count());
    }

    [Fact]
    public void NormalizationPlacesTowerPairMidpointSouth()
    {
        var towers = new[] { new ArenaTower(94.343f, 94.343f, 2), new ArenaTower(105.657f, 94.343f, 2) };

        var rotation = ForsakenPresentation.NormalizedTowerRotation(towers);
        var midpoint = ForsakenPresentation.Rotate(new Vector2(0, -5.657f), rotation);

        Assert.InRange(Math.Abs(midpoint.X), 0, .001f);
        Assert.True(midpoint.Y > 0);
        Assert.Equal(new Vector2(3, -4), ForsakenPresentation.Rotate(new Vector2(3, -4), 0));
    }

    [Theory]
    [InlineData(60u, 20u, 100u, .6f, .2f, 0f)]
    [InlineData(100u, 20u, 100u, 1f, 0f, .2f)]
    [InlineData(20u, 100u, 100u, .2f, .8f, .2f)]
    [InlineData(0u, 0u, 100u, 0f, 0f, 0f)]
    public void HpBarGeometrySeparatesShieldAndOverflow(
        uint hp, uint shield, uint maxHp, float hpRatio, float shieldRatio, float overflowRatio)
    {
        var result = ForsakenPresentation.HpBarSegments(hp, shield, maxHp);

        Assert.True(result.IsAvailable);
        Assert.True(result.ShieldAvailable);
        Assert.Equal(hpRatio, result.HpRatio, 3);
        Assert.Equal(shieldRatio, result.ShieldRatio, 3);
        Assert.Equal(overflowRatio, result.OverflowShieldRatio, 3);
        Assert.Equal((ulong)hp + shield, result.EffectiveHp);
    }

    [Fact]
    public void MissingShieldIsNotTreatedAsZero()
    {
        var result = ForsakenPresentation.HpBarSegments(80, null, 100);

        Assert.True(result.IsAvailable);
        Assert.False(result.ShieldAvailable);
        Assert.Null(result.EffectiveHp);
    }

    [Fact]
    public void EffectiveDeltaUsesHpAndShieldTogether()
    {
        var player = Player(1, 0) with
        {
            HpAtPairStart = 100,
            ShieldHpAtPairStart = 30,
            HpAtResolution = 40,
            ShieldHpAtResolution = 0,
            MaxHp = 100,
        };

        Assert.Equal(-90, ForsakenPresentation.EffectiveHpDelta(player));
    }

    [Fact]
    public void EvidenceOnlyFlagsCapturedParticipants()
    {
        var players = new[] { Player(1, 0), Player(2, 1), Player(3, 2) with { Died = true }, Player(4, 3) };
        var snapshot = new ArenaSnapshot(
            TimeSpan.Zero,
            players,
            [new(92, 100, 1, [1])],
            [new(2, [1, 2], 2)],
            [new(2, [1, 3], 4)]);

        var first = ForsakenPresentation.EvidenceFor(snapshot, 1);
        Assert.True(first.HasFlag(ForsakenPlayerEvidence.Tower));
        Assert.True(first.HasFlag(ForsakenPlayerEvidence.Stack));
        Assert.True(first.HasFlag(ForsakenPlayerEvidence.ConeHit));
        Assert.True(first.HasFlag(ForsakenPlayerEvidence.FailedCheck));
        Assert.True(ForsakenPresentation.EvidenceFor(snapshot, 3).HasFlag(ForsakenPlayerEvidence.Died));
        Assert.False(ForsakenPresentation.EvidenceFor(snapshot, 99).HasFlag(ForsakenPlayerEvidence.FailedCheck));
    }

    [Fact]
    public void PlayersArePresentedInStablePartyOrder()
    {
        var snapshot = new ArenaSnapshot(TimeSpan.Zero, [Player(3, 2), Player(1, 0), Player(2, 1)], [], [], []);

        Assert.Equal([1UL, 2UL, 3UL], ForsakenPresentation.OrderedPlayers(snapshot).Select(player => player.ActorId));
    }

    private static ForsakenParticipant Player(ulong id, int slot) =>
        new(id, $"P{id}", "PLD", false, false, new(100, 0, 100, 0), PartySlot: slot);
}
