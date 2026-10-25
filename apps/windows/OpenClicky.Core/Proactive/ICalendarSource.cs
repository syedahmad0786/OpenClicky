namespace OpenClicky.Core.Proactive;

/// <summary>One calendar event. Text fields are untrusted data: clean them before showing.</summary>
public sealed record UpcomingEvent(string Id, string Title, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool IsAllDay, string? MeetingUrl, string? HtmlLink, string? Location);

/// <summary>Where the countdown and the morning brief get events from (the connector the user picked).</summary>
public interface ICalendarSource
{
    Task<IReadOnlyList<UpcomingEvent>> EventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
