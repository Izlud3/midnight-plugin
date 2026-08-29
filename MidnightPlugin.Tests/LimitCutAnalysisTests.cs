using System.Numerics;
using MidnightPlugin.Core;
using Xunit;

namespace MidnightPlugin.Tests;

public sealed class LimitCutAnalysisTests
{
    [Theory]
    [InlineData(336u, 1)]
    [InlineData(337u, 2)]
    [InlineData(338u, 3)]
    [InlineData(339u, 4)]
    [InlineData(437u, 5)]
    [InlineData(438u, 6)]
    [InlineData(439u, 7)]
    [InlineData(440u, 8)]
    public void NumberMarkersMapToAssignments(uint markerId, int expected)
    {
        Assert.True(LimitCutAnalyzer.TryGetNumber(markerId, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData(0, 0, -20)]
    [InlineData(90, 20, 0)]
    [InlineData(180, 0, 20)]
    [InlineData(270, -20, 0)]
    public void AnglesMatchArenaCompass(double angle, float expectedX, float expectedY)
    {
        var position = LimitCutAnalyzer.PositionAtAngle(angle, 20);
        Assert.InRange(position.X, expectedX - .001f, expectedX + .001f);
        Assert.InRange(position.Y, expectedY - .001f, expectedY + .001f);
        Assert.InRange(LimitCutAnalyzer.AngleOf(position), angle - .001, angle + .001);
    }

    [Fact]
    public void InfersClockwiseKefkaAndOppositePlayerRotationWithSkippedCast()
    {
        var result = AnalyzeWithRotation(
            (0, 0),
            (2, 45),
            (4, 90),
            (8, 180));

        Assert.Equal(LimitCutRotation.Clockwise, result.KefkaRotation);
        Assert.Equal(LimitCutRotation.CounterClockwise, result.PlayerRotation);
        Assert.Equal(0, result.KefkaStartAngle);
        Assert.Equal(157.5, result.PlayerStartAngle);
        Assert.Equal("N (A)", result.KefkaStartName);
    }

    [Fact]
    public void InfersCounterClockwiseRotationAndIgnoresInnerCast()
    {
        var casts = Casts((0, 90), (2, 45), (4, 0), (6, 315)).ToList();
        casts.Add(new(TimeSpan.FromSeconds(3), LimitCutAnalyzer.PositionAtAngle(200, 2), 200));
        var result = LimitCutAnalyzer.Analyze(TimeSpan.FromSeconds(20), casts, [], [], new Dictionary<ulong, int>());

        Assert.Equal(LimitCutRotation.CounterClockwise, result.KefkaRotation);
        Assert.Equal(LimitCutRotation.Clockwise, result.PlayerRotation);
        Assert.Equal(90, result.KefkaStartAngle);
        Assert.InRange(result.WallRadius, 19.99, 20.01);
    }

    [Fact]
    public void CompletePlayersInExpectedGapsSucceed()
    {
        var result = CompleteResult();

        Assert.Equal(MechanicVerdict.Success, result.Verdict);
        Assert.Equal(8, result.Gaps.Count);
        Assert.All(result.Players, player => Assert.InRange(player.AngleError!.Value, 0, .001));
    }

    [Fact]
    public void CapturedWaymarksArePreservedInTheReviewResult()
    {
        var waymarks = new[]
        {
            new LimitCutWaymark("A", new Vector2(100, 90)),
            new LimitCutWaymark("4", new Vector2(92, 108)),
        };

        var result = LimitCutAnalyzer.Analyze(
            TimeSpan.Zero,
            Casts((0, 0), (2, 45)),
            [],
            [],
            new Dictionary<ulong, int>(),
            waymarks);

        Assert.Equal(waymarks, result.Waymarks);
    }

    [Theory]
    [InlineData(11.25, MechanicVerdict.Success)]
    [InlineData(22.5, MechanicVerdict.Failure)]
    public void PlacementThresholdsMatchReviewPolicy(double offset, MechanicVerdict expectedVerdict)
    {
        var result = CompleteResult(firstPlayerOffset: offset);

        Assert.Equal(expectedVerdict, result.Verdict);
        Assert.InRange(result.Players.Single(player => player.Number == 1).AngleError!.Value, offset - .001, offset + .001);
    }

    [Fact]
    public void DirectDeathFailsEvenWhenAnAssignmentIsMissing()
    {
        var result = CompleteResult(deadActor: 1, omitAssignment: 8);

        Assert.Equal(MechanicVerdict.Failure, result.Verdict);
        Assert.Contains(result.Reasons, reason => reason.Contains("murió"));
    }

    [Fact]
    public void MissingOrDuplicateAssignmentsRemainUnverifiedRatherThanFailing()
    {
        var missing = CompleteResult(omitAssignment: 8);
        var duplicate = CompleteResult(duplicateLastAssignment: true);

        Assert.Equal(MechanicVerdict.InsufficientEvidence, missing.Verdict);
        Assert.Equal(MechanicVerdict.InsufficientEvidence, duplicate.Verdict);
        Assert.Null(missing.Players.Single(player => player.ActorId == 8).AngleError);
    }

    [Fact]
    public void PullWithOnlyLimitCutEvidenceIsReviewable()
    {
        var now = TimeSpan.Zero;
        var sessions = new EncounterSessionService(() => now);
        sessions.SetTerritory(EncounterSessionService.DancingMadTerritoryId);
        var pull = sessions.StartPull(DateTimeOffset.UtcNow)!;

        Assert.True(sessions.TryRecordLimitCutResult(CompleteResult()));
        Assert.Same(pull, sessions.LatestReviewablePull());
        Assert.Same(pull, sessions.FindReviewablePull(pull.Id));
        Assert.False(sessions.TryRecordLimitCutResult(CompleteResult()));
    }

    [Fact]
    public void PullCanRetainForsakenAndLimitCutEvidenceTogether()
    {
        var now = TimeSpan.Zero;
        var sessions = new EncounterSessionService(() => now);
        sessions.SetTerritory(EncounterSessionService.DancingMadTerritoryId);
        var pull = sessions.StartPull(DateTimeOffset.UtcNow)!;

        Assert.True(sessions.TryRecordForsakenResult(
            new ForsakenPairResult(1, MechanicVerdict.Success, TimeSpan.Zero, [], [], null)));
        Assert.True(sessions.TryRecordLimitCutResult(CompleteResult()));

        Assert.Single(pull.ForsakenResults);
        Assert.NotNull(pull.LimitCutResult);
        Assert.Same(pull, sessions.LatestReviewablePull());
    }

    [Fact]
    public void LimitCutOnlyPullsHonorReviewHistoryCapacity()
    {
        var now = TimeSpan.Zero;
        var sessions = new EncounterSessionService(() => now, historyCapacity: 2);
        sessions.SetTerritory(EncounterSessionService.DancingMadTerritoryId);
        var pulls = new List<PullSession>();
        for (var index = 0; index < 3; index++)
        {
            var pull = sessions.StartPull(DateTimeOffset.UtcNow)!;
            pulls.Add(pull);
            Assert.True(sessions.TryRecordLimitCutResult(CompleteResult()));
            now += TimeSpan.FromSeconds(1);
            sessions.EndPull(PullState.Wiped, DateTimeOffset.UtcNow);
        }

        Assert.Null(sessions.FindReviewablePull(pulls[0].Id));
        Assert.Same(pulls[1], sessions.FindReviewablePull(pulls[1].Id));
        Assert.Same(pulls[2], sessions.LatestReviewablePull());
    }

    private static LimitCutResult AnalyzeWithRotation(params (double Seconds, double Angle)[] casts) =>
        LimitCutAnalyzer.Analyze(TimeSpan.FromSeconds(20), Casts(casts), [], [], new Dictionary<ulong, int>());

    private static IReadOnlyList<LimitCutBlasterCast> Casts(params (double Seconds, double Angle)[] casts) =>
        casts.Select(value => new LimitCutBlasterCast(
            TimeSpan.FromSeconds(value.Seconds),
            LimitCutAnalyzer.PositionAtAngle(value.Angle, 20),
            value.Angle)).ToArray();

    private static LimitCutResult CompleteResult(
        double firstPlayerOffset = 0,
        ulong? deadActor = null,
        int? omitAssignment = null,
        bool duplicateLastAssignment = false)
    {
        var casts = Casts((0, 0), (2, 45), (4, 90), (6, 135));
        const double playerStart = 157.5;
        var participants = Enumerable.Range(1, 8).Select(number =>
        {
            var angle = LimitCutAnalyzer.Normalize(playerStart - (number - 1) * 45 + (number == 1 ? firstPlayerOffset : 0));
            var offset = LimitCutAnalyzer.PositionAtAngle(angle, 20);
            return new LimitCutParticipant(
                (ulong)number,
                $"P{number}",
                number <= 2 ? "PLD" : "SAM",
                number - 1,
                deadActor == (ulong)number,
                new(offset.X + LimitCutAnalyzer.ArenaCenter, 0, offset.Y + LimitCutAnalyzer.ArenaCenter, 0));
        }).ToArray();
        var assignments = Enumerable.Range(1, 8)
            .Where(number => number != omitAssignment)
            .ToDictionary(number => (ulong)number, number => number);
        if (duplicateLastAssignment) assignments[8] = 7;
        return LimitCutAnalyzer.Analyze(TimeSpan.FromSeconds(20), casts, [], participants, assignments);
    }
}
