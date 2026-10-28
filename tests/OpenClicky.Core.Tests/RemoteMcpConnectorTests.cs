using System.Net;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class RemoteMcpConnectorTests
{
    [Fact]
    public async Task SendsAllHeadersAndBearer()
    {
        var server = new Recorder();
        var bearerCalls = 0;
        var secret = new ConnectorSecret("https://mcp.example.test/mcp", "X-Api-Key", "k1")
        {
            Headers = new Dictionary<string, string> { ["X-Team"] = "design" },
        };
        using (var connector = new RemoteMcpConnector("a", "A", secret, server, _ => { bearerCalls++; return Task.FromResult("tok"); }))
            Assert.Equal(2, (await connector.ListToolsAsync(CancellationToken.None)).Count);

        Assert.Equal(["initialize", "notifications/initialized", "tools/list"], server.Requests.Select(r => r.Method));
        foreach (var request in server.Requests)
        {
            Assert.Equal("design", request.Headers["X-Team"]);
            Assert.Equal("k1", request.Headers["X-Api-Key"]);
            Assert.Equal("Bearer tok", request.Headers["Authorization"]);
        }
        Assert.Equal(3, bearerCalls);

        // A bearer token replaces an API-key Authorization header instead of sending both.
        var replaced = new Recorder();
        using (var connector = new RemoteMcpConnector("b", "B", new ConnectorSecret("https://mcp.example.test/mcp", "Authorization", "Bearer old"), replaced,
            _ => Task.FromResult("new")))
            await connector.ListToolsAsync(CancellationToken.None);
        Assert.All(replaced.Requests, r => Assert.Equal("Bearer new", r.Headers["Authorization"]));

        // Without a bearer the API-key Authorization header is sent as typed.
        var plain = new Recorder();
        using (var connector = new RemoteMcpConnector("c", "C", new ConnectorSecret("https://mcp.example.test/mcp", "Authorization", "Bearer k"), plain))
            await connector.ListToolsAsync(CancellationToken.None);
        Assert.All(plain.Requests, r => Assert.Equal("Bearer k", r.Headers["Authorization"]));
    }

    [Fact]
    public async Task UnauthorizedThrowsStatus()
    {
        var server = new Recorder { Status = HttpStatusCode.Unauthorized };
        using var connector = new RemoteMcpConnector("a", "A", new ConnectorSecret("https://mcp.example.test/mcp", null, null), server);
        var error = await Assert.ThrowsAsync<McpHttpException>(() => connector.ListToolsAsync(CancellationToken.None));
        Assert.Equal(401, error.StatusCode);
        Assert.Equal("initialize", Assert.Single(server.Requests).Method);
        Assert.DoesNotContain("mcp.example.test", error.Message);
    }

    [Fact]
    public void BridgeNamesAccountLabelsAndMultiAccountRule()
    {
        using var work = new RemoteMcpConnector("gmail", "Gmail", new ConnectorSecret("https://mcp.example.test/mcp", null, null), new Recorder()) { Label = "Work" };
        using var other = new RemoteMcpConnector("notes", "Notes", new ConnectorSecret("https://mcp.example.test/mcp", null, null), new Recorder());
        var session = new DesktopToolSession(new NoHost(), AgentAccessMode.ConfirmEach, connectors: [work, other]);
        var json = JsonSerializer.Serialize(McpDesktopBridge.ToolDefinitions(session));
        using var tools = JsonDocument.Parse(json);
        var description = tools.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "connector_tools").GetProperty("description").GetString()!;
        Assert.Contains("gmail (Gmail · Work)", description);
        Assert.Contains("notes (Notes)", description);
        Assert.EndsWith("Some apps have more than one account. When reading, search every account of that app unless the user named one, and say which account each result came from. To change an existing item, use the account it came from. To create or send something, use the account the user named or the one the context makes clear; if it is still unclear, ask the user once.", description);
    }

    private sealed record Seen(string Method, Dictionary<string, string> Headers);

    private sealed class Recorder : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var method = json.RootElement.GetProperty("method").GetString()!;
            Requests.Add(new(method, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
            if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status);
            if (!json.RootElement.TryGetProperty("id", out var id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            object result = method == "initialize"
                ? new { protocolVersion = "2025-06-18", capabilities = new { } }
                : new { tools = new object[] { new { name = "one", inputSchema = new { type = "object" } }, new { name = "two" } } };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.GetInt32(), result }), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class NoHost : IDesktopToolHost
    {
        public Task<DesktopToolObservation> ObserveAsync(bool includeScreens, bool includeUia, CancellationToken token) => throw new NotSupportedException();
        public Task<DesktopToolContext> GetCurrentContextAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(ActionRequest request, CancellationToken token) => Task.FromResult(false);
        public Task<ActionResult> ExecuteAsync(ActionRequest request, bool confirmed, CancellationToken token) => throw new NotSupportedException();
    }
}
