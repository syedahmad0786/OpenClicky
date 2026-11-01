using System.Globalization;

namespace OpenClicky.Core.Proactive;

/// <summary>When the morning brief is due and what it says. Pure: callers pass the clock and the data.</summary>
public static class MorningBrief
{
    public static readonly TimeSpan WindowStart = TimeSpan.FromHours(7), WindowEnd = TimeSpan.FromHours(12), ActiveWithin = TimeSpan.FromSeconds(30);
    private const int TitleChars = 80;

    /// <summary>The once-a-day key: the local date as yyyy-MM-dd. Shared by every proactive "once per day" rule.</summary>
    public static string DayKey(DateTimeOffset localNow) => localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool InWindow(DateTimeOffset localNow) => localNow.TimeOfDay >= WindowStart && localNow.TimeOfDay < WindowEnd;

    /// <summary>In the window, not shown today, and the user touched keyboard or mouse in the last 30 s.</summary>
    public static bool IsDue(DateTimeOffset localNow, string? lastShownDay, DateTimeOffset? lastUserActivity) =>
        InWindow(localNow) && lastShownDay != DayKey(localNow) && lastUserActivity is { } a && localNow - a <= ActiveWithin;

    public static string Title(string? name)
    {
        var n = ProactiveCards.Clean(name, 60);
        return n.Length == 0 ? "Good morning" : "Good morning, " + n;
    }

    /// <param name="today">null when no calendar is connected or it could not be read: no meeting line.</param>
    public static IReadOnlyList<string> Lines(DateTimeOffset localNow, IReadOnlyList<UpcomingEvent>? today, int jobsNeedYou, int jobsFinished, IReadOnlyList<RoutineSlot> routinesToday)
    {
        var lines = new List<string>();
        if (today is not null)
        {
            var timed = today.Where(e => !e.IsAllDay).OrderBy(e => e.StartsAt).ToList();
            lines.Add(timed.Count switch
            {
                0 => "No meetings on your calendar today.",
                1 => $"1 meeting today: {Item(timed[0].Title, timed[0].StartsAt, localNow)}.",
                _ => $"{timed.Count} meetings today. First: {Item(timed[0].Title, timed[0].StartsAt, localNow)}.",
            });
        }
        if (jobsNeedYou > 0) lines.Add(jobsNeedYou == 1 ? "1 job needs you." : $"{jobsNeedYou} jobs need you.");
        if (jobsFinished > 0) lines.Add(jobsFinished == 1 ? "1 job finished since yesterday." : $"{jobsFinished} jobs finished since yesterday.");
        if (routinesToday.Count == 1) lines.Add($"Routine today: {Item(routinesToday[0].Title, routinesToday[0].At, localNow)}.");
        else if (routinesToday.Count > 1)
        {
            var ordered = routinesToday.OrderBy(r => r.At).ToList();
            var next = ordered.FirstOrDefault(r => r.At >= localNow) ?? ordered[0];
            lines.Add($"{ordered.Count} routines run today. Next: {Item(next.Title, next.At, localNow)}.");
        }
        if (lines.Count == 0) lines.Add("Nothing on your plate yet.");
        return lines;
    }

    /// <summary>The chat message "Plan my day" sends (the only way the brief's lines leave the PC).</summary>
    public static string PlanPrompt(IReadOnlyList<string> lines) =>
        "Help me plan my day. My morning brief:\n" + string.Join("\n", lines.Select(l => "- " + l));

    // The time is shown on the clock the caller passed (local), whatever offset the source used.
    private static string Item(string title, DateTimeOffset at, DateTimeOffset localNow) =>
        ProactiveCards.Clean(title, TitleChars) + " at " + at.ToOffset(localNow.Offset).ToString("H:mm", CultureInfo.InvariantCulture);
}
