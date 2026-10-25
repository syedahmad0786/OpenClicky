using System.Net;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class McpCalendarSourceTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero), To = new(2026, 10, 25, 20, 0, 0, TimeSpan.Zero);
    private static readonly ConnectorSecret Secret = new("https://mcp.example.test/mcp", "Authorization", "Bearer k");

    private const string GoogleShape = """
        {"items":[
          {"id":"late","summary":"Dinner","start":{"dateTime":"2026-10-25T21:00:00+01:00"}},
          {"id":"a","summary":"Standup","start":{"dateTime":"2026-10-25T09:30:00+01:00"},"end":{"dateTime":"2026-10-25T09:45:00+01:00"},"hangoutLink":"https://meet.google.com/x","htmlLink":"https://calendar.google.com/e"},
          {"id":"early","summary":"Gym","start":{"dateTime":"2026-10-25T06:00:00Z"}},
          {"id":"b","summary":"Lunch","start":{"dateTime":"2026-10-25T08:00:00Z"}}]}
        """;

    [Fact]
    public async Task ListsAndCallsReadTool()
    {
        var server = new FakeCalendarMcp(GoogleShape);
        var source = new McpCalendarSource("cal", "Calendar", Secret, server);

        var events = await source.EventsAsync(From, To, CancellationToken.None);

        Assert.Equal(["b", "a"], events.Select(e => e.Id));
        var standup = events[1];
        Assert.Equal("Standup", standup.Title);
        Assert.Equal("https://meet.google.com/x", standup.MeetingUrl);
        Assert.Equal(["list_events"], server.CalledTools);
        Assert.DoesNotContain("create_event", server.CalledTools);
        Assert.DoesNotContain("get_event", server.CalledTools);
        using var args = JsonDocument.Parse(server.LastArguments!);
        Assert.Equal("2026-10-25T08:00:00Z", args.RootElement.GetProperty("timeMin").GetString());
        Assert.Equal("2026-10-25T20:00:00Z", args.RootElement.GetProperty("timeMax").GetString());
        Assert.Equal("Bearer k", server.LastAuthorization);
    }

    [Fact]
    public async Task ErrorsThrow()
    {
        var noTool = new FakeCalendarMcp(GoogleShape, tools: [Tool("create_event"), Tool("send_email")]);
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => new McpCalendarSource("cal", "Calendar", Secret, noTool).EventsAsync(From, To, CancellationToken.None));
        Assert.DoesNotContain("example.test", e.Message);
        Assert.Empty(noTool.CalledTools);

        var failing = new FakeCalendarMcp("token expired for https://mcp.example.test", isError: true);
        e = await Assert.ThrowsAsync<InvalidDataException>(() => new McpCalendarSource("cal", "Calendar", Secret, failing).EventsAsync(From, To, CancellationToken.None));
        Assert.DoesNotContain("example.test", e.Message);
        Assert.DoesNotContain("token", e.Message);

        var noHint = new FakeCalendarMcp(GoogleShape, tools: [Tool("create_event"), Tool("list_events")]);
        e = await Assert.ThrowsAsync<InvalidDataException>(() => new McpCalendarSource("cal", "Calendar", Secret, noHint).EventsAsync(From, To, CancellationToken.None));
        Assert.Equal("no read-only calendar tool", e.Message);
        Assert.Empty(noHint.CalledTools);

        var unfillable = new FakeCalendarMcp(GoogleShape, tools: [Tool("search_events", """{"properties":{"query":{}},"required":["query"]}""", readOnly: true)]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new McpCalendarSource("cal", "Calendar", Secret, unfillable).EventsAsync(From, To, CancellationToken.None));
        Assert.Empty(unfillable.CalledTools);
    }

    private static object Tool(string name, string? schema = null, bool readOnly = false) => new
    {
        name,
        description = name,
        annotations = new { readOnlyHint = readOnly },
        inputSchema = JsonDocument.Parse(schema ?? """{"type":"object","properties":{"timeMin":{"type":"string"},"timeMax":{"type":"string"},"maxResults":{"type":"integer"}}}""").RootElement,
    };

    private sealed class FakeCalendarMcp(string callText, bool isError = false, object[]? tools = null) : HttpMessageHandler
    {
        private readonly object[] _tools = tools ??
        [
            Tool("create_event", """{"properties":{"summary":{}},"required":["summary"]}"""),
            Tool("get_event", """{"properties":{"eventId":{}},"required":["eventId"]}""", readOnly: true),
            Tool("list_events", readOnly: true),
        ];
        public string? LastAuthorization, LastArguments;
        public List<string> CalledTools { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var method = json.RootElement.GetProperty("method").GetString();
            if (!json.RootElement.TryGetProperty("id", out var id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            object result = method switch
            {
                "initialize" => new { protocolVersion = "2025-06-18", capabilities = new { }, serverInfo = new { name = "fake" } },
                "tools/list" => new { tools = _tools },
                _ => Called(json.RootElement.GetProperty("params")),
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.GetInt32(), result }), Encoding.UTF8, "application/json"),
            };
        }

        private object Called(JsonElement parameters)
        {
            CalledTools.Add(parameters.GetProperty("name").GetString()!);
            LastArguments = parameters.GetProperty("arguments").GetRawText();
            return new { isError, content = new[] { new { type = "text", text = callText } } };
        }
    }
}
