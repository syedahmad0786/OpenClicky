namespace OpenClicky.Core.Work;

/// <param name="HoldUntil">Set for <see cref="RoutineScheduler.WakeHold"/> after the PC wakes: nothing starts before it.</param>
/// <param name="ActiveJobIds">Jobs already running or waiting for a slot; their routines are left alone.</param>
/// <param name="SignedIn">Jobs can run (ChatGPT is signed in); without it due routines wait, as for the network.</param>
/// <param name="Paused">OpenClicky is paused from the tray: due routines wait until it is resumed.</param>
public sealed record RoutineTick(DateTimeOffset Now, bool NetworkUp, DateTimeOffset? HoldUntil, IReadOnlySet<string> ActiveJobIds, bool SignedIn = true,
    bool Paused = false);

/// <param name="Start">Due routines to start, oldest first. Not capped: admission queues past five.</param>
/// <param name="WaitingForNetwork">Due routines that would start but the network is down.</param>
/// <param name="WaitingForSignIn">Due routines that would start but ChatGPT is not signed in (the network is up).</param>
/// <param name="WaitingWhilePaused">Due routines that would start but OpenClicky is paused (null: none).</param>
public sealed record RoutineDecision(IReadOnlyList<Routine> Start, IReadOnlyList<Routine> WaitingForNetwork, IReadOnlyList<Routine> WaitingForSignIn,
    IReadOnlyList<Routine>? WaitingWhilePaused = null);

/// <summary>How a routine's run ended, for <see cref="RoutineSchedule.AfterRun"/>: only <see cref="Failed"/> counts towards the pause.</summary>
public enum RoutineRunEnd { Succeeded, Failed, Stopped }

/// <summary>Which routines a scheduler tick starts. Pure: the tray supplies the clock, the network and the active jobs.</summary>
public static class RoutineScheduler
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WakeHold = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(300);
    public const int PauseAfterFailures = 3;
    public const string WaitingLine = "Waiting for the internet";
    public const string SignInLine = "Sign in to ChatGPT to run routines";
    public const string PausedLine = "OpenClicky is paused";
    public const string StaleOutcome = "Took longer than 2 hours";
    public const string PausedOutcome = "Paused after 3 failed runs";

    private static readonly RoutineDecision Nothing = new([], [], []);

    public static RoutineDecision Decide(IReadOnlyList<Routine> routines, RoutineTick tick)
    {
        if (tick.HoldUntil is { } hold && tick.Now < hold) return Nothing;
        // A missed run is simply due once; one job with two due routines starts once.
        var due = routines.Where(r => RoutineSchedule.IsDue(r, tick.Now) && !tick.ActiveJobIds.Contains(r.JobId))
            .OrderBy(r => r.NextRunAt).ThenBy(r => r.Id, StringComparer.Ordinal)
            .DistinctBy(r => r.JobId, StringComparer.Ordinal).ToList();
        return tick.Paused ? new([], [], [], due) : !tick.NetworkUp ? new([], due, []) : !tick.SignedIn ? new([], [], due) : new(due, [], []);
    }

    /// <summary>The jobs a tick leaves alone: running or waiting for a slot (<paramref name="admitted"/>), or waiting on the user's own
    /// step (needs-you): a routine must not supersede a step the user is in the middle of.</summary>
    public static IReadOnlySet<string> ActiveJobIds(IEnumerable<Routine> routines, Func<string, bool> admitted, IEnumerable<string> waitingForUser)
    {
        var waiting = waitingForUser.ToHashSet(StringComparer.Ordinal);
        return routines.Select(r => r.JobId).Where(id => admitted(id) || waiting.Contains(id)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Routine id to the line its row shows while it waits.</summary>
    public static IReadOnlyDictionary<string, string> WaitingNotes(RoutineDecision decision)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in decision.WaitingForNetwork) notes[r.Id] = WaitingLine;
        foreach (var r in decision.WaitingForSignIn) notes[r.Id] = SignInLine;
        foreach (var r in decision.WaitingWhilePaused ?? []) notes[r.Id] = PausedLine;
        return notes;
    }

    /// <summary>The PC is kept awake on mains power while an unpaused routine exists, and on any power while a routine's run is in progress.</summary>
    public static bool KeepAwake(IReadOnlyList<Routine> routines, bool onAc, bool routineRunInProgress) =>
        routineRunInProgress || onAc && routines.Any(r => !r.IsPaused);

    /// <summary>More than two ticks went by (the PC slept without Windows reporting a resume): hold as after a wake. A longer hold stays.</summary>
    public static DateTimeOffset? HoldAfterGap(DateTimeOffset? lastTick, DateTimeOffset now, DateTimeOffset? hold)
    {
        if (lastTick is not { } last || now - last <= Tick * 2) return hold;
        var wake = now + WakeHold;
        return hold is { } held && held > wake ? held : wake;
    }

    /// <summary>A run's end as the routine counts it: a user stop is not a failure, a run cancelled for taking too long is (unless it completed first).</summary>
    public static RoutineRunEnd RunEnd(string status, bool timedOut) =>
        status == AgentProgressStates.Completed ? RoutineRunEnd.Succeeded
        : timedOut || status == AgentProgressStates.Failed ? RoutineRunEnd.Failed
        : RoutineRunEnd.Stopped;

    /// <summary>A routine-started run older than <see cref="StaleAfter"/> is cancelled and counts as a failure.</summary>
    public static bool IsStale(DateTimeOffset startedAt, DateTimeOffset now) => now - startedAt > StaleAfter;
}
