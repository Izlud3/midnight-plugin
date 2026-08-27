using MidnightPlugin.Core;
using System.Text.Json;
using Xunit;

namespace MidnightPlugin.Tests;

public sealed class PracticeSessionTests
{
    [Fact]
    public void ReviewedPldFixtureLoadsWithExpectedActionCounts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "pld-dancing-mad-v1.json");
        var result = PracticeReferenceCatalog.Load(File.ReadAllText(path));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("PLD", result.Rotation!.Job);
        Assert.Equal("DMU PLD Top 1", result.Rotation.ProvenancePlayer);
        Assert.Equal(496, result.Rotation.Actions.Count);
        Assert.Equal(TimeSpan.Zero, result.Rotation.Actions[0].Offset);
        Assert.Equal(351, result.Rotation.Actions.Count(action => action.TimingClass == ActionTimingClass.Gcd));
        Assert.Equal(145, result.Rotation.Actions.Count(action => action.TimingClass == ActionTimingClass.Ogcd));
        Assert.DoesNotContain(result.Rotation.Actions, action => PracticeActionIgnoreList.Contains(action.ActionId, action.ActionName));
    }

    [Theory]
    [InlineData(7533u, "Provoke")]
    [InlineData(7382u, "Intervention")]
    [InlineData(25746u, "Holy Sheltron")]
    [InlineData(3540u, "Divine Veil")]
    [InlineData(36920u, "Guardian")]
    [InlineData(7385u, "Passage of Arms")]
    [InlineData(7531u, "Rampart")]
    [InlineData(7535u, "Reprisal")]
    [InlineData(7537u, "Shirk")]
    [InlineData(22u, "Bulwark")]
    [InlineData(30u, "Hallowed Ground")]
    public void IgnoredActionsAreExcludedFromPractice(uint actionId, string actionName)
    {
        Assert.True(PracticeActionIgnoreList.Contains(actionId, actionName));
    }

    [Fact]
    public void IgnoredPlayerActionDoesNotAdvanceOrCreateAnAttempt()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));
        service.Start();
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.False(service.TryRecordAction(7533, "Provoke", ActionTimingClass.Ogcd));
        var snapshot = service.Snapshot();
        Assert.Equal(0, snapshot.ResolvedCount);
        Assert.Empty(snapshot.Attempts);
    }

    [Fact]
    public void ReferenceCatalogNormalizesTheFirstActionToZero()
    {
        var result = PracticeReferenceCatalog.Load("""
        {
          "schemaVersion": 1,
          "encounterKey": "dancing-mad",
          "territoryId": 1331,
          "job": "PLD",
          "patch": "7.5",
          "version": "test",
          "provenance": { "player": "Test Player", "rank": 1, "source": "test" },
          "actions": [
            { "actionId": 100, "actionName": "Fast Blade", "timingClass": "gcd", "timeMs": 892 },
            { "actionId": 200, "actionName": "Fight or Flight", "timingClass": "ogcd", "timeMs": 1606 }
          ]
        }
        """);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(2, result.Rotation!.Actions.Count);
        Assert.Equal(TimeSpan.Zero, result.Rotation.Actions[0].Offset);
        Assert.Equal(TimeSpan.FromMilliseconds(714), result.Rotation.Actions[1].Offset);
        Assert.Equal("Test Player", result.Rotation.ProvenancePlayer);
    }

    [Theory]
    [InlineData("PLD", "gcd", 100)]
    [InlineData("pld", "OGCD", 100)]
    [InlineData("VPR", "gcd", 100)]
    public void ReferenceCatalogAcceptsSupportedJobAndTimingClass(string job, string timingClass, int timeMs)
    {
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            territoryId = 1331,
            job,
            actions = new[]
            {
                new { actionId = 100, actionName = "Action", timingClass, timeMs },
            },
        });
        var result = PracticeReferenceCatalog.Load(json);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(job.ToUpperInvariant(), result.Rotation!.Job);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PALADIN JOB")]
    [InlineData("TOO-LONG-JOB")]
    public void ReferenceCatalogRejectsInvalidJobIdentifiers(string job)
    {
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            job,
            actions = new[] { new { actionId = 100, actionName = "Action", timingClass = "gcd", timeMs = 0 } },
        });

        var result = PracticeReferenceCatalog.Load(json);

        Assert.False(result.IsValid);
        Assert.Contains("job", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReferenceCatalogRejectsInvalidOrderingAndTiming()
    {
        var unordered = PracticeReferenceCatalog.Load("""
        {
          "schemaVersion": 1,
          "job": "PLD",
          "actions": [
            { "actionId": 100, "actionName": "First", "timingClass": "gcd", "timeMs": 200 },
            { "actionId": 200, "actionName": "Second", "timingClass": "gcd", "timeMs": 100 }
          ]
        }
        """);
        var unknownTiming = PracticeReferenceCatalog.Load("""
        {
          "schemaVersion": 1,
          "job": "PLD",
          "actions": [
            { "actionId": 100, "actionName": "Action", "timingClass": "instant", "timeMs": 0 }
          ]
        }
        """);

        Assert.False(unordered.IsValid);
        Assert.Contains("ordered", unordered.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(unknownTiming.IsValid);
        Assert.Contains("timing class", unknownTiming.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartUsesAThreeSecondCountdownAndThenRuns()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));

        service.Start();
        Assert.Equal(PracticeState.Countdown, service.Snapshot().State);

        clock.Advance(TimeSpan.FromMilliseconds(2_999));
        Assert.Equal(PracticeState.Countdown, service.Snapshot().State);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var snapshot = service.Snapshot();
        Assert.Equal(PracticeState.Running, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.Elapsed);
    }

    [Fact]
    public void StartAtOffsetSkipsEarlierActionsAndBeginsAtSelectedTime()
    {
        var clock = new FakeClock();
        var service = CreateService(
            clock,
            new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero),
            new PracticeReferenceAction(200, "Riot Blade", ActionTimingClass.Gcd, TimeSpan.FromSeconds(10)),
            new PracticeReferenceAction(300, "Royal Authority", ActionTimingClass.Gcd, TimeSpan.FromSeconds(20)));

        service.Start(startOffset: TimeSpan.FromSeconds(10));
        var countdown = service.Snapshot();
        Assert.Equal(PracticeState.Countdown, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(10), countdown.Elapsed);
        Assert.Equal(1, countdown.NextReferenceIndex);
        Assert.Equal(2, countdown.TotalCount);

        clock.Advance(TimeSpan.FromSeconds(3.2));
        Assert.True(service.TryRecordAction(200, "Riot Blade", ActionTimingClass.Gcd));
        var running = service.Snapshot();
        Assert.Equal(PracticeState.Running, running.State);
        Assert.Equal(TimeSpan.FromSeconds(10), running.Elapsed);
        Assert.Equal(1, running.HitCount);
        Assert.Equal(2, running.NextReferenceIndex);
        Assert.DoesNotContain(running.ExpectedResults, result => result.Kind == PracticeMatchKind.Missed);
    }

    [Fact]
    public void StartRejectsNegativeOffsetAndClampsPastEnd()
    {
        var clock = new FakeClock();
        var service = CreateService(
            clock,
            new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero),
            new PracticeReferenceAction(200, "Riot Blade", ActionTimingClass.Gcd, TimeSpan.FromSeconds(10)));

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Start(startOffset: TimeSpan.FromSeconds(-1)));

        service.Start(startOffset: TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(10), service.Snapshot().Elapsed);
        Assert.Equal(1, service.Snapshot().NextReferenceIndex);
    }

    [Fact]
    public void PauseAndResumeFreezesTheRunningClockAndKeepsResults()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));
        service.Start();
        clock.Advance(TimeSpan.FromSeconds(3.450));

        Assert.True(service.Pause());
        var paused = service.Snapshot();
        Assert.Equal(PracticeState.Paused, paused.State);
        Assert.Equal(TimeSpan.FromMilliseconds(250), paused.Elapsed);

        clock.Advance(TimeSpan.FromSeconds(10));
        var stillPaused = service.Snapshot();
        Assert.Equal(PracticeState.Paused, stillPaused.State);
        Assert.Equal(paused.Elapsed, stillPaused.Elapsed);
        Assert.False(service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd));

        Assert.True(service.Resume());
        Assert.Equal(PracticeState.Running, service.Snapshot().State);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.True(service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd));
        Assert.Equal(1, service.Snapshot().HitCount);
    }

    [Fact]
    public void PauseAndResumeFreezesTheCountdown()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));
        service.Start();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(service.Pause());
        var paused = service.Snapshot();
        Assert.Equal(PracticeState.Paused, paused.State);
        Assert.Equal(TimeSpan.FromSeconds(2), paused.CountdownRemaining);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(PracticeState.Paused, service.Snapshot().State);

        Assert.True(service.Resume());
        Assert.Equal(PracticeState.Countdown, service.Snapshot().State);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PracticeState.Running, service.Snapshot().State);
    }

    [Fact]
    public void FirstCombatActionModeStartsOnTheFirstEligibleAction()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));

        service.Start(PracticeStartMode.FirstCombatAction);
        Assert.Equal(PracticeState.WaitingForFirstAction, service.Snapshot().State);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(PracticeState.WaitingForFirstAction, service.Snapshot().State);

        Assert.False(service.TryRecordAction(7533, "Provoke", ActionTimingClass.Ogcd));
        Assert.Equal(PracticeState.WaitingForFirstAction, service.Snapshot().State);
        Assert.True(service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd));

        var snapshot = service.Snapshot();
        Assert.Equal(PracticeState.Running, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.Elapsed);
        Assert.Equal(1, snapshot.HitCount);
    }

    [Fact]
    public void GameCountdownModeOnlyStartsFromItsExplicitTrigger()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));

        service.Start(PracticeStartMode.GameCountdown);
        Assert.Equal(PracticeState.WaitingForCountdown, service.Snapshot().State);
        Assert.False(service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd));
        Assert.Equal(PracticeState.WaitingForCountdown, service.Snapshot().State);

        Assert.True(service.BeginFromTrigger());
        Assert.True(service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd));
        Assert.Equal(1, service.Snapshot().HitCount);
        Assert.False(service.BeginFromTrigger());
    }

    [Fact]
    public void StopsAfterTheThirdWrongAction()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));
        service.Start(PracticeStartMode.Countdown, mistakeLimit: 3);
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.True(service.TryRecordAction(999, "Shield Lob", ActionTimingClass.Gcd));
        Assert.Equal(PracticeState.Running, service.Snapshot().State);
        Assert.True(service.TryRecordAction(998, "Total Eclipse", ActionTimingClass.Gcd));
        Assert.Equal(PracticeState.Running, service.Snapshot().State);
        Assert.True(service.TryRecordAction(997, "Shield Bash", ActionTimingClass.Gcd));

        var snapshot = service.Snapshot();
        Assert.Equal(PracticeState.Completed, snapshot.State);
        Assert.True(snapshot.StoppedOnMistake);
        Assert.Equal(3, snapshot.WrongCount);
        Assert.False(service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd));

        clock.Advance(TimeSpan.FromSeconds(10));
        var laterSnapshot = service.Snapshot();
        Assert.Equal(snapshot.Elapsed, laterSnapshot.Elapsed);
        Assert.True(laterSnapshot.StoppedOnMistake);
    }

    [Fact]
    public void StopsAfterTheThirdMissedReference()
    {
        var clock = new FakeClock();
        var service = CreateService(
            clock,
            new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero),
            new PracticeReferenceAction(200, "Riot Blade", ActionTimingClass.Gcd, TimeSpan.FromSeconds(1)),
            new PracticeReferenceAction(300, "Royal Authority", ActionTimingClass.Gcd, TimeSpan.FromSeconds(2)));
        service.Start(PracticeStartMode.Countdown, mistakeLimit: 3);
        clock.Advance(TimeSpan.FromSeconds(5.8));

        var snapshot = service.Snapshot();
        Assert.Equal(PracticeState.Completed, snapshot.State);
        Assert.True(snapshot.StoppedOnMistake);
        Assert.Equal(3, snapshot.MissCount);
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(500)]
    public void CorrectActionAtEitherTimingBoundaryIsAHit(int deltaMilliseconds)
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.FromSeconds(1)));
        service.Start();
        clock.Advance(
            TimeSpan.FromSeconds(3)
                .Add(TimeSpan.FromSeconds(1))
                .Add(TimeSpan.FromMilliseconds(PracticeSessionService.ReferenceStartDelayMilliseconds))
                .Add(TimeSpan.FromMilliseconds(deltaMilliseconds)));

        Assert.True(service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd));
        var snapshot = service.Snapshot();
        Assert.Equal(1, snapshot.HitCount);
        Assert.DoesNotContain(snapshot.ExpectedResults, result => result.Kind == PracticeMatchKind.Missed);
    }

    [Fact]
    public void ReferenceWithUnknownActionIdMatchesByNormalizedName()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(0, "Réquiem", ActionTimingClass.Ogcd, TimeSpan.Zero));
        service.Start();
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.True(service.TryRecordAction(999, "Requiem", ActionTimingClass.Ogcd));
        Assert.Equal(1, service.Snapshot().HitCount);
    }

    [Fact]
    public void WrongActionsStayOnTheExpectedActionAndEarlyUnmatchedActionsAreExtra()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.FromSeconds(1)));
        service.Start();
        clock.Advance(TimeSpan.FromSeconds(3));

        service.TryRecordAction(998, "Total Eclipse", ActionTimingClass.Gcd);
        clock.Advance(TimeSpan.FromSeconds(1));
        service.TryRecordAction(999, "Shield Lob", ActionTimingClass.Gcd);
        var snapshot = service.Snapshot();
        Assert.Equal(1, snapshot.ExtraCount);
        Assert.Equal(1, snapshot.WrongCount);
        Assert.Equal(0, snapshot.HitCount);
        Assert.Equal(0, snapshot.NextReferenceIndex);
    }

    [Fact]
    public void MissedReferenceCompletesAfterItsTimingWindow()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));
        service.Start();
        clock.Advance(TimeSpan.FromSeconds(3.701));

        var snapshot = service.Snapshot();
        Assert.Equal(PracticeState.Completed, snapshot.State);
        Assert.Equal(1, snapshot.MissCount);
        Assert.Equal(1, snapshot.ResolvedCount);
    }

    [Fact]
    public void RestartClearsPreviousResultsAndReturnsToCountdown()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));
        service.Start();
        clock.Advance(TimeSpan.FromSeconds(3));
        service.TryRecordAction(100, "Fast Blade", ActionTimingClass.Gcd);
        Assert.Equal(1, service.Snapshot().HitCount);

        service.Start();
        var snapshot = service.Snapshot();
        Assert.Equal(PracticeState.Countdown, snapshot.State);
        Assert.Equal(0, snapshot.HitCount);
        Assert.Equal(0, snapshot.ResolvedCount);
    }

    [Fact]
    public void ResetReturnsTheSessionToIdle()
    {
        var clock = new FakeClock();
        var service = CreateService(clock, new PracticeReferenceAction(100, "Fast Blade", ActionTimingClass.Gcd, TimeSpan.Zero));
        service.Start();
        service.Reset();

        var snapshot = service.Snapshot();
        Assert.Equal(PracticeState.Idle, snapshot.State);
        Assert.Empty(snapshot.ExpectedResults);
        Assert.Empty(snapshot.Attempts);
    }

    private static PracticeSessionService CreateService(FakeClock clock, params PracticeReferenceAction[] actions) =>
        new(
            new PracticeReferenceRotation(
                "dancing-mad",
                1331,
                "PLD",
                "7.5",
                "test",
                "Tester",
                1,
                "test",
                actions),
            clock.Read);

    private sealed class FakeClock
    {
        private TimeSpan current;

        public TimeSpan Read() => current;

        public void Advance(TimeSpan value) => current += value;
    }
}
