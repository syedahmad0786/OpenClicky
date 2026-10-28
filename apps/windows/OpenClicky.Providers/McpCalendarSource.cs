using OpenClicky.Core;
using OpenClicky.Core.Proactive;

namespace OpenClicky.Providers;

/// <summary>
/// Reads events through one of the user's connectors (sub-project F's: browser sign-in, key, none or a local command), opened fresh per
/// fetch: picks an event tool the connector marks read-only, asks it for the window and keeps the events that start inside it.
/// Nothing is logged; error messages carry no URL, header or event text.
/// </summary>
public sealed class McpCalendarSource(Func<McpConnector?> open) : ICalendarSource
{
    /// <summary>A remote connector with a plain secret; <paramref name="handler"/> is for tests (disposed with its HttpClient after each call).</summary>
    public McpCalendarSource(string key, string displayName, ConnectorSecret secret, HttpMessageHandler? handler = null)
        : this(() => new RemoteMcpConnector(key, displayName, secret, handler)) { }

    public async Task<IReadOnlyList<UpcomingEvent>> EventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var connector = open() ?? throw new InvalidDataException("The calendar connector needs sign-in.");
        var tools = await connector.ListToolsAsync(ct).ConfigureAwait(false);
        var tool = CalendarEvents.ChooseTool(tools.Select(t => new CalendarToolInfo(t.Name, t.ReadOnly, t.InputSchemaJson)))
            ?? throw new InvalidDataException("no read-only calendar tool");
        var arguments = CalendarEvents.BuildArguments(tool.InputSchemaJson, from, to)
            ?? throw new InvalidDataException("The calendar tool needs an argument OpenClicky cannot fill.");
        var result = await connector.CallToolAsync(tool.Name, arguments, ct).ConfigureAwait(false);
        if (result.IsError) throw new InvalidDataException("The calendar tool returned an error.");
        return CalendarEvents.Parse(result.Text, TimeZoneInfo.Local)
            .Where(e => e.StartsAt >= from && e.StartsAt < to)
            .OrderBy(e => e.StartsAt)
            .ToList();
    }
}
