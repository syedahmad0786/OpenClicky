namespace OpenClicky.Core.Dictation;

/// <summary>Runs queued work one item at a time, in the order queued, on the thread pool. A failed or cancelled item does not stop the rest.</summary>
public sealed class SerialQueue
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Queues <paramref name="work"/> after everything queued before it; the returned task completes (or faults) with it.</summary>
    public Task Enqueue(Func<Task> work)
    {
        lock (_gate)
            return _tail = _tail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    /// <summary>Completes once everything queued so far has run. Never faults.</summary>
    public Task WhenIdle()
    {
        Task tail;
        lock (_gate) tail = _tail;
        return tail.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
