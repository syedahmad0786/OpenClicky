using OpenClicky.Core.Agent;

namespace OpenClicky.Core.Browser;

/// <summary>
/// Live browser sessions' stop callbacks by task id. The extension's <c>stop{task}</c> stops that run; <c>stop{}</c>
/// (the popup's Stop) stops every run that has a browser session.
/// </summary>
public sealed class BrowserStopRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Action> _stops = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="stop"/> for <paramref name="task"/> until the result is disposed.</summary>
    public IDisposable Register(string task, Action stop)
    {
        lock (_sync) _stops[task] = stop;
        return new Registration(this, task, stop);
    }

    /// <summary>Calls the task's callback, or every callback when <paramref name="task"/> is null, and returns how many ran.
    /// A throwing callback doesn't skip the rest; the failures are rethrown together afterwards.</summary>
    public int Stop(string? task)
    {
        Action[] stops;
        lock (_sync) stops = task is null ? _stops.Values.ToArray() : _stops.TryGetValue(task, out var one) ? [one] : [];
        List<Exception>? failures = null;
        foreach (var stop in stops)
        {
            try { stop(); }
            catch (Exception e) { (failures ??= []).Add(e); }
        }
        return failures is null ? stops.Length : throw new AggregateException(failures);
    }

    private void Remove(string task, Action stop)
    {
        lock (_sync)
            if (_stops.TryGetValue(task, out var current) && ReferenceEquals(current, stop)) _stops.Remove(task);
    }

    private sealed class Registration(BrowserStopRegistry owner, string task, Action stop) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(task, stop); }
    }
}

/// <summary>
/// A run's view of the bridge: every command but the lifecycle ones (<c>end</c>, <c>sweep</c>) first waits out the run's
/// pause, so a paused job sends nothing and its end never hangs. The wait comes before the bridge's own timeout starts.
/// </summary>
public sealed class PausableBrowserBridge(IBrowserBridge inner, Func<CancellationToken, Task> pause) : IBrowserBridge
{
    public bool IsConnected => inner.IsConnected;

    public IDisposable TrackTask(string task) => inner.TrackTask(task);

    public async Task<BrowserReply> SendAsync(BrowserCommand command, TimeSpan timeout, CancellationToken token)
    {
        if (command.Op is not ("end" or "sweep")) await pause(token).ConfigureAwait(false);
        return await inner.SendAsync(command, timeout, token).ConfigureAwait(false);
    }
}

/// <summary>
/// Asks the user about one browser action. The run's before-approval hook runs first and its lease (if any) is held while
/// the question is up: a pause wait on its own, or the run's screen lease where runs share the screen.
/// </summary>
public sealed class BrowserApprovalBroker(Func<string, CancellationToken, Task<bool>> confirm,
    Func<CancellationToken, Task<IAsyncDisposable?>> beforeApproval) : IChromeActionBroker
{
    public async Task<bool> ApproveAsync(ActionRequest request, CancellationToken token)
    {
        var lease = await beforeApproval(token).ConfigureAwait(false);
        try
        {
            return await confirm(request.ToolName + "\n" + request.Rationale + "\n"
                + string.Join("\n", request.Arguments.Select(argument => argument.Key + ": " + argument.Value)), token).ConfigureAwait(false);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
        }
    }
}
