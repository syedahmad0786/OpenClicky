using System.Text;
using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public sealed class McpDesktopBridgeTests
{
    [Fact]
    public async Task CancelledTaskCannotObserveOrExecuteThroughLiveTransport()
    {
        var host = new Host();
        using var stop = new CancellationTokenSource();
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach, taskToken: stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default));
        Assert.Equal(0, host.ObserveCalls);
        Assert.Equal(0, host.ExecuteCalls);
    }

    [Fact]
    public async Task TransportRejectsBadHostAuthLargeAndMalformedRequests()
    {
        var bridge = CreateBridge(out _);
        Assert.Equal(403, (await bridge.ProcessAsync("evil.test", "Bearer test-token", null, Json("{}"))).StatusCode);
        Assert.Equal(401, (await bridge.ProcessAsync("127.0.0.1:3210", "Bearer wrong", null, Json("{}"))).StatusCode);
        Assert.Equal(413, (await bridge.ProcessAsync("127.0.0.1", "Bearer test-token", null, new byte[McpDesktopBridge.MaxBodyBytes + 1])).StatusCode);
        Assert.Contains("-32700", (await bridge.ProcessAsync("127.0.0.1", "Bearer test-token", null, Json("{"))).Body);
    }

    [Fact]
    public async Task LifecycleListsOnlyBoundedToolsAndStopsUnknownOperations()
    {
        var bridge = CreateBridge(out _);
        var initialized = await InitializeAsync(bridge);
        Assert.Equal("2025-06-18", initialized.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

        var listed = await CallAsync(bridge, 2, "tools/list");
        var names = listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).ToArray();
        Assert.Equal(DesktopToolSession.ToolNames, names);
        Assert.DoesNotContain(names, name => name is "shell" or "command" or "run_command");

        var unknown = await CallAsync(bridge, 3, "resources/list");
        Assert.Equal(-32601, unknown.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task BrowserToolsAreAdvertisedOnlyWhenSessionIsSuppliedAndDispatchSeparately()
    {
        var desktop = new Host();
        var browserHost = new BrowserHost();
        var browserBroker = new BrowserBroker();
        var browser = new ChromeBridgeSession(browserHost, browserBroker, AgentAccessMode.ConfirmEach, ["https://allowed.example/"]);
        var bridge = new McpDesktopBridge(new DesktopToolSession(desktop, AgentAccessMode.ConfirmEach, browser: browser), "test-token");
        await InitializeAsync(bridge);

        var listed = await CallAsync(bridge, 2, "tools/list");
        var names = listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).ToArray();
        Assert.Contains("openclicky_browser_observe", names);
        Assert.Contains("openclicky_browser_follow_link", names);

        var observed = await ToolAsync(bridge, 3, "openclicky_browser_observe", new { });
        using var metadata = JsonDocument.Parse(observed.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        var action = await ToolAsync(bridge, 4, "openclicky_browser_follow_link", new
        {
            observation_id = metadata.RootElement.GetProperty("observation_id").GetString(),
            navigation_revision = metadata.RootElement.GetProperty("navigation_revision").GetInt64().ToString(),
            link_id = "e1",
            rationale = "Open observed help link",
        });

        Assert.False(action.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(1, browserBroker.Calls);
        Assert.Equal("browser_follow_link", browserHost.LastAction?.Name);
        Assert.Equal(0, desktop.ObserveCalls);
        Assert.Equal(0, desktop.ExecuteCalls);
    }

    [Fact]
    public async Task MutationsRequireFreshObservationAndReturnPostActionProof()
    {
        var host = new Host();
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        var bridge = new McpDesktopBridge(session, "test-token");
        await InitializeAsync(bridge);
        var observed = await ToolAsync(bridge, 2, "observe_screen", new { });
        var metadata = JsonDocument.Parse(observed.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        var id = metadata.RootElement.GetProperty("observation_id").GetString();
        var revision = metadata.RootElement.GetProperty("observation_revision").GetInt64().ToString();

        var stale = await ToolAsync(bridge, 3, "click", new { x = "1", y = "2", observation_id = "old", observation_revision = revision });
        Assert.True(stale.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(0, host.ExecuteCalls);

        var action = await ToolAsync(bridge, 4, "click", new { x = "1", y = "2", observation_id = id, observation_revision = revision, rationale = "Click target" });
        var result = action.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Contains(result.GetProperty("content").EnumerateArray(), item => item.GetProperty("type").GetString() == "image");
        Assert.Equal(1, host.ConfirmCalls);
        Assert.Equal(1, host.ExecuteCalls);
        Assert.True(host.LastConfirmed);
        Assert.Equal(2, host.ObserveCalls);
    }

    [Fact]
    public async Task PreviewAndTaskModeSnapshotCannotSilentlyWiden()
    {
        var host = new Host();
        var session = new DesktopToolSession(host, AgentAccessMode.Preview);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);
        var action = JsonDocument.Parse("{\"observation_id\":\"obs-1\",\"observation_revision\":\"1\",\"x\":\"1\",\"y\":\"2\"}").RootElement;

        host.RequestedMode = AgentAccessMode.FullAccess;
        var result = await session.CallAsync("click", action, default);
        var observedAgain = await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        Assert.True(result.IsError);
        Assert.False(observedAgain.IsError);
        Assert.Equal(AgentAccessMode.Preview, session.AccessSnapshot);
        Assert.Equal(0, host.ConfirmCalls);
        Assert.Equal(0, host.ExecuteCalls);
    }

    [Fact]
    public async Task RevisionChangeAfterConfirmationStopsExecution()
    {
        var host = new Host();
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        var bridge = new McpDesktopBridge(session, "test-token");
        host.ChangeRevisionOnConfirm = true;
        await InitializeAsync(bridge);
        await ToolAsync(bridge, 2, "observe_screen", new { });

        var result = await ToolAsync(bridge, 3, "click", new { x = "1", y = "2", observation_id = "obs-1", observation_revision = "1", rationale = "Click target" });

        Assert.True(result.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(0, host.ExecuteCalls);
        Assert.True(session.HasFailedApprovedAction);
    }

    [Fact]
    public async Task DeclinedApprovalEndsSessionAndCannotBeRetried()
    {
        var host = new Host { ConfirmResult = false };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);
        var action = JsonDocument.Parse("{\"observation_id\":\"obs-1\",\"observation_revision\":\"1\",\"x\":\"1\",\"y\":\"2\"}").RootElement;

        var declined = await session.CallAsync("click", action, default);
        host.ConfirmResult = true;
        var observeAgain = await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);
        var retry = await session.CallAsync("click", action, default);

        Assert.True(declined.IsError);
        Assert.Contains("declined", declined.Content[0].Text);
        Assert.True(observeAgain.IsError);
        Assert.True(retry.IsError);
        Assert.Contains("Start a new task", retry.Content[0].Text);
        Assert.Equal(1, host.ConfirmCalls);
        Assert.Equal(0, host.ExecuteCalls);
        Assert.Equal(1, host.ObserveCalls);
        Assert.True(session.WasDeclined);
    }

    [Fact]
    public async Task OpenAppExecutesWhenContextIsUnavailableAfterApproval()
    {
        var host = new Host
        {
            ListAppsResult = "[{\"app_id\":\"B08B08B08B08B08B\",\"name\":\"Calculator\"}]",
            ThrowGetContextAfterConfirmation = true,
        };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        await session.CallAsync("list_apps", JsonSerializer.SerializeToElement(new { query = "Calculator" }), default);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        var result = await session.CallAsync("open_app", OpenApp("B08B08B08B08B08B", "obs-1", 1), default);

        Assert.False(result.IsError);
        Assert.Equal(1, host.ConfirmCalls);
        Assert.Equal(2, host.ExecuteCalls);
        Assert.False(session.HasFailedApprovedAction);
    }

    [Fact]
    public async Task InputRefusesAndSticksWhenContextIsUnavailableAfterApproval()
    {
        var host = new Host { ThrowGetContextAfterConfirmation = true };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.CallAsync("click",
            JsonDocument.Parse("{\"observation_id\":\"obs-1\",\"observation_revision\":\"1\",\"x\":\"1\",\"y\":\"2\"}").RootElement, default));

        Assert.Equal(0, host.ExecuteCalls);
        Assert.True(session.HasFailedApprovedAction);
    }

    [Fact]
    public async Task ExecutorExceptionAfterApprovalSticksFailure()
    {
        var host = new Host { ThrowExecute = true };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.CallAsync("click",
            JsonDocument.Parse("{\"observation_id\":\"obs-1\",\"observation_revision\":\"1\",\"x\":\"1\",\"y\":\"2\"}").RootElement, default));

        Assert.True(session.HasFailedApprovedAction);
    }

    [Fact]
    public async Task PostActionObservationExceptionSticksFailure()
    {
        var host = new Host { ThrowObserveAfterExecution = true };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.CallAsync("click",
            JsonDocument.Parse("{\"observation_id\":\"obs-1\",\"observation_revision\":\"1\",\"x\":\"1\",\"y\":\"2\"}").RootElement, default));

        Assert.Equal(1, host.ExecuteCalls);
        Assert.True(session.HasFailedApprovedAction);
    }

    [Fact]
    public async Task DeniedForegroundStopsObservationBeforeHostRead()
    {
        var host = new Host { CurrentTitle = "Credential Manager" };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach, ["Credential Manager"]);

        var result = await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        Assert.True(result.IsError);
        Assert.Equal(0, host.ObserveCalls);
        Assert.Equal(0, host.ExecuteCalls);
    }

    [Fact]
    public async Task SuccessfulDifferentAppDoesNotClearEarlierUnverifiedLaunch()
    {
        var host = new Host { ListAppsResult = "[{\"app_id\":\"AAAAAAAAAAAAAAAA\",\"name\":\"App A\"},{\"app_id\":\"BBBBBBBBBBBBBBBB\",\"name\":\"App B\"}]" };
        host.ExecuteResults.Enqueue(false);
        host.ExecuteResults.Enqueue(true);
        host.ExecuteResults.Enqueue(true);
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        await session.CallAsync("list_apps", JsonSerializer.SerializeToElement(new { query = "App" }), default);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        var failedA = await session.CallAsync("open_app", OpenApp("AAAAAAAAAAAAAAAA", "obs-1", 1), default);
        var successfulB = await session.CallAsync("open_app", OpenApp("BBBBBBBBBBBBBBBB", "obs-2", 1), default);

        Assert.True(failedA.IsError);
        Assert.True(session.HasFailedApprovedAction);
        Assert.False(successfulB.IsError);
        Assert.True(session.HasUnverifiedAppLaunch);

        var retriedA = await session.CallAsync("open_app", OpenApp("AAAAAAAAAAAAAAAA", "obs-3", 1), default);
        Assert.False(retriedA.IsError);
        Assert.False(session.HasUnverifiedAppLaunch);
    }

    [Fact]
    public async Task OpenAppApprovalReceivesTrustedCatalogName()
    {
        var host = new Host { ListAppsResult = "[{\"app_id\":\"B08B08B08B08B08B\",\"name\":\"Calculator\"}]" };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        await session.CallAsync("list_apps", JsonSerializer.SerializeToElement(new { query = "Calculator" }), default);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        var result = await session.CallAsync("open_app", OpenApp("B08B08B08B08B08B", "obs-1", 1), default);

        Assert.False(result.IsError);
        Assert.Equal("Calculator", host.LastConfirmationRequest?.TrustedTargetName);
    }

    [Fact]
    public async Task AutomaticPostActionCaptureReportsOnlyConciseDestination()
    {
        var host = new Host();
        var updates = new List<string>();
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach, onDestinationObserved: updates.Add);
        await session.CallAsync("observe_screen", JsonDocument.Parse("{}").RootElement, default);

        var result = await session.CallAsync("click",
            JsonDocument.Parse("{\"observation_id\":\"obs-1\",\"observation_revision\":\"1\",\"x\":\"1\",\"y\":\"2\",\"rationale\":\"Click target\"}").RootElement,
            default);

        Assert.False(result.IsError);
        Assert.Equal(["Observed destination: Test"], updates);
        Assert.DoesNotContain("button", updates[0]);
    }

    [Fact]
    public async Task MemorySearchValidatesBeforeHostAndRejectsPathArguments()
    {
        var host = new Host();
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);

        var tooLong = await session.CallAsync("search_memory",
            JsonSerializer.SerializeToElement(new { query = new string('x', 201) }), default);
        var withPath = await session.CallAsync("search_memory",
            JsonSerializer.SerializeToElement(new { query = "needle", path = "other.db" }), default);

        Assert.True(tooLong.IsError);
        Assert.True(withPath.IsError);
        Assert.Equal(0, host.MemorySearchCalls);
    }

    [Fact]
    public async Task MemorySearchReturnsBoundedSourceReferencesFromInjectedStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-memory-tool-{Guid.NewGuid():N}.json");
        try
        {
            var note = LocalNotesStore.Add("Project fact", "unique memory needle", path);
            var host = new Host { MemoryPath = path };
            var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);

            var result = await session.CallAsync("search_memory",
                JsonSerializer.SerializeToElement(new { query = "memory needle" }), default);

            Assert.False(result.IsError);
            using var document = JsonDocument.Parse(Assert.Single(result.Content).Text!);
            var match = Assert.Single(document.RootElement.GetProperty("matches").EnumerateArray());
            Assert.Equal("note", match.GetProperty("source").GetProperty("kind").GetString());
            Assert.Equal(note.Id, match.GetProperty("source").GetProperty("record_id").GetString());
            Assert.Contains("untrusted data", document.RootElement.GetProperty("notice").GetString());
            Assert.Equal(10, host.LastMemoryLimit);
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, ".db"));
        }
    }

    [Fact]
    public async Task MemorySearchCancellationReachesHost()
    {
        var host = new Host { BlockMemorySearch = true, MemoryPath = "unused" };
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach);
        using var stop = new CancellationTokenSource();
        var pending = session.CallAsync("search_memory", JsonSerializer.SerializeToElement(new { query = "needle" }), stop.Token);
        await host.MemorySearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, host.MemorySearchCalls);
    }

    [Fact]
    public async Task CancelNotificationCancelsInflightTool()
    {
        var bridge = CreateBridge(out var host);
        host.BlockExecution = true;
        await InitializeAsync(bridge);
        var call = ToolAsync(bridge, 7, "wait", new { ms = "30000" });
        await host.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var cancelled = await bridge.ProcessAsync("127.0.0.1", "Bearer test-token", bridge.SessionId,
            Json("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":7}}"));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(202, cancelled.StatusCode);
        Assert.Equal(-32800, result.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    private static McpDesktopBridge CreateBridge(out Host host)
    {
        host = new Host();
        return new(new DesktopToolSession(host, AgentAccessMode.ConfirmEach), "test-token");
    }

    private static async Task<JsonDocument> InitializeAsync(McpDesktopBridge bridge)
    {
        var response = await bridge.ProcessAsync("127.0.0.1:3210", "Bearer test-token", null,
            Json("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}"));
        var initialized = await bridge.ProcessAsync("127.0.0.1:3210", "Bearer test-token", bridge.SessionId,
            Json("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        Assert.Equal(202, initialized.StatusCode);
        return JsonDocument.Parse(response.Body!);
    }

    private static async Task<JsonDocument> CallAsync(McpDesktopBridge bridge, int id, string method)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id, method, @params = new { } });
        var response = await bridge.ProcessAsync("127.0.0.1", "Bearer test-token", bridge.SessionId, body);
        return JsonDocument.Parse(response.Body!);
    }

    private static async Task<JsonDocument> ToolAsync(McpDesktopBridge bridge, int id, string name, object arguments)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments } });
        var response = await bridge.ProcessAsync("127.0.0.1", "Bearer test-token", bridge.SessionId, body);
        return JsonDocument.Parse(response.Body!);
    }

    private static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);

    private static JsonElement OpenApp(string appId, string observationId, long revision) =>
        JsonSerializer.SerializeToElement(new
        {
            app_id = appId,
            observation_id = observationId,
            observation_revision = revision,
            rationale = "Open the selected installed app",
        });

    private sealed class Host : IDesktopToolHost
    {
        public int ObserveCalls, ConfirmCalls, ExecuteCalls, MemorySearchCalls, LastMemoryLimit;
        public bool LastConfirmed;
        public bool ChangeRevisionOnConfirm, BlockExecution;
        public bool ThrowGetContextAfterConfirmation, ThrowExecute, ThrowObserveAfterExecution;
        public bool ConfirmResult = true;
        public Queue<bool> ExecuteResults { get; } = new();
        public string? ListAppsResult;
        public ActionRequest? LastConfirmationRequest;
        public string? MemoryPath;
        public bool BlockMemorySearch;
        public TaskCompletionSource MemorySearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Revision = 1;
        public string CurrentTitle = "Test";
        public AgentAccessMode RequestedMode = AgentAccessMode.ConfirmEach;
        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DesktopToolObservation> ObserveAsync(bool includeScreens, bool includeUia, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ThrowObserveAfterExecution && ExecuteCalls > 0) throw new InvalidOperationException("Post-action observation unavailable.");
            ObserveCalls++;
            IReadOnlyList<ScreenCaptureResult> screens = includeScreens
                ? [new([0xff, 0xd8], 10, 10, 0, 0, 10, 10, true, "test")]
                : [];
            return Task.FromResult(new DesktopToolObservation($"obs-{ObserveCalls}", Revision, CurrentTitle, includeUia ? "button" : null, screens));
        }

        public Task<DesktopToolContext> GetCurrentContextAsync(CancellationToken token)
        {
            if (ThrowGetContextAfterConfirmation && ConfirmCalls > 0) throw new InvalidOperationException("Home has foreground focus.");
            return Task.FromResult(new DesktopToolContext(Revision, CurrentTitle));
        }

        public async Task<MemorySearchResponse> SearchMemoryAsync(string query, int limit, CancellationToken token)
        {
            MemorySearchCalls++;
            LastMemoryLimit = limit;
            MemorySearchStarted.TrySetResult();
            if (BlockMemorySearch) await Task.Delay(Timeout.Infinite, token);
            return MemoryPath is null
                ? MemorySearchResponse.Unavailable
                : new MemorySearchResponse(true, LocalSearchStore.Search(query, MemoryPath, limit));
        }

        public Task<bool> ConfirmAsync(ActionRequest request, CancellationToken token)
        {
            ConfirmCalls++;
            LastConfirmationRequest = request;
            if (ChangeRevisionOnConfirm) Revision++;
            return Task.FromResult(ConfirmResult);
        }

        public async Task<ActionResult> ExecuteAsync(ActionRequest request, bool confirmed, CancellationToken token)
        {
            ExecuteCalls++;
            if (ThrowExecute) throw new InvalidOperationException("Executor failed.");
            LastConfirmed = confirmed;
            ExecutionStarted.TrySetResult();
            if (BlockExecution) await Task.Delay(Timeout.Infinite, token);
            if (request.ToolName == "list_apps" && ListAppsResult is not null)
                return new(true, ListAppsResult, false);
            var success = ExecuteResults.Count == 0 || ExecuteResults.Dequeue();
            return new(success, success ? "performed and verified" : "destination could not be verified", false);
        }
    }

    private sealed class BrowserBroker : IChromeActionBroker
    {
        public int Calls;
        public Task<bool> ApproveAsync(ActionRequest request, CancellationToken token) { Calls++; return Task.FromResult(true); }
    }

    private sealed class BrowserHost : IChromeBridgeHost
    {
        private static readonly BrowserContext Current = new("4", "0", 7, "https://allowed.example/", "Fixture");
        public BrowserAction? LastAction;
        public Task<BrowserContext> GetCurrentContextAsync(CancellationToken token) => Task.FromResult(Current);
        public Task<BrowserObservation> ObserveAsync(CancellationToken token) => Task.FromResult(new BrowserObservation(
            "browser-observation", Current, "[]", new Dictionary<string, string> { ["e1"] = "https://allowed.example/help" }));
        public Task<BrowserActionResult> ExecuteAsync(BrowserAction action, CancellationToken token)
        { LastAction = action; return Task.FromResult(new BrowserActionResult(true, "done")); }
    }
}
