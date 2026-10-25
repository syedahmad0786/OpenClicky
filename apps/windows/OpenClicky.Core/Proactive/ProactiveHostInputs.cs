namespace OpenClicky.Core.Proactive;

/// <summary>What the brief and the suggestion prompt need from saved jobs and routines.</summary>
public sealed record WorkSummary(int JobsNeedYou, int JobsFinished, IReadOnlyList<string> RecentJobTitles,
    IReadOnlyList<RoutineSlot> RoutinesToday, IReadOnlyList<string> RoutineTitles)
{
    public static readonly WorkSummary Empty = new(0, 0, [], [], []);
}

/// <summary>The host's derivations for <see cref="ProactiveInputs"/>, kept here so they can be tested without the tray app.</summary>
public static class ProactiveHostInputs
{
    public const int RecentJobs = 10;

    /// <summary>
    /// Needs-you jobs; jobs completed since yesterday's local midnight; the 10 newest job titles; non-paused routines due in the
    /// rest of the local day (titled by their job, in <paramref name="localNow"/>'s offset); every non-paused routine's job title.
    /// A routine whose job is gone is left out.
    /// </summary>
    public static WorkSummary Summarise(IReadOnlyList<WorkJob> jobs, IReadOnlyList<Routine> routines, DateTimeOffset localNow)
    {
        var midnight = new DateTimeOffset(localNow.Date, localNow.Offset);
        var (since, endOfDay) = (midnight.AddDays(-1), midnight.AddDays(1));
        var titles = jobs.GroupBy(j => j.Id).ToDictionary(g => g.Key, g => g.First().Title);
        var active = routines.Where(r => !r.IsPaused && titles.ContainsKey(r.JobId)).ToList();
        return new(
            jobs.Count(j => j.Status == WorkJobStates.NeedsYou),
            jobs.Count(j => j.Status == WorkJobStates.Completed && j.LastRunAt >= since),
            jobs.OrderByDescending(j => j.LastRunAt ?? j.CreatedAt).Take(RecentJobs).Select(j => j.Title).ToList(),
            active.Where(r => r.NextRunAt >= localNow && r.NextRunAt < endOfDay)
                .Select(r => new RoutineSlot(titles[r.JobId], r.NextRunAt.ToOffset(localNow.Offset))).ToList(),
            active.Select(r => titles[r.JobId]).ToList());
    }

    /// <summary>
    /// One microphone consent-store entry: in use (started, not stopped) by an app other than OpenClicky. Non-packaged entries are
    /// named by exe path with <c>\</c> written as <c>#</c>; ours is compared case-insensitively. Missing values mean not in use.
    /// </summary>
    public static bool MicInUseByOther(string subKeyName, long? lastUsedStart, long? lastUsedStop, string? ownExePath) =>
        lastUsedStart > 0 && lastUsedStop == 0
        && (string.IsNullOrEmpty(ownExePath) || !subKeyName.Contains(ownExePath.Replace('\\', '#'), StringComparison.OrdinalIgnoreCase));
}
