using System.Globalization;
using System.Text;
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
    /// The tool to list events with, or null. The connector must mark it read-only, and its name must say "event" and a
    /// read verb and no write verb. Tools whose required arguments can all be filled come first, then the shorter name.
    /// </summary>
    // ponytail: name heuristics over a generic MCP connector (F has no calendar-specific connector); a typed calendar connector would replace them.
    public static CalendarToolInfo? ChooseTool(IEnumerable<CalendarToolInfo> tools)
    {
        if (tools is null) return null;
        CalendarToolInfo? best = null;
        var bestFillable = false;
        foreach (var tool in tools)
        {
            if (tool?.Name is not { } name || !tool.ReadOnly) continue;
            var n = name.ToLowerInvariant();
            if (!n.Contains("event", StringComparison.Ordinal) || !Reads.Any(n.Contains) || Writes.Any(n.Contains)) continue;
            var fillable = BuildArguments(tool.InputSchemaJson, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) is not null;
            var order = best is null ? -1 : Rank(tool, fillable).CompareTo(Rank(best, bestFillable));
            if (order < 0 || (order == 0 && string.CompareOrdinal(tool.Name, best!.Name) < 0)) (best, bestFillable) = (tool, fillable);
        }
        return best;
    }

    private static (int, int) Rank(CalendarToolInfo t, bool fillable) => (fillable ? 0 : 1, t.Name.Length);

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
    /// closing match, else every complete JSON value a tolerant scan can read (a reply cut at the connector's size cap,
    /// or several JSON blocks one after another). Walks at most 8 levels and 2,000 nodes, skips cancelled events, dedupes by id, keeps at most
    /// <see cref="MaxEvents"/>. A date-only start is all-day from local midnight in <paramref name="zone"/>.
    /// </summary>
    public static IReadOnlyList<UpcomingEvent> Parse(string text, TimeZoneInfo zone)
    {
        var events = new List<UpcomingEvent>();
        if (string.IsNullOrWhiteSpace(text) || zone is null) return events;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nodes = 0;
        try
        {
            using var doc = TryParse(text) ?? TryParse(JsonSpan(text));
            if (doc is not null) Walk(doc.RootElement, 0);
            else Scan(Encoding.UTF8.GetBytes(text));

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

            // Reads tokens from each '{' or '[' on; every complete object or array within the depth cap is parsed and
            // walked, an incomplete one is entered token by token. Invalid JSON (prose, or a second top-level block)
            // restarts the scan at the next '{' or '[' after the last good token.
            // ponytail: a restart may rescan to the end of the text; the node cap bounds restarts, fine for 20,000 chars.
            void Scan(byte[] bytes)
            {
                var offset = 0;
                while (++nodes <= MaxNodes && events.Count < MaxEvents && offset < bytes.Length)
                {
                    var start = bytes.AsSpan(offset).IndexOfAny((byte)'{', (byte)'[');
                    if (start < 0) return;
                    offset += start;
                    var reader = new Utf8JsonReader(bytes.AsSpan(offset), isFinalBlock: false, state: default);
                    var consumed = 0;
                    try
                    {
                        while (reader.Read())
                        {
                            if (++nodes > MaxNodes || events.Count >= MaxEvents) return;
                            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && reader.CurrentDepth <= MaxDepth
                                && JsonDocument.TryParseValue(ref reader, out var value))
                                using (value) Walk(value.RootElement, reader.CurrentDepth);
                            consumed = (int)reader.BytesConsumed;
                        }
                        return; // the rest is an incomplete value: nothing more to read
                    }
                    catch (JsonException) { }
                    offset += Math.Max(1, consumed);
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
                var named = NamedZone(Str(v, "timeZone"));
                if ((ParseTime(Str(v, "dateTime"), zone, named) ?? ParseTime(Str(v, "date_time"), zone, named) ?? ParseTime(Str(v, "date"), zone)) is { } t) return t;
            }
            else if (ParseTime(Str(v), zone) is { } t) return t;
        }
        return null;
    }

    /// <summary>A Windows or IANA zone id from a start/end object (Google's and Graph's "timeZone"), or null.</summary>
    private static TimeZoneInfo? NamedZone(string? id)
    {
        if (id is not { Length: > 0 and <= 64 }) return null;
        try { return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : null; }
        catch (Exception e) when (e is ArgumentException or InvalidTimeZoneException or System.Security.SecurityException) { return null; }
    }

    /// <summary>
    /// A date-only value is all-day from midnight in <paramref name="zone"/>; a date-time without an offset is read in
    /// <paramref name="named"/> when given, else as UTC.
    /// </summary>
    private static (DateTimeOffset, bool)? ParseTime(string? s, TimeZoneInfo zone, TimeZoneInfo? named = null)
    {
        if (s is null) return null;
        s = s.Trim();
        try
        {
            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return (new DateTimeOffset(day, zone.GetUtcOffset(day)), true);
            if (named is not null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var wall)
                && wall.Kind == DateTimeKind.Unspecified)
                return (new DateTimeOffset(wall, named.GetUtcOffset(wall)), false);
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
