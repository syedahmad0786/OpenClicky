using System.Text.Json;
using OpenClicky.Core.Agent;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Work;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public sealed class DesktopLeaseTests
{
    [Fact]
    public async Task SecondHolderWaits()
    {
        var lease = new DesktopLease();
        var first = await lease.AcquireAsync("a", CancellationToken.None);
        var second = lease.AcquireAsync("b", CancellationToken.None);
        await Task.Delay(50);
        Assert.False(second.IsCompleted);
        Assert.Equal("a", lease.Holder);

        first.Dispose();
        using var held = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("b", lease.Holder);
    }

    [Fact]
    public async Task ReleasedOnCancel()
    {
        var lease = new DesktopLease();
        var first = await lease.AcquireAsync("a", CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var waiting = lease.AcquireAsync("b", stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal("a", lease.Holder); // the cancelled waiter never took it

        first.Dispose();
        first.Dispose(); // a second dispose must not release someone else's hold
        Assert.Null(lease.Holder);
        using var next = await lease.AcquireAsync("c", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        var blocked = lease.AcquireAsync("d", CancellationToken.None);
        await Task.Delay(50);
        Assert.False(blocked.IsCompleted);
        Assert.Equal("c", lease.Holder);
    }

    [Fact]
    public async Task ReleasedOnException()
    {
        var lease = new DesktopLease();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using (await lease.AcquireAsync("a", CancellationToken.None)) throw new InvalidOperationException("boom");
        });
        Assert.Null(lease.Holder);
        using var again = await lease.AcquireAsync("b", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("b", lease.Holder);
    }

    [Fact]
    public async Task HolderReported()
    {
        var lease = new DesktopLease();
        var seen = new List<string?>();
        lease.Changed += () => seen.Add(lease.Holder);
        Assert.Null(lease.Holder);
        using (await lease.AcquireAsync(DesktopLease.NativeTaskHolder, CancellationToken.None))
            Assert.Equal("native-task", lease.Holder);
        Assert.Null(lease.Holder);
        Assert.Equal(["native-task", null], seen);
    }

    [Fact]
    public void LastHolderStaysOnTheScreenOnlyWhileDrivingOrBriefly()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(DesktopLease.StillOnScreen(holdsLease: true, DrivePhase.Idle, null, now));
        Assert.True(DesktopLease.StillOnScreen(false, DrivePhase.Driving, now.AddMinutes(-5), now));
        Assert.True(DesktopLease.StillOnScreen(false, DrivePhase.AwaitingConsent, null, now));
        Assert.True(DesktopLease.StillOnScreen(false, DrivePhase.TakenBack, now.AddMinutes(-5), now));
        Assert.True(DesktopLease.StillOnScreen(false, DrivePhase.Idle, now.AddSeconds(-10), now));
        Assert.False(DesktopLease.StillOnScreen(false, DrivePhase.Idle, now.AddSeconds(-11), now));
        Assert.False(DesktopLease.StillOnScreen(false, DrivePhase.Ended, now.AddSeconds(-11), now));
        Assert.False(DesktopLease.StillOnScreen(false, DrivePhase.Idle, null, now));
    }

    [Fact]
    public async Task DesktopToolsTakeTheLeaseButMemorySearchDoesNot()
    {
        var lease = new DesktopLease();
        var host = new Host(lease);
        var waits = new List<bool>();
        var session = new DesktopToolSession(host, AgentAccessMode.ConfirmEach, lease: lease, leaseHolder: "job-1", waitingForScreen: waits.Add);

        await session.CallAsync("observe_screen", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
        await session.CallAsync("wait", JsonSerializer.SerializeToElement(new { ms = "0" }), CancellationToken.None);
        Assert.Equal(["job-1", "job-1"], host.HoldersSeen);
        Assert.Null(lease.Holder);
        Assert.Empty(waits); // free lease: no waiting line

        var other = await lease.AcquireAsync("job-2", CancellationToken.None);
        await session.CallAsync("search_memory", JsonSerializer.SerializeToElement(new { query = "x" }), CancellationToken.None);
        var blocked = session.CallAsync("uia", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(blocked.IsCompleted);
        Assert.Equal([true], waits);
        other.Dispose();
        await blocked.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([true, false], waits);
        Assert.Equal("job-1", host.HoldersSeen[^1]);
        Assert.Null(lease.Holder);
    }

    private sealed class Host(DesktopLease lease) : IDesktopToolHost
    {
        public List<string?> HoldersSeen { get; } = [];
        public Task<DesktopToolObservation> ObserveAsync(bool includeScreens, bool includeUia, CancellationToken token)
        {
            HoldersSeen.Add(lease.Holder);
            return Task.FromResult(new DesktopToolObservation("obs", 1, "Test", null, []));
        }
        public Task<DesktopToolContext> GetCurrentContextAsync(CancellationToken token) => Task.FromResult(new DesktopToolContext(1, "Test"));
        public Task<bool> ConfirmAsync(ActionRequest request, CancellationToken token) => Task.FromResult(true);
        public Task<ActionResult> ExecuteAsync(ActionRequest request, bool confirmed, CancellationToken token)
        {
            HoldersSeen.Add(lease.Holder);
            return Task.FromResult(new ActionResult(true, "ok", false));
        }
        public Task<MemorySearchResponse> SearchMemoryAsync(string query, int limit, CancellationToken token)
        {
            Assert.Equal("job-2", lease.Holder); // memory search runs while another run holds the screen
            return Task.FromResult(new MemorySearchResponse(true, []));
        }
    }
}
