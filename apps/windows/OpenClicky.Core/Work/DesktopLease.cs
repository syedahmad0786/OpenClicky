namespace OpenClicky.Core.Work;

/// <summary>
/// The screen and input belong to one run at a time. A job takes the lease for each desktop tool call; a native task holds it for its
/// whole run. Waiters are served in order; a cancelled waiter never takes it. Not re-entrant: a holder must not acquire it again.
/// </summary>
public sealed class DesktopLease
{
    public const string NativeTaskHolder = "native-task";
    public const string WaitingLine = "Waiting for the screen";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _holder;

    /// <summary>Who holds it now (a job id or <see cref="NativeTaskHolder"/>); null when free.</summary>
    public string? Holder => Volatile.Read(ref _holder);

    /// <summary>Raised on the acquiring or releasing thread, after <see cref="Holder"/> changed (before the new holder proceeds).</summary>
    public event Action? Changed;

    /// <summary>Waits for the lease. Disposing the result releases it (once; later disposes do nothing).</summary>
    public async Task<IDisposable> AcquireAsync(string holder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(holder);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _holder, holder);
        try { Changed?.Invoke(); }
        catch { Release(); throw; } // a failing listener must not keep the screen held forever
        return new Hold(this);
    }

    private void Release()
    {
        Volatile.Write(ref _holder, null);
        _gate.Release();
        Changed?.Invoke();
    }

    private sealed class Hold(DesktopLease lease) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) lease.Release(); }
    }
}
