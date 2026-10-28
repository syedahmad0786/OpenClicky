using OpenClicky.Core;
using OpenClicky.Core.Agent;

namespace OpenClicky.Core.Tests;

public class UnifiedCompanionWorkflowTests
{
    [Theory]
    [InlineData(AgentAccessMode.Preview, 0)]
    [InlineData(AgentAccessMode.ConfirmEach, 1)]
    public async Task OpenApp_UsesExistingMutationGate(AgentAccessMode access, int expectedExecutions)
    {
        var executions = 0;
        var agent = new AgentOrchestrator(new Capture(),
            new Planner(() => new AgentPlanStep("open_app", new Dictionary<string,string> { ["app_id"] = "known" }, "Open the selected app", false)),
            (_, confirmed, _) => { Assert.True(confirmed); executions++; return Task.FromResult(new ActionResult(true, "accepted", false)); },
            new AllowLaunch(), new Overlay(), new Tts(), _ => { }, maxSteps: 1, access: access);
        await agent.RunAsync("open Slack", "test");
        Assert.Equal(expectedExecutions, executions);
    }

    [Fact]
    public async Task OpenApp_AllowsForegroundChangeButStillRejectsRevisionChange()
    {
        nint window = 1;
        long revision = 1;
        var executions = 0;
        var changeRevision = false;
        var agent = new AgentOrchestrator(new Capture(),
            new Planner(() => new AgentPlanStep("open_app", new Dictionary<string,string> { ["app_id"] = "known" }, "Open selected app", false)),
            (_, _, _) => { executions++; return Task.FromResult(new ActionResult(false, "done", false)); },
            new AllowLaunch(() => { window = 2; if (changeRevision) revision = 2; }), new Overlay(), new Tts(), _ => { },
            maxSteps: 1, access: AgentAccessMode.ConfirmEach, foregroundWindow: () => window, revision: () => revision);

        await agent.RunAsync("open app", "test");
        Assert.Equal(1, executions);

        executions = 0; window = 1; revision = 1; changeRevision = true;
        await agent.RunAsync("open app", "test");
        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task OpenApp_StillHonorsExplicitPauseAfterApproval()
    {
        var control = new AgentTaskControl();
        var pausedBoundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var agent = new AgentOrchestrator(new Capture(),
            new Planner(() => new AgentPlanStep("open_app", new Dictionary<string,string> { ["app_id"] = "known" }, "Open selected app", false)),
            (_, _, _) => { executions++; return Task.FromResult(new ActionResult(false, "fixture finished", false)); },
            new AllowLaunch(control.Pause), new Overlay(), new Tts(), _ => { }, maxSteps: 1,
            beforeStep: async token =>
            {
                if (control.IsPaused) pausedBoundary.TrySetResult();
                await control.WaitAsync(token);
            });

        var run = agent.RunAsync("open app", "test");
        await pausedBoundary.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, executions);
        control.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Click_UsesForegroundInputConfirmation()
    {
        var confirm = new InputConfirm();
        var executions = 0;
        var agent = new AgentOrchestrator(new Capture(),
            new Planner(() => new AgentPlanStep("click", new Dictionary<string,string> { ["x"] = "10", ["y"] = "10" }, "Click", false)),
            (_, _, _) => { executions++; return Task.FromResult(new ActionResult(false, "done", false)); },
            confirm, new Overlay(), new Tts(), _ => { }, maxSteps: 1, access: AgentAccessMode.ConfirmEach);

        await agent.RunAsync("click", "test");

        Assert.Equal(1, confirm.InputCalls);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task SelectedRegion_SharesOnlyReturnedRegion()
    {
        var capture = new Capture
        {
            Region = new([0xff, 0xd8], 80, 60, 10, 20, 80, 60, true, "raw"),
        };
        IReadOnlyList<ScreenCaptureResult>? shared = null;
        using var talk = CreateTalk(
            capture,
            () => new Talk((system, screens) =>
            {
                shared = screens;
                return "Selected area [POINT:none]";
            }));

        await talk.SubmitRegionAsync("Explain this", (10, 20, 80, 60));

        Assert.Equal(0, capture.AllCalls);
        Assert.Equal(1, capture.RegionCalls);
        Assert.Single(shared!);
        Assert.Contains("user-selected area", shared![0].Label);
    }

    [Fact]
    public async Task MissingSelectedRegion_ShowsFailureWithoutCallingProvider()
    {
        var capture = new Capture();
        var providerCalls = 0;
        var messages = new List<string>();
        using var talk = CreateTalk(capture, () => new Talk((_, _) =>
        {
            providerCalls++;
            return "unexpected";
        }));
        talk.MessageReceived += (_, message) => messages.Add(message);

        await talk.SubmitRegionAsync("Explain this", (10, 20, 80, 60));

        Assert.Equal(0, providerCalls);
        Assert.Equal(0, capture.AllCalls);
        Assert.Equal(1, capture.RegionCalls);
        Assert.Contains(messages, message => message.Contains("selected screen area could not be captured", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RequestInterceptor_HandlesRequestBeforeCaptureOrProvider()
    {
        var capture = new Capture();
        var providerCalls = 0;
        var intercepted = new List<string>();
        using var talk = CreateTalk(
            capture,
            () =>
            {
                providerCalls++;
                return new Talk((_, _) => "unexpected");
            },
            requestInterceptor: (request, _) =>
            {
                intercepted.Add(request);
                return Task.FromResult(true);
            });
        var messages = new List<string>();
        talk.MessageReceived += (_, message) => messages.Add(message);

        await talk.SubmitTextAsync("show my local task");

        Assert.Equal(new[] { "show my local task" }, intercepted);
        Assert.Equal(0, capture.AllCalls);
        Assert.Equal(0, capture.RegionCalls);
        Assert.Equal(0, providerCalls);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task UserContext_IsAppendedToSystemPrompt()
    {
        var capture = new Capture();
        string? systemPrompt = null;
        using var talk = CreateTalk(
            capture,
            () => new Talk((system, _) =>
            {
                systemPrompt = system;
                return "Done [POINT:none]";
            }),
            userContext: () => "\nUSER-AUTHORED-CONTEXT");

        await talk.SubmitTextAsync("help");

        Assert.Contains("USER-AUTHORED-CONTEXT", systemPrompt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextChangeAfterPlanning_DiscardsOldMutatingAction(bool changeWindow)
    {
        nint window = 1;
        long revision = 1;
        var executions = 0;
        var progress = new List<AgentProgress>();
        var agent = new AgentOrchestrator(
            new Capture(),
            new Planner(() =>
            {
                if (changeWindow) window = 2;
                else revision = 2;
                return new AgentPlanStep(
                    "type_text",
                    new Dictionary<string, string> { ["text"] = "stale" },
                    "Old plan",
                    false);
            }),
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(new ActionResult(true, "done", false));
            },
            new Confirm(),
            new Overlay(),
            new Tts(),
            _ => { },
            maxSteps: 1,
            onProgress: progress.Add,
            access: AgentAccessMode.FullAccess,
            foregroundWindow: () => window,
            revision: () => revision);

        await agent.RunAsync("type stale text", "test");

        Assert.Equal(0, executions);
        Assert.Contains(progress, item => item.Status == AgentProgressStates.Observing
            && item.Message.Contains("Context changed", StringComparison.Ordinal));
    }

    private static TalkOrchestrator CreateTalk(
        Capture capture,
        Func<ITalkClient?> provider,
        Func<string>? userContext = null,
        Func<string, CancellationToken, Task<bool>>? requestInterceptor = null) =>
        new(
            new Hotkey(),
            new Overlay(),
            capture,
            new Mic(),
            () => null,
            provider,
            new Tts(),
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true },
            log: _ => { },
            userContext: userContext,
            requestInterceptor: requestInterceptor);

    private sealed class Capture : IScreenCaptureService
    {
        public int AllCalls;
        public int RegionCalls;
        public ScreenCaptureResult? Region;

        public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        {
            AllCalls++;
            return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>(
                [new([0xff, 0xd8], 100, 50, 0, 0, 100, 50, true, "screen 1")]);
        }

        public Task<ScreenCaptureResult?> CaptureRegionJpegAsync(
            (int X, int Y, int Width, int Height) bounds,
            CancellationToken token = default)
        {
            RegionCalls++;
            return Task.FromResult(Region);
        }
    }

    private sealed class Talk(Func<string, IReadOnlyList<ScreenCaptureResult>, string> reply) : ITalkClient
    {
        public Task<string> CompleteVisionTalkAsync(
            string system,
            IReadOnlyList<(string role, string text)> history,
            string text,
            IReadOnlyList<ScreenCaptureResult> screens,
            string model,
            CancellationToken token = default) => Task.FromResult(reply(system, screens));
    }

    private sealed class Planner(Func<AgentPlanStep> plan) : IAgentPlanner
    {
        public Task<AgentPlanStep> NextStepAsync(
            string task,
            IReadOnlyList<ScreenCaptureResult> screens,
            IReadOnlyList<string> prior,
            string model,
            CancellationToken token = default) => Task.FromResult(plan());
    }

    private sealed class AllowLaunch(Action? approved = null) : IAgentConfirm
    {
        public Task<bool> ConfirmAsync(string summary, CancellationToken token = default) =>
            throw new InvalidOperationException("App launch must use non-input confirmation.");
        public Task<bool> ConfirmNonInputAsync(string summary, CancellationToken token = default)
        {
            Assert.Contains("open_app", summary);
            approved?.Invoke();
            return Task.FromResult(true);
        }
    }

    private sealed class InputConfirm : IAgentConfirm
    {
        public int InputCalls;
        public Task<bool> ConfirmAsync(string summary, CancellationToken token = default)
        { InputCalls++; return Task.FromResult(true); }
        public Task<bool> ConfirmNonInputAsync(string summary, CancellationToken token = default) =>
            throw new InvalidOperationException("Foreground input must use strict confirmation.");
    }

    private sealed class Confirm : IAgentConfirm
    {
        public Task<bool> ConfirmAsync(string summary, CancellationToken token = default) =>
            throw new InvalidOperationException("Discarded action must not request confirmation.");
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

    private sealed class Mic : IMicrophoneRecorder
    {
        public bool IsRecording => false;
        public void Start() { }
        public byte[] StopToWav() => [];
        public void Dispose() { }
    }

    private sealed class Overlay : IOverlayController
    {
        public void ShowListening() { }
        public void ShowProcessing() { }
        public void ShowResponding(string text) { }
        public void PointAt(int x, int y, string? label, string? text = null) { }
        public void Hide() { }
        public void Dispose() { }
    }

    private sealed class Tts : ITtsClient
    {
        public Task SpeakAsync(string text, CancellationToken token = default) => Task.CompletedTask;
        public void Stop() { }
    }
}
