using System.Globalization;

namespace OpenClicky.Core.Proactive;

/// <summary>When the meeting countdown card shows, what it says, and which URLs it may offer.</summary>
public static class MeetingCountdown
{
    public static readonly TimeSpan ShowBefore = TimeSpan.FromSeconds(75), ShowAfter = TimeSpan.FromMinutes(2);

    /// <summary>The earliest timed, not-dismissed event inside the window, or null.</summary>
    public static UpcomingEvent? Current(IReadOnlyList<UpcomingEvent> events, DateTimeOffset now, IReadOnlySet<string> dismissedIds) =>
        events.Where(e => !e.IsAllDay && !dismissedIds.Contains(e.Id) && e.StartsAt - now <= ShowBefore && now - e.StartsAt < ShowAfter)
              .OrderBy(e => e.StartsAt).FirstOrDefault();

    /// <summary>"Starts in m:ss" in 15 s steps (rounded up), then "Starting now", then "Started N min ago".</summary>
    public static string Text(DateTimeOffset startsAt, DateTimeOffset now)
    {
        var remaining = startsAt - now;
        if (remaining > TimeSpan.Zero)
        {
            var secs = (int)Math.Ceiling(remaining.TotalSeconds / 15) * 15;
            return string.Create(CultureInfo.InvariantCulture, $"Starts in {secs / 60}:{secs % 60:00}");
        }
        var mins = (int)(-remaining).TotalMinutes;
        return mins < 1 ? "Starting now" : string.Create(CultureInfo.InvariantCulture, $"Started {mins} min ago");
    }

    /// <summary>The URL when it is absolute https without user info; otherwise null.</summary>
    public static string? SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.UserInfo.Length == 0 ? u.AbsoluteUri : null;
}
