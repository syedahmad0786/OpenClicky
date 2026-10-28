using OpenClicky.Core.Agent;

namespace OpenClicky.Core.Tests;

/// <summary>The native agent and the drive gate: a take-back or an elevation refusal pauses the run, it does not end it.</summary>
public class DriveNativeAgentTests
{
    [Fact]
    public async Task ObserveAgainPausesThenReplansFromAFreshLookWithoutReplayingTheAction()
    {
        var control = new AgentTaskControl();
        var capture = new Capture();
        var executed = new List<string>();
        var replies = new List<string>();
        var planner = new Planner(
            new("type_text", new Dictionary<string, string> { ["text"] = "stale" }, "Type the stale text", false),
            new("done", new Dictionary<string, string> { ["summary"] = "finished" }, "Finished", true));
        var agent = new AgentOrchestrator(capture, planner,
            (request, _, _) =>
            {
                executed.Add(request.Arguments["text"]);
                control.Pause(); // the gate paused the task (take-back or refusal) and sent nothing
                return Task.FromResult(new ActionResult(false, "You took over, so I didn't do that step. Say resume task when you want me to continue.", false, ObserveAgain: true));
            },
            new Confirm(), new Overlay(), new Tts(), _ => { }, maxSteps: 5, onReply: replies.Add,
            beforeStep: control.WaitAsync, access: AgentAccessMode.FullAccess);

        var run = agent.RunAsync("task", "model");
        await WaitUntil(() => control.IsPaused && executed.Count == 1);
        await Task.Delay(100);
        Assert.False(run.IsCompleted); // waiting out the pause, not ended
        Assert.Equal(1, capture.Calls); // no fresh look until the user resumes

        control.Resume();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, capture.Calls); // a fresh observation after the resume
        Assert.Equal(2, planner.Calls); // and a fresh plan
        Assert.Equal(["stale"], executed); // the old action was never replayed
        Assert.Contains(replies, r => r.Contains("say resume task", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("finished", replies[^1]);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    private sealed class Capture : IScreenCaptureService
    {
        public int Calls;
        public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>([new([0xff, 0xd8], 100, 50, 0, 0, 100, 50, true, "test")]);
        }

        public Task<ScreenCaptureResult?> CaptureRegionJpegAsync((int X, int Y, int Width, int Height) bounds, CancellationToken token = default) =>
            throw new NotSupportedException();
    }

    private sealed class Planner(params AgentPlanStep[] steps) : IAgentPlanner
    {
        public int Calls;
        public Task<AgentPlanStep> NextStepAsync(string task, IReadOnlyList<ScreenCaptureResult> screens, IReadOnlyList<string> prior,
            string model, CancellationToken token = default) => Task.FromResult(steps[Math.Min(Calls++, steps.Length - 1)]);
    }

    private sealed class Confirm : IAgentConfirm
    {
        public Task<bool> ConfirmAsync(string summary, CancellationToken token = default) => Task.FromResult(true);
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
