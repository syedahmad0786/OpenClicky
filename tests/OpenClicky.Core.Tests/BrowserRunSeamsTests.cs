using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Core.Browser;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

/// <summary>The per-run hooks the tray passes to a browser session: stop by task, the before-approval lease, the pause wait.</summary>
public sealed class BrowserRunSeamsTests
{
    private static readonly TimeSpan T = TimeSpan.FromSeconds(10);
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void StopNamesOneTaskOrAll()
    {
        var registry = new BrowserStopRegistry();
        var stopped = new List<string>();
        using var a = registry.Register("task-a", () => stopped.Add("a"));
        var b = registry.Register("task-b", () => stopped.Add("b"));

        Assert.Equal(1, registry.Stop("task-b"));
        Assert.Equal(["b"], stopped);
        Assert.Equal(0, registry.Stop("task-zzz"));
        Assert.Equal(2, registry.Stop(null));
        Assert.Equal(["a", "b"], stopped.Skip(1).Order());

        b.Dispose();
        b.Dispose(); // twice is fine
        stopped.Clear();
        Assert.Equal(0, registry.Stop("task-b"));
        Assert.Equal(1, registry.Stop(null));
        Assert.Equal(["a"], stopped);
    }

    [Fact]
    public void StopAllRunsEveryCallbackEvenWhenOneThrows()
    {
        var registry = new BrowserStopRegistry();
        var ran = 0;
        using var bad = registry.Register("task-a", () => throw new InvalidOperationException("boom"));
        using var good = registry.Register("task-b", () => ran++);
        Assert.Throws<AggregateException>(() => registry.Stop(null));
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task ApprovalHoldsTheLeaseWhileAsking()
    {
        var log = new List<string>();
        var broker = new BrowserApprovalBroker(
            (summary, _) => { log.Add("ask: " + summary); return Task.FromResult(true); },
            _ => { log.Add("lease"); return Task.FromResult<IAsyncDisposable?>(new Lease(log)); });

        var request = new ActionRequest("browser.click", new Dictionary<string, string> { ["ref"] = "e4" }, "Open the menu");
        Assert.True(await broker.ApproveAsync(request, default));
        Assert.Equal(["lease", "ask: browser.click\nOpen the menu\nref: e4", "released"], log);

        var plain = new BrowserApprovalBroker((_, _) => Task.FromResult(false), _ => Task.FromResult<IAsyncDisposable?>(null));
        Assert.False(await plain.ApproveAsync(request, default));
    }

    [Fact]
    public async Task PausedSessionSendsNothingUntilResumedButEndsAtOnce()
    {
        var bridge = new RecordingBridge();
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = true;
        var session = new ChromeBridgeSession(
            new PausableBrowserBridge(bridge, t => paused ? resume.Task.WaitAsync(t) : Task.CompletedTask),
            new Approve(), AgentAccessMode.FullAccess, new BrowserTaskIdentity("task-1", "job-1", "Research", "blue"), [], null);

        var snapshot = session.CallAsync("browser", Args(new { op = "snapshot" }), default);
        await Task.Delay(100);
        Assert.False(snapshot.IsCompleted);
        Assert.Empty(bridge.Ops);

        paused = false;
        resume.SetResult();
        Assert.False((await snapshot.WaitAsync(T)).IsError);
        Assert.Equal(["snapshot"], bridge.Ops);

        paused = true;
        resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var click = session.CallAsync("browser", Args(new { op = "click", @ref = "e1", rationale = "Open it" }), default);
        await Task.Delay(100);
        Assert.False(click.IsCompleted);
        await session.EndAsync(tidy: true, default).WaitAsync(T); // end never waits on the pause
        Assert.Equal(["snapshot", "end"], bridge.Ops);
    }

    [Fact]
    public async Task EndReleasesTheSessionLease()
    {
        var registry = new BrowserStopRegistry();
        var stops = 0;
        var session = new ChromeBridgeSession(new RecordingBridge(), new Approve(), AgentAccessMode.ConfirmEach,
            new BrowserTaskIdentity("task-1", "job-1", "Research", "blue"), [], null, registry.Register("task-1", () => stops++));
        Assert.Equal(1, registry.Stop("task-1"));
        await session.EndAsync(tidy: false, default);
        Assert.Equal(0, registry.Stop("task-1"));
        Assert.Equal(1, stops);
    }

    private sealed class Lease(List<string> log) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { log.Add("released"); return ValueTask.CompletedTask; }
    }

    private sealed class Approve : IChromeActionBroker
    {
        public Task<bool> ApproveAsync(ActionRequest request, CancellationToken token) => Task.FromResult(true);
    }

    private sealed class RecordingBridge : IBrowserBridge
    {
        private readonly object _sync = new();
        private readonly List<string> _ops = [];
        public string[] Ops { get { lock (_sync) return _ops.ToArray(); } }
        public bool IsConnected => true;
        public Task<BrowserReply> SendAsync(BrowserCommand command, TimeSpan timeout, CancellationToken token)
        {
            lock (_sync) _ops.Add(command.Op);
            return Task.FromResult(new BrowserReply(true, "done"));
        }
        public IDisposable TrackTask(string task) => new Nothing();
        private sealed class Nothing : IDisposable { public void Dispose() { } }
    }
}
