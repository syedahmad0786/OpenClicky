using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public sealed class ChromeBridgeSessionTests
{
    [Fact]
    public async Task ExcludedOriginIsRejectedBeforeObservationRead()
    {
        var host = new FakeHost { Context = Context(origin: "https://blocked.example/") };
        var session = Session(host, approved: true);

        var result = await session.CallAsync("browser_observe", Args(), default);

        Assert.True(result.IsError);
        Assert.Equal(0, host.ObserveCalls);
    }

    [Fact]
    public async Task ApprovedLinkRequiresCurrentObservedNavigationAndKnownLink()
    {
        var host = new FakeHost();
        var broker = new FakeBroker(true);
        var session = new ChromeBridgeSession(host, broker, AgentAccessMode.ConfirmEach, ["https://allowed.example/"]);
        var observed = await session.CallAsync("browser_observe", Args(), default);

        Assert.False(observed.IsError);
        var result = await session.CallAsync("browser_follow_link", Args(new
        {
            observation_id = "observation-1", navigation_revision = "7", link_id = "e1", rationale = "Open the observed help link"
        }), default);

        Assert.False(result.IsError);
        Assert.Equal(1, broker.Calls);
        Assert.Equal("browser_follow_link", host.LastAction?.Name);
        Assert.Equal("https://allowed.example/help", host.Observation.Links["e1"]);
    }

    [Fact]
    public async Task NavigationChangePreventsApprovedAction()
    {
        var host = new FakeHost();
        var session = Session(host, approved: true);
        await session.CallAsync("browser_observe", Args(), default);
        host.Context = host.Context with { NavigationRevision = 8 };

        var result = await session.CallAsync("browser_click", Args(new
        {
            observation_id = "observation-1", navigation_revision = "7", element_id = "e2"
        }), default);

        Assert.True(result.IsError);
        Assert.Null(host.LastAction);
    }

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

    private static ChromeBridgeSession Session(FakeHost host, bool approved) =>
        new(host, new FakeBroker(approved), AgentAccessMode.ConfirmEach, ["https://allowed.example/"]);
    private static BrowserContext Context(string origin = "https://allowed.example/") => new("tab-4", "top", 7, origin, "Allowed page");
    private static JsonElement Args(object? value = null) => JsonSerializer.SerializeToElement(value ?? new { });

    private sealed class FakeBroker(bool approved) : IChromeActionBroker
    {
        public int Calls { get; private set; }
        public Task<bool> ApproveAsync(ActionRequest request, CancellationToken token) { Calls++; return Task.FromResult(approved); }
    }

    private sealed class FakeHost : IChromeBridgeHost
    {
        public BrowserContext Context { get; set; } = ChromeBridgeSessionTests.Context();
        public BrowserObservation Observation => new("observation-1", Context, "synthetic fixture", new Dictionary<string, string> { ["e1"] = "https://allowed.example/help" });
        public int ObserveCalls { get; private set; }
        public BrowserAction? LastAction { get; private set; }
        public Task<BrowserContext> GetCurrentContextAsync(CancellationToken token) => Task.FromResult(Context);
        public Task<BrowserObservation> ObserveAsync(CancellationToken token) { ObserveCalls++; return Task.FromResult(Observation); }
        public Task<BrowserActionResult> ExecuteAsync(BrowserAction action, CancellationToken token) { LastAction = action; return Task.FromResult(new BrowserActionResult(true, "done")); }
    }
}
