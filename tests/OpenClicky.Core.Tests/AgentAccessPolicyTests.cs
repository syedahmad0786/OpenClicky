using OpenClicky.Core.Agent;

namespace OpenClicky.Core.Tests;

public class AgentAccessPolicyTests
{
    [Fact]
    public async Task TakingOverWhileAlreadyPausedInvalidatesPendingResume()
    {
        var control = new AgentTaskControl();
        control.Pause();
        var countdownRevision = control.PauseRevision;
        var pending = control.WaitAsync(CancellationToken.None);
        control.Pause();
        Assert.False(control.TryResume(countdownRevision));
        Assert.False(pending.IsCompleted);
        Assert.True(control.TryResume(control.PauseRevision));
        await pending;
        Assert.False(control.IsPaused);
    }

    [Fact]
    public async Task Preview_StopsBeforeConfirmationOrInput()
    {
        var confirmations = 0;
        var executions = 0;
        var progress = new List<AgentProgress>();
        var agent = CreateAgent(
            AgentAccessMode.Preview,
            new("type_text", new Dictionary<string, string> { ["text"] = "hello" }, "Type hello", false),
            () => { confirmations++; return true; },
            () => executions++,
            progress.Add);

        await agent.RunAsync("type hello", "test");

        Assert.Equal(0, confirmations);
        Assert.Equal(0, executions);
        Assert.Contains(progress, item => item.Status == AgentProgressStates.Stopped);
    }

    [Fact]
    public async Task FullAccess_SkipsConfirmationAndExecutes()
    {
        var confirmations = 0;
        var executions = 0;
        var agent = CreateAgent(
            AgentAccessMode.FullAccess,
            new("type_text", new Dictionary<string, string> { ["text"] = "hello" }, "Type hello", false),
            () => { confirmations++; return true; },
            () => executions++);

        await agent.RunAsync("type hello", "test");

        Assert.Equal(0, confirmations);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Auto_StillConfirmsSensitiveAction()
    {
        var confirmations = 0;
        var executions = 0;
        var agent = CreateAgent(
            AgentAccessMode.Auto,
            new("type_text", new Dictionary<string, string> { ["text"] = "secret" }, "Enter password", false),
            () => { confirmations++; return false; },
            () => executions++);

        await agent.RunAsync("enter password", "test");

        Assert.Equal(1, confirmations);
        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task UnknownAccessMode_FallsBackToConfirmation()
    {
        var confirmations = 0;
        var executions = 0;
        var agent = CreateAgent(
            (AgentAccessMode)999,
            new("type_text", new Dictionary<string, string> { ["text"] = "hello" }, "Type hello", false),
            () => { confirmations++; return false; },
            () => executions++);

        await agent.RunAsync("type hello", "test");

        Assert.Equal(1, confirmations);
        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task PausedWait_CanResume()
    {
        var control = new AgentTaskControl();
        control.Pause();

        var waiting = control.WaitAsync(CancellationToken.None);
        Assert.True(control.IsPaused);
        Assert.False(waiting.IsCompleted);

        control.Resume();
        await waiting.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(control.IsPaused);
    }

    [Fact]
    public async Task PausedWait_ObservesCancellationWithoutResuming()
    {
        var control = new AgentTaskControl();
        control.Pause();
        using var cancellation = new CancellationTokenSource();
        var waiting = control.WaitAsync(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.True(control.IsPaused);
        control.Resume();
    }

    [Fact]
    public async Task ProtectedForegroundStopsBeforeCapturePlanningOrExecution()
    {
        var capture = new Capture();
        var planner = new Planner(new("done", new Dictionary<string, string>(), "Done", true));
        var executions = 0;
        var agent = new AgentOrchestrator(
            capture,
            planner,
            (_, _, _) => { executions++; return Task.FromResult(new ActionResult(true, "done", false)); },
            new Confirm(() => true),
            new Overlay(),
            new Tts(),
            _ => { },
            foregroundTitle: () => "Private Ledger",
            denyListExtra: () => ["Private Ledger"],
            maxSteps: 1);

        await agent.RunAsync("inspect", "test");

        Assert.Equal(0, capture.Calls);
        Assert.Equal(0, planner.Calls);
        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task ProductionBoundsCaptureOnlyCurrentWindowRegion()
    {
        var capture = new Capture();
        var planner = new Planner(new("done", new Dictionary<string, string>(), "Done", true));
        var bounds = (X: 10, Y: 20, Width: 300, Height: 200);
        var agent = new AgentOrchestrator(capture, planner,
            (_, _, _) => Task.FromResult(new ActionResult(true, "done", false)),
            new Confirm(() => true), new Overlay(), new Tts(), _ => { }, maxSteps: 1,
            foregroundTitle: () => "Editor", foregroundWindow: () => (nint)7,
            foregroundBounds: () => bounds);

        await agent.RunAsync("inspect", "test");

        Assert.Equal(1, capture.RegionCalls);
        Assert.Equal(0, capture.AllMonitorCalls);
        Assert.Equal(bounds, capture.LastBounds);
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task ProtectedChangeDuringCaptureDiscardsPixelsBeforePlanner()
    {
        var title = "Editor";
        var capture = new Capture { OnRegionCapture = () => title = "Private Ledger" };
        var planner = new Planner(new("done", new Dictionary<string, string>(), "Done", true));
        var agent = new AgentOrchestrator(capture, planner,
            (_, _, _) => Task.FromResult(new ActionResult(true, "done", false)),
            new Confirm(() => true), new Overlay(), new Tts(), _ => { }, maxSteps: 1,
            foregroundTitle: () => title, denyListExtra: () => ["Private Ledger"],
            foregroundWindow: () => (nint)7, foregroundBounds: () => (0, 0, 100, 100));

        await agent.RunAsync("inspect", "test");

        Assert.Equal(1, capture.RegionCalls);
        Assert.Equal(0, planner.Calls);
    }

    [Fact]
    public async Task ProtectedChangeAfterPlanningBlocksReadToolExecution()
    {
        var title = "Editor";
        var capture = new Capture();
        var planner = new Planner(new("uia_snapshot", new Dictionary<string, string>(), "Read controls", false),
            () => title = "Private Ledger");
        var executions = 0;
        var agent = new AgentOrchestrator(capture, planner,
            (_, _, _) => { executions++; return Task.FromResult(new ActionResult(true, "read", false)); },
            new Confirm(() => true), new Overlay(), new Tts(), _ => { }, maxSteps: 1,
            foregroundTitle: () => title, denyListExtra: () => ["Private Ledger"]);

        await agent.RunAsync("inspect", "test");

        Assert.Equal(1, planner.Calls);
        Assert.Equal(0, executions);
    }

    private static AgentOrchestrator CreateAgent(
        AgentAccessMode access,
        AgentPlanStep plan,
        Func<bool> confirm,
        Action execute,
        Action<AgentProgress>? progress = null) =>
        new(
            new Capture(),
            new Planner(plan),
            (_, _, _) =>
            {
                execute();
                return Task.FromResult(new ActionResult(false, "test complete", false));
            },
            new Confirm(confirm),
            new Overlay(),
            new Tts(),
            _ => { },
            maxSteps: 1,
            onProgress: progress,
            access: access);

    private sealed class Capture : IScreenCaptureService
    {
        public int Calls => AllMonitorCalls + RegionCalls;
        public int AllMonitorCalls, RegionCalls;
        public (int X, int Y, int Width, int Height)? LastBounds;
        public Action? OnRegionCapture;
        public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        {
            AllMonitorCalls++;
            return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>(
                [new([0xff, 0xd8], 100, 50, 0, 0, 100, 50, true, "test")]);
        }

        public Task<ScreenCaptureResult?> CaptureRegionJpegAsync((int X, int Y, int Width, int Height) bounds, CancellationToken token = default)
        {
            RegionCalls++;
            LastBounds = bounds;
            OnRegionCapture?.Invoke();
            return Task.FromResult<ScreenCaptureResult?>(new([0xff, 0xd8], bounds.Width, bounds.Height,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, true, "test"));
        }
    }

    private sealed class Planner(AgentPlanStep plan, Action? onPlan = null) : IAgentPlanner
    {
        public int Calls;
        public Task<AgentPlanStep> NextStepAsync(
            string task,
            IReadOnlyList<ScreenCaptureResult> screens,
            IReadOnlyList<string> prior,
            string model,
            CancellationToken token = default)
        {
            Calls++;
            onPlan?.Invoke();
            return Task.FromResult(plan);
        }
    }

    private sealed class Confirm(Func<bool> confirm) : IAgentConfirm
    {
        public Task<bool> ConfirmAsync(string summary, CancellationToken token = default) =>
            Task.FromResult(confirm());
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
