using OpenClicky.Core.Agent;

namespace OpenClicky.Core.Tests;

/// <summary>The native agent's capture never sees the companion: it is hidden first, and a pause or the end of a talk turn that
/// calls ShowIdle mid-capture waits for the capture to finish.</summary>
public class AgentCaptureHidesOverlayTests
{
    [Fact]
    public async Task OverlayIsHiddenForEveryCaptureAndShowIdleMidCaptureWaitsForIt()
    {
        var overlay = new GatedOverlay();
        var seen = new List<(bool BeforeIdle, bool AfterIdle)>();
        var capture = new Capture(() =>
        {
            var before = overlay.Visible;
            overlay.ShowIdle(); // PauseTask / the end of a talk turn, while the capture is in flight
            seen.Add((before, overlay.Visible));
        });
        var planner = new Planner(
            new("wait", new Dictionary<string, string>(), "Let the page settle", false),
            new("done", new Dictionary<string, string> { ["summary"] = "finished" }, "Finished", true));
        overlay.ShowIdle(); // the resting companion is up when the run begins
        Assert.True(overlay.Visible);

        var agent = new AgentOrchestrator(capture, planner, (_, _, _) => Task.FromResult(new ActionResult(true, "ok", false)),
            new Confirm(), overlay, new Tts(), _ => { }, maxSteps: 5, access: AgentAccessMode.FullAccess);
        await agent.RunAsync("task", "model").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, seen.Count); // two observations, so two captures
        Assert.All(seen, s => Assert.False(s.BeforeIdle, "the companion was visible when the capture ran"));
        Assert.All(seen, s => Assert.False(s.AfterIdle, "ShowIdle put the companion back mid-capture"));
        Assert.True(overlay.Visible); // released: the deferred show ran, and the reply bubble shows
        Assert.False(overlay.Held);
    }

    [Fact]
    public async Task AFailedCaptureStillReleasesTheHold()
    {
        var overlay = new GatedOverlay();
        var capture = new Capture(() => throw new IOException("capture failed"));
        var agent = new AgentOrchestrator(capture, new Planner(new AgentPlanStep("done", new Dictionary<string, string>(), "x", true)),
            (_, _, _) => Task.FromResult(new ActionResult(true, "ok", false)), new Confirm(), overlay, new Tts(), _ => { }, maxSteps: 2);
        await Assert.ThrowsAsync<IOException>(() => agent.RunAsync("task", "model").WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(overlay.Held);
    }

    private sealed class Capture(Action onCapture) : IScreenCaptureService
    {
        public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        {
            onCapture();
            return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>([new([0xff, 0xd8], 100, 50, 0, 0, 100, 50, true, "test")]);
        }

        public Task<ScreenCaptureResult?> CaptureRegionJpegAsync((int X, int Y, int Width, int Height) bounds, CancellationToken token = default) =>
            throw new NotSupportedException();
    }

    private sealed class Planner(params AgentPlanStep[] steps) : IAgentPlanner
    {
        private int _calls;
        public Task<AgentPlanStep> NextStepAsync(string task, IReadOnlyList<ScreenCaptureResult> screens, IReadOnlyList<string> prior,
            string model, CancellationToken token = default) => Task.FromResult(steps[Math.Min(_calls++, steps.Length - 1)]);
    }

    private sealed class Confirm : IAgentConfirm
    {
        public Task<bool> ConfirmAsync(string summary, CancellationToken token = default) => Task.FromResult(true);
    }

    private sealed class Tts : ITtsClient
    {
        public Task SpeakAsync(string text, CancellationToken token = default) => Task.CompletedTask;
        public void Stop() { }
    }

    /// <summary>Shows and hides the way the real overlay does: every show goes through the hold gate.</summary>
    private sealed class GatedOverlay : IOverlayController
    {
        private readonly OverlayHoldGate _gate;
        private volatile bool _visible;
        public GatedOverlay() { _gate = new(() => _visible = true); }
        public bool Visible => _visible;
        public bool Held => _gate.Held;
        private void Show() { if (_gate.Allow()) _visible = true; }
        public IDisposable HoldHidden() => _gate.Hold();
        public void Hide() => _visible = false;
        public void ShowIdle() => Show();
        public void ShowListening() => Show();
        public void ShowProcessing() => Show();
        public void ShowResponding(string text) => Show();
        public void PointAt(int x, int y, string? label, string? text = null) => Show();
        public void Dispose() { }
    }
}
