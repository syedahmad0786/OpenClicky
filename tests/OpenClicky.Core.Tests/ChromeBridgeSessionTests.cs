using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public sealed class ChromeBridgeSessionTests
{
    private static readonly BrowserTaskIdentity Identity = new("task-1", "job-7", "Research trip", "green");

    [Fact]
    public async Task PreviewRefusesOpen()
    {
        var (session, bridge, broker) = Create(AgentAccessMode.Preview);

        var snapshot = await session.CallAsync("browser", Args(new { op = "snapshot" }), default);
        var open = await session.CallAsync("browser", Args(new { op = "open", url = "https://example.com/", rationale = "Look it up" }), default);
        var save = await session.CallAsync("browser", Args(new { op = "screenshot", save = true }), default);

        Assert.False(snapshot.IsError);
        Assert.True(open.IsError);
        Assert.Equal("Preview mode: the browser is read-only for this job.", open.Content[0].Text);
        Assert.True(save.IsError);
        Assert.Equal(["snapshot"], bridge.Commands.Select(c => c.Op));
        Assert.Equal(0, broker.Calls);
    }

    [Fact]
    public async Task ConfirmEachAsksForClickNotForSnapshot()
    {
        var (session, bridge, broker) = Create(AgentAccessMode.ConfirmEach);

        await session.CallAsync("browser", Args(new { op = "snapshot" }), default);
        await session.CallAsync("browser", Args(new { op = "close", tabId = 3 }), default);
        Assert.Equal(0, broker.Calls);
        var click = await session.CallAsync("browser", Args(new { op = "click", tabId = 3, @ref = "e4", rationale = "Open the menu" }), default);

        Assert.False(click.IsError);
        Assert.Equal(1, broker.Calls);
        Assert.Equal("browser.click", broker.Last!.ToolName);
        Assert.Equal("Open the menu", broker.Last.Rationale);
        Assert.Equal("e4", broker.Last.Arguments["ref"]);
        Assert.Equal("3", broker.Last.Arguments["tabId"]);
        Assert.Equal(["snapshot", "close", "click"], bridge.Commands.Select(c => c.Op));
    }

    [Fact]
    public async Task ConfirmEachAsksOncePerBatch()
    {
        var (session, bridge, broker) = Create(AgentAccessMode.ConfirmEach);

        var result = await session.CallAsync("browser", Args(new
        {
            op = "batch", rationale = "Search for cats",
            steps = new object[] { new { op = "fill", @ref = "e3", text = "cats" }, new { op = "press", key = "Enter" } },
        }), default);

        Assert.False(result.IsError);
        Assert.Equal(1, broker.Calls);
        Assert.Equal("browser.batch", broker.Last!.ToolName);
        Assert.Contains(broker.Last.Arguments.Values, v => v.Contains("fill") && v.Contains("cats"));
        Assert.Contains(broker.Last.Arguments.Values, v => v.Contains("press") && v.Contains("Enter"));
        Assert.Equal(TimeSpan.FromSeconds(45), bridge.Timeouts.Single());
    }

    [Fact]
    public async Task AutoAlwaysAsksForJavascriptAndUpload()
    {
        var file = Path.Combine(Path.GetTempPath(), "openclicky-session-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "hello");
        try
        {
            var (session, _, broker) = Create(AgentAccessMode.Auto);

            await session.CallAsync("browser", Args(new { op = "scroll", direction = "down" }), default);
            await session.CallAsync("browser", Args(new { op = "back", rationale = "Return to the list" }), default);
            Assert.Equal(0, broker.Calls);
            await session.CallAsync("browser", Args(new { op = "javascript", code = "document.title", rationale = "Read the title" }), default);
            await session.CallAsync("browser", Args(new { op = "upload", @ref = "e2", paths = new[] { file }, rationale = "Attach the notes" }), default);
            await session.CallAsync("browser", Args(new
            {
                op = "batch", rationale = "Attach the notes",
                steps = new object[] { new { op = "upload", @ref = "e2", paths = new[] { file } } },
            }), default);

            Assert.Equal(["browser.javascript", "browser.upload", "browser.batch"], broker.Requests.Select(r => r.ToolName));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task UploadPolicyRunsBeforeAsking()
    {
        var (session, bridge, broker) = Create(AgentAccessMode.FullAccess);

        var result = await session.CallAsync("browser", Args(new
        {
            op = "batch", rationale = "Attach",
            steps = new object[] { new { op = "upload", @ref = "e2", paths = new[] { "relative.txt" } } },
        }), default);

        Assert.True(result.IsError);
        Assert.Empty(bridge.Commands);
        Assert.Equal(0, broker.Calls);
    }

    [Fact]
    public async Task FullAsksNothing()
    {
        var (session, bridge, broker) = Create(AgentAccessMode.FullAccess);

        await session.CallAsync("browser", Args(new { op = "click", @ref = "e4", rationale = "Submit the form" }), default);
        await session.CallAsync("browser", Args(new { op = "javascript", code = "1", rationale = "Check" }), default);

        Assert.Equal(0, broker.Calls);
        Assert.Equal(2, bridge.Commands.Count);
    }

    [Fact]
    public async Task DeclineEndsBrowserWork()
    {
        var (session, bridge, broker) = Create(AgentAccessMode.ConfirmEach, approve: false);

        var declined = await session.CallAsync("browser", Args(new { op = "click", @ref = "e4", rationale = "Open" }), default);
        var after = await session.CallAsync("browser", Args(new { op = "snapshot" }), default);

        Assert.True(declined.IsError);
        Assert.Equal("You declined that browser action.", declined.Content[0].Text);
        Assert.True(session.WasDeclined);
        Assert.True(after.IsError);
        Assert.Equal("This job's browser work ended when you declined an action.", after.Content[0].Text);
        Assert.Empty(bridge.Commands);
    }

    [Fact]
    public async Task IdentityDenyAndSayReachTheCommand()
    {
        var (session, bridge, _) = Create(AgentAccessMode.FullAccess);

        await session.CallAsync("browser", Args(new { op = "open", url = "https://example.com/", say = "Opening the docs", rationale = "Read the docs" }), default);
        await session.CallAsync("browser", Args(new { op = "wait_for", text = "Done", timeout = 5 }), default);

        var command = bridge.Commands[0];
        Assert.Equal(("open", "task-1", "job-7", "Research trip", "green"), (command.Op, command.Task, command.Owner, command.GroupTitle, command.Color));
        Assert.Equal(["paypal", "my-bank"], command.Deny);
        Assert.Equal("Opening the docs", command.Say);
        Assert.Equal("https://example.com/", command.Args.GetProperty("url").GetString());
        Assert.False(command.Args.TryGetProperty("op", out _));
        Assert.False(command.Args.TryGetProperty("rationale", out _));
        Assert.False(command.Args.TryGetProperty("say", out _));
        Assert.Equal([TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(20)], bridge.Timeouts);
    }

    [Fact]
    public async Task ScreenshotImageAndSave()
    {
        var folder = Path.Combine(Path.GetTempPath(), "openclicky-shots-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
            var (session, _, _) = Create(AgentAccessMode.ConfirmEach, outputFolder: folder,
                reply: _ => new BrowserReply(true, "Screenshot of the visible page.", Convert.ToBase64String(bytes), "image/jpeg"));

            var plain = await session.CallAsync("browser", Args(new { op = "screenshot" }), default);
            var saved = await session.CallAsync("browser", Args(new { op = "screenshot", save = true }), default);

            Assert.False(plain.IsError);
            Assert.Equal(new DesktopToolContent("image", null, Convert.ToBase64String(bytes), "image/jpeg"), plain.Content[1]);
            var file = Assert.Single(Directory.GetFiles(folder));
            Assert.Matches(@"^screenshot-\d{8}-\d{6}\.jpg$", Path.GetFileName(file));
            Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.Contains($"Saved to the job's output folder as {Path.GetFileName(file)}.", saved.Content[0].Text);

            var (noFolder, _, _) = Create(AgentAccessMode.ConfirmEach,
                reply: _ => new BrowserReply(true, "Screenshot.", Convert.ToBase64String(bytes), "image/jpeg"));
            var unsaved = await noFolder.CallAsync("browser", Args(new { op = "screenshot", save = true }), default);
            Assert.Contains("This job has no output folder, so the screenshot was not saved.", unsaved.Content[0].Text);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task FailedActionMarksFailure()
    {
        var (session, _, _) = Create(AgentAccessMode.FullAccess, reply: c => new BrowserReply(false, "That element is gone. Take a new snapshot."));

        var read = await session.CallAsync("browser", Args(new { op = "snapshot" }), default);
        Assert.True(read.IsError);
        Assert.False(session.HasFailedApprovedAction);
        var click = await session.CallAsync("browser", Args(new { op = "click", @ref = "e4", rationale = "Open" }), default);

        Assert.True(click.IsError);
        Assert.Equal("That element is gone. Take a new snapshot.", click.Content[0].Text);
        Assert.True(session.HasFailedApprovedAction);
    }

    [Fact]
    public async Task ModelCannotSendLifecycleOps()
    {
        var (session, bridge, _) = Create(AgentAccessMode.FullAccess);

        var end = await session.CallAsync("browser", Args(new { op = "end", tidy = true }), default);
        var sweep = await session.CallAsync("browser", Args(new { op = "sweep" }), default);
        var unknown = await session.CallAsync("browser_click", Args(new { op = "click" }), default);

        Assert.True(end.IsError);
        Assert.True(sweep.IsError);
        Assert.True(unknown.IsError);
        Assert.Empty(bridge.Commands);
    }

    [Fact]
    public async Task EndSendsTidyOnce()
    {
        var (session, bridge, _) = Create(AgentAccessMode.ConfirmEach);
        Assert.Equal(1, bridge.Tracked);

        await session.EndAsync(tidy: true, default);
        await session.EndAsync(tidy: true, default);

        var end = Assert.Single(bridge.Commands);
        Assert.Equal(("end", "task-1"), (end.Op, end.Task));
        Assert.True(end.Args.GetProperty("tidy").GetBoolean());
        Assert.Equal(0, bridge.Tracked);
    }

    [Fact]
    public async Task RefusesCallsAfterEnd()
    {
        var (session, bridge, _) = Create(AgentAccessMode.FullAccess);
        await session.EndAsync(tidy: false, default);

        var after = await session.CallAsync("browser", Args(new { op = "snapshot" }), default);

        Assert.True(after.IsError);
        Assert.Equal("This job's browser session has ended.", after.Content[0].Text);
        Assert.Equal(["end"], bridge.Commands.Select(c => c.Op));
    }

    [Fact]
    public async Task NullArgsAreDroppedAndLongSayIsCut()
    {
        var (session, bridge, broker) = Create(AgentAccessMode.ConfirmEach);
        using var args = JsonDocument.Parse($$"""
            {"op":"batch","tabId":null,"rationale":"Go back","say":"{{new string('s', 200)}}",
             "steps":[{"op":"back","tabId":null},{"op":"click","ref":"e1","dismiss":null}]}
            """);

        var result = await session.CallAsync("browser", args.RootElement, default);

        Assert.False(result.IsError);
        var command = Assert.Single(bridge.Commands);
        Assert.Equal(160, command.Say!.Length);
        Assert.False(command.Args.TryGetProperty("tabId", out _));
        Assert.False(command.Args.GetProperty("steps")[0].TryGetProperty("tabId", out _));
        Assert.False(command.Args.GetProperty("steps")[1].TryGetProperty("dismiss", out _));
        Assert.Equal("e1", command.Args.GetProperty("steps")[1].GetProperty("ref").GetString());
        Assert.DoesNotContain(broker.Last!.Arguments.Values, v => v.Contains("null"));
    }

    [Fact]
    public void ToolNamesMapFromMcp()
    {
        Assert.True(ChromeBridgeSession.TryMapToolName("openclicky_browser", out var mapped));
        Assert.Equal("browser", mapped);
        Assert.False(ChromeBridgeSession.TryMapToolName("openclicky_browser_click", out _));
    }

    // Legacy content-script transport; G9 removes these with ChromeBridgeHost.
    [Fact]
    public async Task NativeProtocolRejectsOversizedMessageBeforeAllocation()
    {
        var header = BitConverter.GetBytes(ChromeBridgeNativeProtocol.MaxMessageBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => ChromeBridgeNativeProtocol.ReadAsync(new MemoryStream(header), default));
    }

    [Fact]
    public async Task NativeRelayAuthenticatesAndCarriesOneRequestResponse()
    {
        var secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        await using var server = new ChromeBridgePipeTransport(secret);
        using var chromeInput = new MemoryStream();
        await ChromeBridgeNativeProtocol.WriteAsync(chromeInput, new { response = "ok" }, default);
        chromeInput.Position = 0;
        using var chromeOutput = new MemoryStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var relay = ChromeBridgeNativeRelay.RunAsync(chromeInput, chromeOutput, secret, cts.Token);
        await server.WaitForNativeHostAsync(cts.Token);

        using var response = await server.RequestAsync(new { requestId = "one", command = new { name = "contextActive" } }, cts.Token);
        Assert.Equal("ok", response.RootElement.GetProperty("response").GetString());
        chromeOutput.Position = 0;
        using var forwarded = await ChromeBridgeNativeProtocol.ReadAsync(chromeOutput, default);
        Assert.Equal("one", forwarded!.RootElement.GetProperty("requestId").GetString());
        cts.Cancel();
        try { await relay; } catch (OperationCanceledException) { }
        var disconnected = await Record.ExceptionAsync(() => server.RequestAsync(new { requestId = "after-disconnect" }, default));
        Assert.True(disconnected is IOException or InvalidOperationException);
        Assert.False(server.IsConnected);
        Assert.True(server.ConnectionEnded.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ConcreteHostMapsSyntheticExtensionContextAndObservation()
    {
        var secret = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
        await using var server = new ChromeBridgePipeTransport(secret);
        using var chromeInput = new MemoryStream();
        await ChromeBridgeNativeProtocol.WriteAsync(chromeInput, new { tabId = 4, frameId = 0, response = new { ok = true, url = "https://allowed.example/page", title = "Fixture", revision = 700L } }, default);
        await ChromeBridgeNativeProtocol.WriteAsync(chromeInput, new { tabId = 4, frameId = 0, response = new { ok = true, observation = new { url = "https://allowed.example/page", title = "Fixture", revision = 700L, controls = new[] { new { id = "e1", tag = "a", text = "Help" } }, links = new Dictionary<string, string> { ["e1"] = "https://allowed.example/help" } } } }, default);
        chromeInput.Position = 0;
        using var chromeOutput = new MemoryStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var relay = ChromeBridgeNativeRelay.RunAsync(chromeInput, chromeOutput, secret, cts.Token);
        await server.WaitForNativeHostAsync(cts.Token);
        var host = new ChromeBridgeHost(server);

        var context = await host.GetCurrentContextAsync(cts.Token);
        var observation = await host.ObserveAsync(cts.Token);

        Assert.Equal(new BrowserContext("4", "0", 700, "https://allowed.example/", "Fixture"), context);
        Assert.Equal("https://allowed.example/help", observation.Links["e1"]);
        cts.Cancel();
        try { await relay; } catch (OperationCanceledException) { }
    }

    [Fact]
    public void ConfigurationLoadsOnlyExactOriginsAndExpectedExtension()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"extensionOrigin":"chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/","allowedOrigins":["https://allowed.example/","https://allowed.example/"]}""");
            var config = ChromeBridgeConfiguration.Load(path);
            Assert.Equal(["https://allowed.example/"], config.AllowedOrigins);
            File.WriteAllText(path, """{"extensionOrigin":"chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/","allowedOrigins":["https://allowed.example/path"]}""");
            Assert.Throws<InvalidDataException>(() => ChromeBridgeConfiguration.Load(path));
            File.WriteAllText(path, """{"extensionOrigin":"chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/","allowedOrigins":null}""");
            Assert.Throws<InvalidDataException>(() => ChromeBridgeConfiguration.Load(path));
        }
        finally { File.Delete(path); }
    }

    private static (ChromeBridgeSession, FakeBridge, FakeBroker) Create(AgentAccessMode access, bool approve = true,
        string? outputFolder = null, Func<BrowserCommand, BrowserReply>? reply = null)
    {
        var bridge = new FakeBridge(reply ?? (c => new BrowserReply(true, "done")));
        var broker = new FakeBroker(approve);
        return (new ChromeBridgeSession(bridge, broker, access, Identity, ["paypal", "my-bank"], outputFolder), bridge, broker);
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class FakeBridge(Func<BrowserCommand, BrowserReply> reply) : IBrowserBridge
    {
        public List<BrowserCommand> Commands { get; } = [];
        public List<TimeSpan> Timeouts { get; } = [];
        public int Tracked;
        public bool IsConnected => true;
        public Task<BrowserReply> SendAsync(BrowserCommand command, TimeSpan timeout, CancellationToken token)
        { Commands.Add(command); Timeouts.Add(timeout); return Task.FromResult(reply(command)); }
        public IDisposable TrackTask(string task) { Tracked++; return new Untrack(this); }
        private sealed class Untrack(FakeBridge owner) : IDisposable { public void Dispose() => owner.Tracked--; }
    }

    private sealed class FakeBroker(bool approved) : IChromeActionBroker
    {
        public List<ActionRequest> Requests { get; } = [];
        public int Calls => Requests.Count;
        public ActionRequest? Last => Requests.LastOrDefault();
        public Task<bool> ApproveAsync(ActionRequest request, CancellationToken token) { Requests.Add(request); return Task.FromResult(approved); }
    }
}
