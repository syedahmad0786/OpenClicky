using System.Text.Json;
using OpenClicky.Core.Proactive;

namespace OpenClicky.Core.Tests;

public class CalendarEventsTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTimeOffset From = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero), To = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private static CalendarToolInfo Tool(string name, bool readOnly = false, string schema = "{}") => new(name, readOnly, schema);

    [Fact]
    public void ChoosesReadToolAndPrefersReadOnly()
    {
        var chosen = CalendarEvents.ChooseTool([Tool("create_event"), Tool("list_events"), Tool("GOOGLECALENDAR_FIND_EVENT", readOnly: true)]);
        Assert.Equal("GOOGLECALENDAR_FIND_EVENT", chosen?.Name);
        Assert.Null(CalendarEvents.ChooseTool([Tool("create_event"), Tool("delete_event")]));
        Assert.Equal("list_events", CalendarEvents.ChooseTool([Tool("search_events"), Tool("list_events")])?.Name);
        // A shorter tool that needs an id OpenClicky cannot supply loses to one it can fill.
        Assert.Equal("list_events", CalendarEvents.ChooseTool(
            [Tool("get_event", true, """{"properties":{"eventId":{}},"required":["eventId"]}"""), Tool("list_events", true)])?.Name);
    }

    [Fact]
    public void NeverPicksMutatingTool()
    {
        Assert.Null(CalendarEvents.ChooseTool([Tool("update_event_list"), Tool("quick_add_event"), Tool("events_import")]));
        Assert.Null(CalendarEvents.ChooseTool([Tool("list_calendars", true), Tool("respond_to_event"), Tool("move_event_get")]));
        Assert.Null(CalendarEvents.ChooseTool(null!));
        Assert.Null(CalendarEvents.ChooseTool([null!, new CalendarToolInfo(null!, true, "{}")]));
    }

    [Fact]
    public void FillsKnownArguments()
    {
        var args = CalendarEvents.BuildArguments(
            """{"type":"object","properties":{"timeMin":{"type":"string"},"time_max":{},"maxResults":{},"calendarId":{},"singleEvents":{},"orderBy":{}},"required":["timeMin"]}""",
            From, To)!.Value;
        Assert.Equal("2026-10-25T08:00:00Z", args.GetProperty("timeMin").GetString());
        Assert.Equal("2026-10-26T08:00:00Z", args.GetProperty("time_max").GetString());
        Assert.Equal(50, args.GetProperty("maxResults").GetInt32());
        Assert.Equal("primary", args.GetProperty("calendarId").GetString());
        Assert.True(args.GetProperty("singleEvents").GetBoolean());
        Assert.False(args.TryGetProperty("orderBy", out _));

        var local = CalendarEvents.BuildArguments("""{"properties":{"start_date":{},"Before":{}}}""",
            new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.FromHours(1)), To)!.Value;
        Assert.Equal("2026-10-25T08:00:00Z", local.GetProperty("start_date").GetString());
        Assert.Equal("2026-10-26T08:00:00Z", local.GetProperty("Before").GetString());
    }

    [Fact]
    public void UnfillableRequiredArgumentRefuses()
    {
        Assert.Null(CalendarEvents.BuildArguments("""{"properties":{"query":{},"timeMin":{}},"required":["query"]}""", From, To));
        Assert.Null(CalendarEvents.BuildArguments("""{"required":["timeMin"]}""", From, To));
        Assert.Equal("{}", CalendarEvents.BuildArguments("{}", From, To)!.Value.GetRawText());
    }

    [Fact]
    public void ParsesGoogleShape()
    {
        var e = Assert.Single(CalendarEvents.Parse("""
            {"items":[{"id":"a","summary":"Standup","start":{"dateTime":"2026-10-25T09:30:00+01:00"},"end":{"dateTime":"2026-10-25T09:45:00+01:00"},
              "hangoutLink":"https://meet.google.com/x","htmlLink":"https://calendar.google.com/e","location":{"displayName":"Room 4"}}]}
            """, Zone));
        Assert.Equal("a", e.Id);
        Assert.Equal("Standup", e.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 8, 30, 0, TimeSpan.Zero), e.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 8, 45, 0, TimeSpan.Zero), e.EndsAt);
        Assert.False(e.IsAllDay);
        Assert.Equal("https://meet.google.com/x", e.MeetingUrl);
        Assert.Equal("https://calendar.google.com/e", e.HtmlLink);
        Assert.Equal("Room 4", e.Location);
    }

    [Fact]
    public void ParsesFlatShapeAndAllDay()
    {
        var events = CalendarEvents.Parse("""
            Here are your events:
            [{"title":"Review","start":"2026-10-25T14:00:00Z","joinUrl":"https://zoom.us/j/1"},{"subject":"Holiday","start":{"date":"2026-10-25"}}]
            Anything else?
            """, Zone);
        Assert.Equal(2, events.Count);
        Assert.Equal("Review", events[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.Zero), events[0].StartsAt);
        Assert.Equal("https://zoom.us/j/1", events[0].MeetingUrl);
        Assert.Equal("Review|2026-10-25T14:00:00.0000000+00:00", events[0].Id);
        Assert.Equal("Holiday", events[1].Title);
        Assert.True(events[1].IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(1)), events[1].StartsAt);

        var teams = Assert.Single(CalendarEvents.Parse("""
            {"value":[{"eventId":"t","name":"Sync\u0007  now","startTime":"2026-10-25T10:00:00","onlineMeeting":{"joinUrl":"https://teams.microsoft.com/l/1"},"webLink":"https://outlook.office.com/e","location":"HQ"},
             {"event_id":"c","title":"Video","start_time":{"date_time":"2026-10-25T11:00:00Z"},"conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:1"},{"entryPointType":"video","uri":"https://meet.google.com/y"}]}}]}
            """, Zone), x => x.Id == "t");
        Assert.Equal("Sync now", teams.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero), teams.StartsAt); // no offset → UTC
        Assert.Equal("https://teams.microsoft.com/l/1", teams.MeetingUrl);
        Assert.Equal("https://outlook.office.com/e", teams.HtmlLink);
        Assert.Equal("HQ", teams.Location);
        var video = CalendarEvents.Parse("""{"e":[{"event_id":"c","title":"Video","start_time":{"date_time":"2026-10-25T11:00:00Z"},"conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:1"},{"entryPointType":"video","uri":"https://meet.google.com/y"}]}}]}""", Zone);
        Assert.Equal("https://meet.google.com/y", Assert.Single(video).MeetingUrl);
    }

    [Fact]
    public void SkipsCancelledDedupesAndCaps()
    {
        var events = CalendarEvents.Parse("""
            [{"id":"x","summary":"Gone","status":"cancelled","start":"2026-10-25T10:00:00Z"},
             {"id":"a","summary":"One","start":"2026-10-25T10:00:00Z"},{"id":"a","summary":"One again","start":"2026-10-25T11:00:00Z"}]
            """, Zone);
        Assert.Equal("One", Assert.Single(events).Title);

        var many = "[" + string.Join(",", Enumerable.Range(0, 80).Select(i => $$"""{"id":"e{{i}}","summary":"E{{i}}","start":"2026-10-25T10:00:00Z"}""")) + "]";
        Assert.Equal(CalendarEvents.MaxEvents, CalendarEvents.Parse(many, Zone).Count);
        Assert.Empty(CalendarEvents.Parse("You have no events today.", Zone));
        Assert.Empty(CalendarEvents.Parse("", Zone));
        Assert.Empty(CalendarEvents.Parse(null!, Zone));

        var longTitle = new string('t', 300);
        Assert.Equal(160, Assert.Single(CalendarEvents.Parse($$"""[{"summary":"{{longTitle}}","start":"2026-10-25T10:00:00Z"}]""", Zone)).Title.Length);
    }

    [Fact]
    public void NeverThrowsOnHostileInput()
    {
        const string lone = "\\ud800";
        Assert.Null(CalendarEvents.BuildArguments("{\"properties\":{\"" + lone + "\":{}}}", From, To));
        Assert.Null(CalendarEvents.BuildArguments("{\"properties\":{\"timeMin\":{}},\"required\":[\"" + lone + "\"]}", From, To));
        Assert.Null(CalendarEvents.BuildArguments("{not json", From, To));
        Assert.Null(CalendarEvents.BuildArguments("[]", From, To));
        Assert.NotNull(CalendarEvents.BuildArguments(null!, From, To));

        var parsed = CalendarEvents.Parse("[{\"summary\":\"" + lone + "\",\"start\":\"2026-10-25T10:00:00Z\"},"
            + "{\"summary\":\"Ok\",\"start\":\"2026-10-25T10:00:00Z\",\"htmlLink\":\"" + lone + "\"},"
            + "{\"summary\":\"Late\",\"start\":\"9999-12-31\"},{\"summary\":\"Min\",\"start\":\"0001-01-01\"}]",
            TimeZoneInfo.CreateCustomTimeZone("Far", TimeSpan.FromHours(14), "Far", "Far"));
        Assert.Equal(["Ok", "Late"], parsed.Select(e => e.Title)); // lone-surrogate title and out-of-range date are skipped
        Assert.Null(parsed[0].HtmlLink);

        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 20)) + """{"summary":"Deep","start":"2026-10-25T10:00:00Z"}""" + new string('}', 20);
        Assert.Empty(CalendarEvents.Parse(deep, Zone));
        Assert.Empty(CalendarEvents.Parse(new string('[', 200) + new string(']', 200), Zone));
        Assert.Empty(CalendarEvents.Parse("} [ { ] {", Zone));
    }
}
