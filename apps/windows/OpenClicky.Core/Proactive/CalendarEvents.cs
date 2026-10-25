using System.Globalization;
using System.Text.Json;

namespace OpenClicky.Core.Proactive;

/// <summary>One tool a calendar connector offers: its name, read-only hint and JSON input schema (all untrusted).</summary>
public sealed record CalendarToolInfo(string Name, bool ReadOnly, string InputSchemaJson);

/// <summary>
/// Heuristics for reading events from an arbitrary calendar tool: pick a read tool, fill its time-window arguments
/// and pull events out of whatever JSON it returns. Connector output is untrusted, so nothing here throws on bad input.
/// </summary>
public static class CalendarEvents
{
    public const int MaxEvents = 50;
    private const int MaxDepth = 8, MaxNodes = 2000, UrlChars = 2000;
    private static readonly string[] Reads = ["list", "find", "search", "get", "fetch"];
    private static readonly string[] Writes = ["create", "update", "delete", "insert", "move", "quick", "patch", "remove", "send", "respond", "rsvp", "import"];

    /// <summary>
    /// The tool to list events with, or null. Its name must say "event" and a read verb and no write verb.
    /// Tools whose required arguments can all be filled come first, then read-only ones, then the shorter name.
    /// </summary>
    // ponytail: name heuristics; F's calendar connector replaces this adapter.
    public static CalendarToolInfo? ChooseTool(IEnumerable<CalendarToolInfo> tools)
    {
        if (tools is null) return null;
        CalendarToolInfo? best = null;
        var bestFillable = false;
        foreach (var tool in tools)
        {
            if (tool?.Name is not { } name) continue;
            var n = name.ToLowerInvariant();
            if (!n.Contains("event", StringComparison.Ordinal) || !Reads.Any(n.Contains) || Writes.Any(n.Contains)) continue;
            var fillable = BuildArguments(tool.InputSchemaJson, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) is not null;
            var order = best is null ? -1 : Rank(tool, fillable).CompareTo(Rank(best, bestFillable));
            if (order < 0 || (order == 0 && string.CompareOrdinal(tool.Name, best!.Name) < 0)) (best, bestFillable) = (tool, fillable);
        }
        return best;
    }

    private static (int, int, int) Rank(CalendarToolInfo t, bool fillable) => (fillable ? 0 : 1, t.ReadOnly ? 0 : 1, t.Name.Length);

    /// <summary>
    /// Arguments for the tool's schema: window bounds (UTC, second precision), a 50-result limit, the primary
    /// calendar and expanded recurrences, matched by property name ignoring case and '_'. Null when the schema
    /// is unreadable or a required property cannot be filled.
    /// </summary>
    public static JsonElement? BuildArguments(string inputSchemaJson, DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            using var schema = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputSchemaJson) ? "{}" : inputSchemaJson);
            var root = schema.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var args = new Dictionary<string, object>(StringComparer.Ordinal);
            if (root.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
                foreach (var property in properties.EnumerateObject())
                    if (Fill(property.Name.ToLowerInvariant().Replace("_", "", StringComparison.Ordinal), from, to) is { } value)
                        args[property.Name] = value;
            if (root.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                foreach (var name in required.EnumerateArray())
                    if (name.ValueKind != JsonValueKind.String || !args.ContainsKey(name.GetString()!)) return null;
            return JsonSerializer.SerializeToElement(args);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException) { return null; }
    }

    private static object? Fill(string key, DateTimeOffset from, DateTimeOffset to) => key switch
    {
        "timemin" or "start" or "starttime" or "startdate" or "from" or "after" => Utc(from),
        "timemax" or "end" or "endtime" or "enddate" or "to" or "before" => Utc(to),
        "maxresults" or "limit" or "count" or "top" => MaxEvents,
        "calendarid" => "primary",
        "singleevents" => true,
        _ => null,
    };

    private static string Utc(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Events found in a tool's text reply: the whole text as JSON, else the span from the first '{' or '[' to its last
    /// closing match. Walks at most 8 levels and 2,000 nodes, skips cancelled events, dedupes by id, keeps at most
    /// <see cref="MaxEvents"/>. A date-only start is all-day from local midnight in <paramref name="zone"/>.
    /// </summary>
    public static IReadOnlyList<UpcomingEvent> Parse(string text, TimeZoneInfo zone)
    {
        var events = new List<UpcomingEvent>();
        if (string.IsNullOrWhiteSpace(text) || zone is null) return events;
        try
        {
            using var doc = TryParse(text) ?? TryParse(JsonSpan(text));
            if (doc is null) return events;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var nodes = 0;
            Walk(doc.RootElement, 0);

            void Walk(JsonElement element, int depth)
            {
                if (depth > MaxDepth || ++nodes > MaxNodes || events.Count >= MaxEvents) return;
                if (element.ValueKind == JsonValueKind.Array)
                    foreach (var item in element.EnumerateArray()) Walk(item, depth + 1);
                else if (element.ValueKind == JsonValueKind.Object)
                {
                    if (Title(element) is { } title && Time(element, zone, "start", "startTime", "start_time", "startsAt", "start_datetime") is { } start)
                    {
                        if (Event(element, zone, title, start) is { } e && ids.Add(e.Id)) events.Add(e);
                        return;
                    }
                    foreach (var property in element.EnumerateObject()) Walk(property.Value, depth + 1);
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException) { }
        return events;
    }

    private static JsonDocument? TryParse(string? json)
    {
        if (json is null) return null;
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static string? JsonSpan(string text)
    {
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0) return null;
        var end = text.LastIndexOf(text[start] == '{' ? '}' : ']');
        return end > start ? text[start..(end + 1)] : null;
    }

    private static UpcomingEvent? Event(JsonElement e, TimeZoneInfo zone, string title, (DateTimeOffset At, bool AllDay) start)
    {
        if (string.Equals(Str(e, "status"), "cancelled", StringComparison.OrdinalIgnoreCase)) return null;
        var end = Time(e, zone, "end", "endTime", "end_time", "endsAt", "end_datetime");
        var id = Clip(Id(e), UrlChars) ?? $"{title}|{start.At.ToString("O", CultureInfo.InvariantCulture)}";
        var location = Str(e, "location") ?? (e.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.Object ? Str(l, "displayName") : null);
        return new(id, title, start.At, end?.At, start.AllDay, Clip(MeetingUrl(e), UrlChars),
            Clip(Str(e, "htmlLink", "html_link", "webLink", "web_link"), UrlChars), Clip(location, UrlChars));
    }

    private static string? Title(JsonElement e) => Clip(Str(e, "summary", "title", "subject", "name"), ProactiveCards.TitleChars);

    private static string? Id(JsonElement e)
    {
        foreach (var name in (string[])["id", "eventId", "event_id", "iCalUID"])
            if (e.TryGetProperty(name, out var v))
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                else if (Str(v) is { } s) return s;
        return null;
    }

    private static string? MeetingUrl(JsonElement e)
    {
        if (Str(e, "hangoutLink", "meetingUrl", "meeting_url", "joinUrl", "join_url", "onlineMeetingUrl") is { } url) return url;
        if (e.TryGetProperty("onlineMeeting", out var online) && online.ValueKind == JsonValueKind.Object && Str(online, "joinUrl") is { } join) return join;
        if (e.TryGetProperty("conferenceData", out var conference) && conference.ValueKind == JsonValueKind.Object
            && conference.TryGetProperty("entryPoints", out var points) && points.ValueKind == JsonValueKind.Array)
            foreach (var point in points.EnumerateArray().Take(20))
                if (point.ValueKind == JsonValueKind.Object && Str(point, "entryPointType") == "video" && Str(point, "uri") is { } uri) return uri;
        return null;
    }

    private static (DateTimeOffset At, bool AllDay)? Time(JsonElement e, TimeZoneInfo zone, params string[] names)
    {
        foreach (var name in names)
        {
            if (!e.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Object)
            {
                if ((ParseTime(Str(v, "dateTime"), zone) ?? ParseTime(Str(v, "date_time"), zone) ?? ParseTime(Str(v, "date"), zone)) is { } t) return t;
            }
            else if (ParseTime(Str(v), zone) is { } t) return t;
        }
        return null;
    }

    private static (DateTimeOffset, bool)? ParseTime(string? s, TimeZoneInfo zone)
    {
        if (s is null) return null;
        s = s.Trim();
        try
        {
            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return (new DateTimeOffset(day, zone.GetUtcOffset(day)), true);
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return (at, false);
        }
        catch (ArgumentException) { } // a midnight that falls outside DateTimeOffset's range in this zone
        return null;
    }

    private static string? Str(JsonElement e, params string[] names)
    {
        foreach (var name in names)
            if (e.TryGetProperty(name, out var v) && Str(v) is { } s) return s;
        return null;
    }

    /// <summary>The string value, or null when it is not a string or cannot be decoded (a lone surrogate).</summary>
    private static string? Str(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String) return null;
        try { return v.GetString(); }
        catch (InvalidOperationException) { return null; }
    }

    private static string? Clip(string? s, int max) => ProactiveCards.Clean(s, max) is { Length: > 0 } c ? c : null;
}
