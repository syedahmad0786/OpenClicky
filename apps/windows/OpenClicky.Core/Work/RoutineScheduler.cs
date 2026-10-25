namespace OpenClicky.Core.Work;

/// <param name="HoldUntil">Set for <see cref="RoutineScheduler.WakeHold"/> after the PC wakes: nothing starts before it.</param>
/// <param name="ActiveJobIds">Jobs already running or waiting for a slot; their routines are left alone.</param>
public sealed record RoutineTick(DateTimeOffset Now, bool NetworkUp, DateTimeOffset? HoldUntil, IReadOnlySet<string> ActiveJobIds);

/// <param name="Start">Due routines to start, oldest first. Not capped: admission queues past five.</param>
/// <param name="WaitingForNetwork">Due routines that would start but the network is down.</param>
public sealed record RoutineDecision(IReadOnlyList<Routine> Start, IReadOnlyList<Routine> WaitingForNetwork);

/// <summary>Which routines a scheduler tick starts. Pure: the tray supplies the clock, the network and the active jobs.</summary>
public static class RoutineScheduler
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WakeHold = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(300);
    public const int PauseAfterFailures = 3;
    public const string WaitingLine = "Waiting for the internet";
    public const string StaleOutcome = "Took longer than 2 hours";
    public const string PausedOutcome = "Paused after 3 failed runs";

    private static readonly RoutineDecision Nothing = new([], []);

    public static RoutineDecision Decide(IReadOnlyList<Routine> routines, RoutineTick tick)
    {
        if (tick.HoldUntil is { } hold && tick.Now < hold) return Nothing;
        // A missed run is simply due once; one job with two due routines starts once.
        var due = routines.Where(r => RoutineSchedule.IsDue(r, tick.Now) && !tick.ActiveJobIds.Contains(r.JobId))
            .OrderBy(r => r.NextRunAt).ThenBy(r => r.Id, StringComparer.Ordinal)
            .DistinctBy(r => r.JobId, StringComparer.Ordinal).ToList();
        return tick.NetworkUp ? new(due, []) : new([], due);
    }

    /// <summary>A routine-started run older than <see cref="StaleAfter"/> is cancelled and counts as a failure.</summary>
    public static bool IsStale(DateTimeOffset startedAt, DateTimeOffset now) => now - startedAt > StaleAfter;
}
