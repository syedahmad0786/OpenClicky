using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class CompanionWorkflowTests
{
    [Fact]
    public async Task TypedRequest_BypassesMicrophoneAndReportsReply()
    {
        var mic = new Mic();
        var capture = new Capture();
        var overlay = new Overlay();
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, capture, mic, () => throw new Exception("STT must not run"),
            () => new Talk((text, token) => Task.FromResult("Found it. [POINT:50,25:button]")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);
        await talk.SubmitTextAsync("Find the button", remember: false);
        Assert.Equal(0, mic.Starts);
        Assert.Equal(1, capture.Calls);
        Assert.Contains("Found it.", messages);
        Assert.Equal((100, 50), overlay.LastPoint);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task NewRequest_CancelsOldTurnWithoutPublishingItsReply()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new List<string>();
        var client = new Talk(async (text, token) =>
        {
            if (text == "old") { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return "new reply [POINT:none]";
        });
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => client, new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);
        var old = talk.SubmitTextAsync("old", false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await talk.SubmitTextAsync("new", false);
        await old;
        Assert.Single(messages, m => m == "new reply");
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task MissingProvider_GivesAnActionableMessageWithoutCapturingScreen()
    {
        var capture = new Capture();
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null, () => null,
            new Tts(), () => new AppSettings(), log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);
        await talk.SubmitTextAsync("help", false);
        Assert.Contains(messages, m => m.Contains("Connect OpenRouter"));
        Assert.Equal(0, capture.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Agent_DenialOrExecutionFailureStopsTheLoop(bool allow, bool fail)
    {
        var planner = new Planner(new("type_text", new Dictionary<string,string> { ["text"] = "hello" }, "test", false));
        var confirm = new Confirm(() => allow);
        var executions = 0;
        var replies = new List<string>();
        var agent = new AgentOrchestrator(new Capture(), planner, (request, approved, token) =>
        {
            executions++;
            return Task.FromResult(new ActionResult(!fail, "input rejected", false));
        }, confirm, new Overlay(), new Tts(), _ => { }, onReply: replies.Add);
        await agent.RunAsync("test", "test");
        Assert.Equal(allow ? 1 : 0, executions);
        Assert.Equal(1, planner.Calls);
        Assert.Single(replies);
    }

    [Fact]
    public async Task Agent_RechecksProtectedWindowAfterApproval()
    {
        var title = "Test sandbox";
        var executions = 0;
        var agent = new AgentOrchestrator(new Capture(), new Planner(new("type_text", new Dictionary<string,string> { ["text"] = "hello" }, "test", false)),
            (r, c, t) => { executions++; return Task.FromResult(new ActionResult(true, "ok", false)); },
            new Confirm(() => { title = "1Password"; return true; }), new Overlay(), new Tts(), _ => { }, foregroundTitle: () => title);
        await agent.RunAsync("test", "test");
        Assert.Equal(0, executions);
    }

    [Theory]
    [InlineData("NaN", "screenshot")]
    [InlineData("5000", "screenshot")]
    [InlineData("5000", "virtual")]
    [InlineData("5", "unknown")]
    public async Task Agent_InvalidCoordinatesNeverAskOrExecute(string x, string space)
    {
        var confirm = new Confirm(() => throw new Exception("Invalid input must not reach approval"));
        var agent = new AgentOrchestrator(new Capture(), new Planner(new("click", new Dictionary<string,string> { ["x"] = x, ["y"] = "5", ["space"] = space }, "test", false)),
            (r,c,t) => throw new Exception("Invalid input must not execute"), confirm, new Overlay(), new Tts(), _ => { });
        await Assert.ThrowsAsync<FormatException>(() => agent.RunAsync("test", "test"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("http://user:pass@localhost")]
    [InlineData("http://localhost?secret=x")]
    public void LocalProxy_RejectsNonLocalOrAmbiguousDestinations(string url) =>
        Assert.Throws<InvalidOperationException>(() => ProviderFactory.ResolveBaseUrl(new AppSettings { UseLocalProxy = true, LocalProxyBaseUrl = url }));

    [Fact]
    public void LocalProxy_NormalizesLoopback() => Assert.Equal("http://127.0.0.1:8787/v1", ProviderFactory.ResolveBaseUrl(new AppSettings { UseLocalProxy = true }));

    [Fact]
    public void Hotkey_UsesObservedModifierKeyEvents()
    {
        Assert.True(HotkeyChord.Default.MatchesPressedKeys(new HashSet<int> { 0x5B, 0xA4, 0x20 }));
        Assert.False(HotkeyChord.Default.MatchesPressedKeys(new HashSet<int> { 0x5B, 0x20 }));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tool\":\"shell\"}")]
    [InlineData("{\"tool\":42}")]
    public void AgentParser_RejectsMissingOrUnsupportedTools(string json) => Assert.Throws<FormatException>(() => OpenRouterAgentPlanner.ParseStep(json));

    private sealed class Capture : IScreenCaptureService
    {
        public int Calls;
        public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        { Calls++; return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>([new([0xff,0xd8], 100,50,0,0,200,100,true,"test")]); }
    }
    private sealed class Mic : IMicrophoneRecorder
    {
        public int Starts;
        public bool IsRecording { get; private set; }
        public void Start() { Starts++; IsRecording = true; }
        public byte[] StopToWav() { IsRecording = false; return []; }
        public void Dispose() { }
    }
    private sealed class Hotkey : IPushToTalkMonitor
    {
        public HotkeyChord Chord { get; set; } = HotkeyChord.Default;
        public event EventHandler? Pressed { add { } remove { } }
        public event EventHandler? Released { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Overlay : IOverlayController
    {
        public (int,int)? LastPoint;
        public void ShowListening() { }
        public void ShowProcessing() { }
        public void ShowResponding(string text) { }
        public void PointAt(int x, int y, string? label, string? text = null) => LastPoint = (x,y);
        public void Hide() { }
        public void Dispose() { }
    }
    private sealed class Tts : ITtsClient
    { public Task SpeakAsync(string text, CancellationToken token = default) => Task.CompletedTask; public void Stop() { } }
    private sealed class Talk(Func<string,CancellationToken,Task<string>> reply) : ITalkClient
    { public Task<string> CompleteVisionTalkAsync(string system, IReadOnlyList<(string role,string text)> history, string text, IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken token = default) => reply(text,token); }
    private sealed class Planner(AgentPlanStep step) : IAgentPlanner
    {
        public int Calls;
        public Task<AgentPlanStep> NextStepAsync(string task, IReadOnlyList<ScreenCaptureResult> screens, IReadOnlyList<string> prior, string model, CancellationToken token = default) { Calls++; return Task.FromResult(step); }
    }
    private sealed class Confirm(Func<bool> allow) : IAgentConfirm
    { public Task<bool> ConfirmAsync(string summary, CancellationToken token = default) => Task.FromResult(allow()); }
}
