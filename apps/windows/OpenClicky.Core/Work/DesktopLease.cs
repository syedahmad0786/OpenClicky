using OpenClicky.Core.CompanionCursor;

namespace OpenClicky.Core.Work;

/// <summary>
/// The screen and input belong to one run at a time. A job takes the lease for each desktop tool call; a native task holds it for its
/// whole run. Waiters are served in order; a cancelled waiter never takes it. Not re-entrant: a holder must not acquire it again.
/// </summary>
public sealed class DesktopLease
{
    public const string NativeTaskHolder = "native-task";
    public const string WaitingLine = "Waiting for the screen";
    /// <summary>A paused job that holds the lease (it paused inside a desktop call): other jobs wait for it.</summary>
    public const string PausedHolderLine = "Paused — holding the screen";
    /// <summary>How long the last holder still counts as the run on the screen after it gave the lease back, when it is not driving.</summary>
    public static readonly TimeSpan ScreenGrace = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether the run that held the lease last is still the run on the screen (the one Esc, the Esc pill and take-back act on):
    /// while it holds the lease, while its drive is active (asking consent, driving, or taken back by the user), or for
    /// <see cref="ScreenGrace"/> after it gave the lease back. Otherwise it is a background job; its card stops it.
    /// </summary>
    public static bool StillOnScreen(bool holdsLease, DrivePhase drive, DateTimeOffset? releasedAt, DateTimeOffset now) =>
        holdsLease || drive is DrivePhase.AwaitingConsent or DrivePhase.Driving or DrivePhase.TakenBack
        || (releasedAt is { } at && now - at <= ScreenGrace);

    /// <summary>Talk typing and dictation insertion hold the lease while they inject text, so a job's input waits.</summary>
    public const string TalkHolder = "talk";
    /// <summary>How long Talk typing or a dictation insert waits for a job to give the screen back before it gives up.</summary>
    public static readonly TimeSpan TalkWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A run is in the foreground (it may use the screen, so Esc, the classic window, dictation, walkthroughs, replays, guidance,
    /// drawn buttons and detaching treat OpenClicky as busy): a native task is open, or the last lease holder is still on the screen
    /// (<see cref="StillOnScreen"/>). A background job that is not on the screen never counts; only its card stops it.
    /// </summary>
    public static bool InForeground(bool nativeTaskOpen, (bool HoldsLease, DrivePhase Drive, DateTimeOffset? ReleasedAt)? lastHolder, DateTimeOffset now) =>
        nativeTaskOpen || lastHolder is { } h && StillOnScreen(h.HoldsLease, h.Drive, h.ReleasedAt, now);

    /// <summary>Waits up to <paramref name="wait"/> for the lease; null when it stayed busy (nothing taken). The caller's own cancel throws.</summary>
    public async Task<IDisposable?> TryAcquireAsync(string holder, TimeSpan wait, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        try { return await AcquireAsync(holder, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

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
