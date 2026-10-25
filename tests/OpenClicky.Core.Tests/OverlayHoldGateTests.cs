namespace OpenClicky.Core.Tests;

public class OverlayHoldGateTests
{
    [Fact]
    public void AllowWhileNotHeldIsTrueAndReplaysNothing()
    {
        var replays = 0;
        var gate = new OverlayHoldGate(() => replays++);
        Assert.True(gate.Allow());
        Assert.False(gate.Held);
        gate.Hold().Dispose(); // nothing was refused, so nothing is replayed
        Assert.Equal(0, replays);
    }

    [Fact]
    public void ARefusedShowReplaysOnceAtTheLastOfNestedHolds()
    {
        var replays = 0;
        var gate = new OverlayHoldGate(() => replays++);
        var outer = gate.Hold();
        var inner = gate.Hold();
        Assert.False(gate.Allow());
        Assert.False(gate.Allow()); // many refusals, one replay
        inner.Dispose();
        Assert.Equal(0, replays);
        Assert.True(gate.Held);
        outer.Dispose();
        Assert.Equal(1, replays);
        Assert.False(gate.Held);
    }

    [Fact]
    public void DisposingAHoldTwiceReleasesItOnce()
    {
        var replays = 0;
        var gate = new OverlayHoldGate(() => replays++);
        var first = gate.Hold();
        var second = gate.Hold();
        Assert.False(gate.Allow());
        first.Dispose();
        first.Dispose(); // must not close the second hold
        Assert.True(gate.Held);
        Assert.Equal(0, replays);
        second.Dispose();
        Assert.Equal(1, replays);
        Assert.False(gate.Held);
    }

    [Fact]
    public void AHoldReopenedBeforeTheReplayRunsDefersItAgain()
    {
        var gate = (OverlayHoldGate?)null;
        var replays = 0;
        var queued = new Queue<Action>(); // the real overlay posts the replayed show to its UI thread
        gate = new OverlayHoldGate(() => { replays++; queued.Enqueue(() => gate!.Allow()); });
        var first = gate.Hold();
        Assert.False(gate.Allow());
        first.Dispose();
        Assert.Equal(1, replays);

        var second = gate.Hold(); // before the queued replay runs
        queued.Dequeue()(); // the replayed show asks again and is refused
        second.Dispose();
        Assert.Equal(2, replays); // so it replays again at this release
    }

    [Fact]
    public void AHideDuringTheHoldCancelsTheDeferredShow()
    {
        var replays = 0;
        var gate = new OverlayHoldGate(() => replays++);
        var hold = gate.Hold();
        Assert.False(gate.Allow()); // a ShowIdle mid-capture
        gate.CancelDeferred(); // then a plain Hide: the newest request is to hide
        hold.Dispose();
        Assert.Equal(0, replays);

        var later = gate.Hold();
        Assert.False(gate.Allow()); // a show after the Hide is newer again and still replays
        later.Dispose();
        Assert.Equal(1, replays);
    }
}
