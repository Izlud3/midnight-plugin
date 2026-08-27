using MidnightPlugin.Core;
using Xunit;

namespace MidnightPlugin.Tests;

public sealed class EncounterFeatureTests
{
    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, true, true, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, false, false)]
    public void PullStartPolicySupportsVerifiedEncounterFallback(
        bool dutyStarted,
        bool inCombat,
        bool playback,
        bool encounterSignal,
        bool expected)
    {
        Assert.Equal(expected, EncounterPullStartPolicy.ShouldStart(
            isDancingMad: true,
            dutyStarted,
            inCombat,
            playback,
            encounterSignal));
    }

    [Fact]
    public void PullStartPolicyRemainsTerritoryGated()
    {
        Assert.False(EncounterPullStartPolicy.ShouldStart(
            isDancingMad: false,
            isDutyStarted: true,
            isInCombat: true,
            isDutyRecorderPlayback: true,
            hasEncounterSignal: true));
    }

    [Fact]
    public void SessionIsTerritoryGatedAndUsesBoundedHistory()
    {
        var now = TimeSpan.Zero;
        var service = new EncounterSessionService(() => now, historyCapacity: 2);

        Assert.Null(service.StartPull(DateTimeOffset.UtcNow));
        service.SetTerritory(EncounterSessionService.DancingMadTerritoryId);
        for (var pull = 0; pull < 3; pull++)
        {
            service.StartPull(DateTimeOffset.UtcNow);
            now += TimeSpan.FromSeconds(1);
            service.EndPull(PullState.Wiped, DateTimeOffset.UtcNow);
        }

        Assert.Equal(2, service.HistorySnapshot().Count);
    }

    [Fact]
    public void LeavingTerritoryAbandonsActivePull()
    {
        var service = new EncounterSessionService(() => TimeSpan.Zero);
        service.SetTerritory(EncounterSessionService.DancingMadTerritoryId);
        service.StartPull(DateTimeOffset.UtcNow);

        service.SetTerritory(1);

        Assert.Null(service.ActivePull);
        Assert.Equal(PullState.Abandoned, Assert.Single(service.HistorySnapshot()).State);
    }

    [Fact]
    public void RecognizedEncounterCanStartInReplayTerritoryAndResetsOnZoneChange()
    {
        var service = new EncounterSessionService(() => TimeSpan.Zero);
        service.SetTerritory(751);

        Assert.False(service.IsDancingMad);
        Assert.True(service.RecognizeDancingMad());
        Assert.True(service.IsDancingMad);
        Assert.NotNull(service.StartPull(DateTimeOffset.UtcNow));

        service.SetTerritory(1);

        Assert.False(service.IsDancingMad);
        Assert.Null(service.ActivePull);
        Assert.Equal(PullState.Abandoned, Assert.Single(service.HistorySnapshot()).State);
    }

    [Fact]
    public void ForsakenFailureIncludesReasonsAndSnapshot()
    {
        var participants = Enumerable.Range(0, 8)
            .Select(index => new ForsakenParticipant((ulong)index + 1, $"P{index}", "PLD", index < 4, index == 1, new(index, index, 0, 0)))
            .ToArray();

        var result = ForsakenAnalyzer.Analyze(1, TimeSpan.FromSeconds(240), participants,
            [new(90, 100, 3), new(110, 100, 1)], [new(2)], true);

        Assert.Equal(MechanicVerdict.Failure, result.Verdict);
        Assert.Contains(result.Reasons, reason => reason.Contains("3 soakers"));
        Assert.Contains(result.Reasons, reason => reason.Contains("Stack 1 tuvo 2 jugadores"));
        Assert.Contains(result.Reasons, reason => reason.Contains("murió"));
        Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public void ForsakenCanSucceedWithoutRoleAssignments()
    {
        var participants = Enumerable.Range(0, 8)
            .Select(index => new ForsakenParticipant((ulong)index + 1, $"P{index}", "PLD", index < 4, false, new(index, index, 0, 0)))
            .ToArray();

        var result = ForsakenAnalyzer.Analyze(1, TimeSpan.FromSeconds(240), participants,
            [new(90, 100, 2), new(110, 100, 2)], [new(3)], true);

        Assert.Equal(MechanicVerdict.Success, result.Verdict);
        Assert.Empty(result.Reasons);
        Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public void ForsakenSnapshotPreservesPlayersAssignedToEachCheck()
    {
        var participants = Enumerable.Range(0, 8)
            .Select(index => new ForsakenParticipant((ulong)index + 1, $"P{index}", "PLD", index < 4, false, new(index, index, 0, 0)))
            .ToArray();

        var result = ForsakenAnalyzer.Analyze(1, TimeSpan.Zero, participants,
            [new(90, 100, 2, [1, 2]), new(110, 100, 2, [3, 4])],
            [new(3, [5, 6, 7], 5)], true,
            [new(1, [2], 1)]);

        Assert.Equal([1UL, 2UL], result.Snapshot!.Towers[0].SoakerIds);
        Assert.Equal([5UL, 6UL, 7UL], result.Snapshot.Stacks[0].PlayerIds);
        Assert.Equal(5UL, result.Snapshot.Stacks[0].SourceId);
        Assert.Equal(1UL, result.Snapshot.Cones[0].SourceId);
    }

    [Fact]
    public void RestartingForsakenClearsResultsFromTheActivePull()
    {
        var sessions = new EncounterSessionService(() => TimeSpan.Zero);
        sessions.SetTerritory(EncounterSessionService.DancingMadTerritoryId);
        var pull = sessions.StartPull(DateTimeOffset.UtcNow)!;
        var result = ForsakenAnalyzer.Analyze(1, TimeSpan.Zero, [], [], [], false);
        Assert.True(sessions.TryRecordForsakenResult(result));
        Assert.Single(pull.ForsakenResults);

        Assert.True(sessions.ClearActiveForsakenResults());

        Assert.Empty(pull.ForsakenResults);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    public void ForsakenTowerResolutionRequiresExactlyTwoSoakers(int soakers, bool expected)
    {
        Assert.Equal(expected, new ArenaTower(100, 100, soakers).IsResolvedCorrectly);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    public void ForsakenStackResolutionRequiresExactlyThreePlayers(int players, bool expected)
    {
        Assert.Equal(expected, new StackResolution(players).IsResolvedCorrectly);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public void ForsakenConeResolutionRequiresExactlyOneOtherPlayer(int players, bool expected)
    {
        Assert.Equal(expected, new ConeResolution(players).IsResolvedCorrectly);
    }

    [Fact]
    public void ForsakenConeClipMakesThePairFail()
    {
        var participants = Enumerable.Range(0, 8)
            .Select(index => new ForsakenParticipant((ulong)index + 1, $"P{index}", "PLD", index < 4, false, new(index, index, 0, 0)))
            .ToArray();

        var result = ForsakenAnalyzer.Analyze(1, TimeSpan.Zero, participants,
            [new(90, 100, 2), new(110, 100, 2)], [], true,
            [new(2, [5, 6])]);

        Assert.Equal(MechanicVerdict.Failure, result.Verdict);
        Assert.Contains(result.Reasons, reason => reason.Contains("Cono 1 alcanzó a 2 otros jugadores"));
    }

    [Fact]
    public void IncompleteForsakenEvidenceNeverBecomesFailure()
    {
        var result = ForsakenAnalyzer.Analyze(1, TimeSpan.Zero, [], [], [], false);

        Assert.Equal(MechanicVerdict.InsufficientEvidence, result.Verdict);
        Assert.Null(result.Snapshot);
    }

}
