using OpenClicky.Core;
using OpenClicky.Core.Proactive;

namespace OpenClicky.Providers;

/// <summary>
/// Reads events through the user's own remote MCP connector: picks a read-only-looking event tool, asks it for the
/// window and keeps the events that start inside it. Nothing is logged; error messages carry no URL, header or event text.
/// The optional <paramref name="handler"/> is for tests: the connector disposes it with its HttpClient after each call.
/// </summary>
// ponytail: stand-in seam over RemoteMcpConnector; sub-project F's calendar connector replaces it.
public sealed class McpCalendarSource(string key, string displayName, ConnectorSecret secret, HttpMessageHandler? handler = null) : ICalendarSource
{
    public async Task<IReadOnlyList<UpcomingEvent>> EventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var connector = new RemoteMcpConnector(key, displayName, secret, handler);
        var tools = await connector.ListToolsAsync(ct).ConfigureAwait(false);
        var tool = CalendarEvents.ChooseTool(tools.Select(t => new CalendarToolInfo(t.Name, t.ReadOnly, t.InputSchemaJson)))
            ?? throw new InvalidDataException("The connector has no calendar tool.");
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
