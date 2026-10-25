using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class RoutineSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static Routine Due(string id, TimeSpan ago, string? jobId = null, bool paused = false) =>
        new(id, jobId ?? "j-" + id, RoutineCadence.Daily, 9 * 60, 0, 0, paused, Now - ago);

    private static RoutineTick Tick(bool network = true, DateTimeOffset? hold = null, IReadOnlySet<string>? active = null, bool signedIn = true) =>
        new(Now, network, hold, active ?? None, signedIn);

    [Fact]
    public void Constants()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), RoutineScheduler.Tick);
        Assert.Equal(TimeSpan.FromMinutes(3), RoutineScheduler.WakeHold);
        Assert.Equal(TimeSpan.FromHours(2), RoutineScheduler.StaleAfter);
        Assert.Equal(TimeSpan.FromSeconds(300), RoutineScheduler.RetryAfter);
        Assert.Equal(3, RoutineScheduler.PauseAfterFailures);
    }

    [Fact]
    public void HoldsThreeMinutesAfterWake()
    {
        Routine[] routines = [Due("a", TimeSpan.FromMinutes(1))];
        var held = RoutineScheduler.Decide(routines, Tick(hold: Now.AddSeconds(1)));
        Assert.Empty(held.Start);
        Assert.Empty(held.WaitingForNetwork);
        Assert.Single(RoutineScheduler.Decide(routines, Tick(hold: Now)).Start); // the hold is over at HoldUntil
    }

    [Fact]
    public void MissedRunsRunOnce()
    {
        var missed = Due("a", TimeSpan.FromDays(5));
        var decision = RoutineScheduler.Decide([missed, Due("b", TimeSpan.FromMinutes(-1))], Tick());
        Assert.Equal(["a"], decision.Start.Select(r => r.Id));
        // After it runs, the next time counts from the finish: the five missed days never fire one after another.
        var after = RoutineSchedule.AfterRun(missed, RoutineRunEnd.Succeeded, "done", Now, TimeZoneInfo.Utc);
        Assert.Empty(RoutineScheduler.Decide([after], Tick()).Start);
    }

    [Fact]
    public void SkipsRoutineWhoseJobIsActive()
    {
        var decision = RoutineScheduler.Decide([Due("a", TimeSpan.FromMinutes(1)), Due("b", TimeSpan.FromMinutes(1))],
            Tick(active: new HashSet<string> { "j-a" }));
        Assert.Equal(["b"], decision.Start.Select(r => r.Id));
    }

    [Fact]
    public void TwoDueRoutinesOfOneJobStartItOnce()
    {
        var decision = RoutineScheduler.Decide([Due("a", TimeSpan.FromMinutes(1), "j"), Due("b", TimeSpan.FromMinutes(2), "j")], Tick());
        Assert.Equal(["b"], decision.Start.Select(r => r.Id));
    }

    [Fact]
    public void NetworkDownReportsWaiting()
    {
        var decision = RoutineScheduler.Decide([Due("a", TimeSpan.FromMinutes(1)), Due("b", TimeSpan.FromMinutes(-5))], Tick(network: false));
        Assert.Empty(decision.Start);
        Assert.Equal(["a"], decision.WaitingForNetwork.Select(r => r.Id));
        Assert.Empty(decision.WaitingForSignIn);
        Assert.Equal("Waiting for the internet", RoutineScheduler.WaitingLine);
    }

    [Fact]
    public void NotSignedInReportsWaitingForSignIn()
    {
        var decision = RoutineScheduler.Decide([Due("a", TimeSpan.FromMinutes(1)), Due("b", TimeSpan.FromMinutes(-5))], Tick(signedIn: false));
        Assert.Empty(decision.Start);
        Assert.Empty(decision.WaitingForNetwork);
        Assert.Equal(["a"], decision.WaitingForSignIn.Select(r => r.Id));
        Assert.Equal("Sign in to ChatGPT to run routines", RoutineScheduler.SignInLine);
        // The network is the first thing a routine waits for.
        var both = RoutineScheduler.Decide([Due("a", TimeSpan.FromMinutes(1))], Tick(network: false, signedIn: false));
        Assert.Equal(["a"], both.WaitingForNetwork.Select(r => r.Id));
        Assert.Empty(both.WaitingForSignIn);
    }

    [Fact]
    public void WaitingNotesNameWhatEachRoutineWaitsFor()
    {
        var decision = new RoutineDecision([], [Due("a", TimeSpan.Zero)], [Due("b", TimeSpan.Zero)]);
        var notes = RoutineScheduler.WaitingNotes(decision);
        Assert.Equal("Waiting for the internet", notes["a"]);
        Assert.Equal("Sign in to ChatGPT to run routines", notes["b"]);
        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public void KeepAwakeOnAcWithAnUnpausedRoutineOrWhileARoutineRuns()
    {
        Routine[] live = [Due("a", TimeSpan.Zero)];
        Routine[] paused = [Due("a", TimeSpan.Zero, paused: true)];
        Assert.True(RoutineScheduler.KeepAwake(live, onAc: true, routineRunInProgress: false));
        Assert.False(RoutineScheduler.KeepAwake(live, onAc: false, routineRunInProgress: false));
        Assert.False(RoutineScheduler.KeepAwake(paused, onAc: true, routineRunInProgress: false));
        Assert.False(RoutineScheduler.KeepAwake([], onAc: true, routineRunInProgress: false));
        // A routine run in progress keeps the PC awake on any power source.
        Assert.True(RoutineScheduler.KeepAwake(paused, onAc: false, routineRunInProgress: true));
        Assert.True(RoutineScheduler.KeepAwake([], onAc: false, routineRunInProgress: true));
    }

    [Fact]
    public void ALongGapBetweenTicksHoldsAsAfterAWake()
    {
        var tick = RoutineScheduler.Tick;
        // No tick yet, or a normal gap: the hold stays as it was.
        Assert.Null(RoutineScheduler.HoldAfterGap(null, Now, null));
        Assert.Null(RoutineScheduler.HoldAfterGap(Now - tick * 2, Now, null));
        Assert.Equal(Now.AddSeconds(5), RoutineScheduler.HoldAfterGap(Now - tick, Now, Now.AddSeconds(5)));
        // More than two ticks missed (a sleep Windows did not report as a resume): hold for WakeHold from now.
        Assert.Equal(Now + RoutineScheduler.WakeHold, RoutineScheduler.HoldAfterGap(Now - tick * 2 - TimeSpan.FromSeconds(1), Now, null));
        // A longer hold already set is kept.
        Assert.Equal(Now.AddMinutes(10), RoutineScheduler.HoldAfterGap(Now - TimeSpan.FromHours(1), Now, Now.AddMinutes(10)));
    }

    [Theory]
    [InlineData("completed", false, RoutineRunEnd.Succeeded)]
    [InlineData("failed", false, RoutineRunEnd.Failed)]
    [InlineData("stopped", false, RoutineRunEnd.Stopped)]
    [InlineData("stopped", true, RoutineRunEnd.Failed)]
    [InlineData("failed", true, RoutineRunEnd.Failed)]
    [InlineData("completed", true, RoutineRunEnd.Succeeded)]
    public void RunEndCountsOnlyFailuresAndTimeouts(string status, bool timedOut, RoutineRunEnd expected) =>
        Assert.Equal(expected, RoutineScheduler.RunEnd(status, timedOut));

    [Fact]
    public void PausedNeverStarts()
    {
        var decision = RoutineScheduler.Decide([Due("a", TimeSpan.FromDays(1), paused: true)], Tick());
        Assert.Empty(decision.Start);
        Assert.Empty(RoutineScheduler.Decide([Due("a", TimeSpan.FromDays(1), paused: true)], Tick(network: false)).WaitingForNetwork);
        Assert.Empty(RoutineScheduler.Decide([Due("a", TimeSpan.FromDays(1), paused: true)], Tick(signedIn: false)).WaitingForSignIn);
    }

    [Fact]
    public void RespectsConcurrencyCap()
    {
        var routines = Enumerable.Range(0, 7).Select(i => Due("r" + i, TimeSpan.FromMinutes(i + 1))).ToList();
        var decision = RoutineScheduler.Decide(routines, Tick());
        // Oldest NextRunAt first; the scheduler does not cap, admission does.
        Assert.Equal(["r6", "r5", "r4", "r3", "r2", "r1", "r0"], decision.Start.Select(r => r.Id));
        var admission = new JobAdmission();
        var outcomes = decision.Start.Select(r => admission.TryStartOrEnqueue(r.JobId)).ToList();
        Assert.Equal(5, outcomes.Count(o => o == JobAdmission.Outcome.Started));
        Assert.Equal(["j-r1", "j-r0"], admission.Queued);
    }

    [Fact]
    public void RunsOverTwoHoursAreStale()
    {
        Assert.False(RoutineScheduler.IsStale(Now - TimeSpan.FromHours(2), Now));
        Assert.True(RoutineScheduler.IsStale(Now - TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1), Now));
        Assert.Equal("Took longer than 2 hours", RoutineScheduler.StaleOutcome);
    }
}
