using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class SerialQueueTests
{
    [Fact]
    public async Task RunsInOrderWithoutOverlap()
    {
        var queue = new SerialQueue();
        var log = new List<string>();
        var running = 0;
        var overlapped = false;
        for (var i = 0; i < 5; i++)
        {
            var n = i;
            _ = queue.Enqueue(async () =>
            {
                if (Interlocked.Increment(ref running) > 1) overlapped = true;
                lock (log) log.Add("start" + n);
                await Task.Delay(n % 2 == 0 ? 30 : 1); // a slow item must still finish before the next starts
                lock (log) log.Add("end" + n);
                Interlocked.Decrement(ref running);
            });
        }
        await queue.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(overlapped);
        Assert.Equal(Enumerable.Range(0, 5).SelectMany(n => new[] { "start" + n, "end" + n }), log);
    }

    [Fact]
    public async Task AFailedItemDoesNotStopTheRest()
    {
        var queue = new SerialQueue();
        var ran = false;
        var failed = queue.Enqueue(() => throw new InvalidOperationException("boom"));
        _ = queue.Enqueue(() => Task.FromCanceled(new CancellationToken(true)));
        _ = queue.Enqueue(() => { ran = true; return Task.CompletedTask; });
        await queue.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10)); // never faults
        Assert.True(ran);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
    }

    [Fact]
    public async Task EmptyQueueIsIdle() => await new SerialQueue().WhenIdle().WaitAsync(TimeSpan.FromSeconds(1));
}
