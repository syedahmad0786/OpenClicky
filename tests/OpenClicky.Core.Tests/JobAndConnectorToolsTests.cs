using System.Net;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class JobAndConnectorToolsTests
{
    [Fact]
    public async Task WriteOutput_StaysInsideOutputFolderAndRespectsPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "openclicky-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new JobOutputTools(Path.Combine(root, "output"));
            var session = new DesktopToolSession(new Host(), AgentAccessMode.ConfirmEach, jobFiles: files);

            var saved = await Call(session, "write_output", new { path = "report/brief.md", content = "# Brief" });
            Assert.False(saved.IsError, saved.Content[0].Text);
            Assert.Equal("# Brief", File.ReadAllText(Path.Combine(root, "output", "report", "brief.md")));
            Assert.Contains("report/brief.md", (await Call(session, "list_outputs", new { })).Content[0].Text);

            foreach (var bad in new[] { "../escape.md", "C:\\x.md", "/abs.md", "run.ps1", "tool.EXE", "noext", "a/./b.md", "dot." })
                Assert.True((await Call(session, "write_output", new { path = bad, content = "x" })).IsError, bad);
            Assert.False(File.Exists(Path.Combine(root, "escape.md")));

            var preview = new DesktopToolSession(new Host(), AgentAccessMode.Preview, jobFiles: files);
            Assert.True((await Call(preview, "write_output", new { path = "p.md", content = "x" })).IsError);
            Assert.True((await Call(new DesktopToolSession(new Host(), AgentAccessMode.ConfirmEach), "write_output", new { path = "p.md", content = "x" })).IsError);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConnectorCall_ReadOnlyRunsDirectlyWritesNeedApproval()
    {
        var server = new FakeMcp();
        using var connector = new RemoteMcpConnector("mail", "Mail", new ConnectorSecret("https://mcp.example.test/mcp", "Authorization", "Bearer k"), server);
        var host = new Host();
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach, connectors: [connector]);

        var listed = await Call(session, "connector_tools", new { connector = "mail" });
        Assert.Contains("read_inbox", listed.Content[0].Text);
        Assert.Equal("Bearer k", server.LastAuthorization);

        var read = await Call(session, "connector_call", new { connector = "mail", tool = "read_inbox", arguments = "{}", rationale = "check mail" });
        Assert.False(read.IsError);
        Assert.Contains("3 unread", read.Content[0].Text);
        Assert.Equal(0, host.ConfirmCalls);

        host.ConfirmResult = false;
        var send = await Call(session, "connector_call", new { connector = "mail", tool = "send_email", arguments = "{\"to\":\"a@b.c\"}", rationale = "send it" });
        Assert.True(send.IsError);
        Assert.Equal(1, host.ConfirmCalls);
        Assert.Equal("connector_call", host.LastRequest!.ToolName);
        Assert.DoesNotContain("send_email", server.CalledTools);
        Assert.True(session.WasDeclined);
        Assert.True((await Call(session, "connector_call", new { connector = "mail", tool = "read_inbox", arguments = "{}", rationale = "x" })).IsError);

        Assert.True((await Call(new DesktopToolSession(new Host(), AgentAccessMode.ConfirmEach, connectors: [connector]),
            "connector_call", new { connector = "nope", tool = "read_inbox", arguments = "{}", rationale = "x" })).IsError);
        var preview = new DesktopToolSession(new Host(), AgentAccessMode.Preview, connectors: [connector]);
        Assert.True((await Call(preview, "connector_call", new { connector = "mail", tool = "send_email", arguments = "{}", rationale = "x" })).IsError);
    }

    [Fact]
    public void EventStream_ResponseIsMatchedById()
    {
        var body = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\"}\n\ndata: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}\n";
        Assert.Contains("\"id\":2", RemoteMcpConnector.FromEventStream(body, 2));
        Assert.Throws<InvalidDataException>(() => RemoteMcpConnector.FromEventStream(body, 3));
    }

    [Fact]
    public void Registry_KeepsEndpointInSecretStoreOnly()
    {
        var db = Path.Combine(Path.GetTempPath(), "openclicky-conn-" + Guid.NewGuid().ToString("N") + ".db");
        var secrets = new MemorySecrets();
        try
        {
            var registry = new ConnectorRegistry(secrets, db);
            var info = registry.Add("Work Gmail", "https://mcp.example.test/u/123?api_key=s3cret", "Authorization", "Bearer abc");
            Assert.Equal("work_gmail", info.Key);
            Assert.Equal("mcp.example.test", Assert.Single(registry.List()).Host);
            Assert.Equal("Bearer abc", registry.Secret(info.Key)!.HeaderValue);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.DoesNotContain("s3cret", Encoding.UTF8.GetString(File.ReadAllBytes(db)));

            Assert.Throws<ArgumentException>(() => registry.Add("Plain", "http://example.com/mcp", null, null));
            Assert.Throws<ArgumentException>(() => registry.Add("Half", "https://example.com/mcp", "Authorization", null));
            registry.Add("Local", "http://127.0.0.1:9000/mcp", null, null);

            registry.Remove(info.Key);
            Assert.Null(registry.Secret(info.Key));
            Assert.Single(registry.List());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(db)!, Path.GetFileName(db) + "*")) File.Delete(file);
            if (File.Exists(db)) File.Delete(db);
        }
    }

    private static Task<DesktopToolResult> Call(DesktopToolSession session, string tool, object arguments) =>
        session.CallAsync(tool, JsonSerializer.SerializeToElement(arguments), CancellationToken.None);

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new();
        public string? Get(string name) => _values.GetValueOrDefault(name);
        public void Set(string name, string value) => _values[name] = value;
        public void Delete(string name) => _values.Remove(name);
    }

    [Fact]
    public async Task ConnectorApprovalHoldsTheScreenAsTheAskingRun()
    {
        var server = new FakeMcp();
        using var connector = new RemoteMcpConnector("mail", "Mail", new ConnectorSecret("https://mcp.example.test/mcp", "Authorization", "Bearer k"), server);
        var lease = new OpenClicky.Core.Work.DesktopLease();
        var host = new Host { Lease = lease };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach, connectors: [connector], lease: lease, leaseHolder: "job-1");

        await Call(session, "connector_call", new { connector = "mail", tool = "send_email", arguments = "{}", rationale = "send it" });
        Assert.Equal(1, host.ConfirmCalls);
        Assert.Equal("job-1", host.HolderAtConfirm); // no other run's input can reach the approval dialog
        Assert.Null(lease.Holder);
        Assert.Contains("send_email", server.CalledTools);
    }

    private sealed class Host : IDesktopToolHost
    {
        public int ConfirmCalls;
        public bool ConfirmResult = true;
        public ActionRequest? LastRequest;
        public OpenClicky.Core.Work.DesktopLease? Lease;
        public string? HolderAtConfirm;
        public Task<DesktopToolObservation> ObserveAsync(bool includeScreens, bool includeUia, CancellationToken token) =>
            Task.FromResult(new DesktopToolObservation("obs", 1, "Test", null, []));
        public Task<DesktopToolContext> GetCurrentContextAsync(CancellationToken token) => Task.FromResult(new DesktopToolContext(1, "Test"));
        public Task<bool> ConfirmAsync(ActionRequest request, CancellationToken token)
        {
            ConfirmCalls++;
            LastRequest = request;
            HolderAtConfirm = Lease?.Holder;
            return Task.FromResult(ConfirmResult);
        }
        public Task<ActionResult> ExecuteAsync(ActionRequest request, bool confirmed, CancellationToken token) =>
            Task.FromResult(new ActionResult(true, "ok", false));
    }

    private sealed class FakeMcp : HttpMessageHandler
    {
        public string? LastAuthorization;
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
                "tools/list" => new
                {
                    tools = new object[]
                    {
                        new { name = "read_inbox", description = "Read unread mail", annotations = new { readOnlyHint = true }, inputSchema = new { type = "object" } },
                        new { name = "send_email", description = "Send mail", inputSchema = new { type = "object" } },
                    },
                },
                _ => Called(json.RootElement.GetProperty("params").GetProperty("name").GetString()!),
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id.GetInt32(), result }), Encoding.UTF8, "application/json"),
            };
            if (method == "initialize") response.Headers.Add("Mcp-Session-Id", "s1");
            return response;
        }

        private object Called(string name)
        {
            CalledTools.Add(name);
            return new { content = new[] { new { type = "text", text = "3 unread" } } };
        }
    }
}
