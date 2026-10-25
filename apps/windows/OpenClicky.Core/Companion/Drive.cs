namespace OpenClicky.Core.CompanionCursor;

public enum DrivePhase { Idle, AwaitingConsent, Driving, TakenBack, Ended }

public enum DriveEnd { Finished, Declined, UserTookOver, Stopped, SafetyStop, Failed }

public enum PhysicalKind { MouseMove, MouseButton, Key }

/// <summary>
/// One agent task's use of the mouse and keyboard. It is created with the task's access snapshot, which it never changes:
/// consent only lets the task drive, it never widens what the task may do. The user's physical input takes control back.
/// Thread-safe: raw input arrives on the UI thread, agent actions on the thread pool.
/// </summary>
public sealed class DriveSession(AgentAccessMode accessSnapshot, Func<CancellationToken, Task<bool>> askConsent, TimeSpan? consentTimeout = null)
{
    public const int TakeBackDistancePx = 40;
    public static readonly TimeSpan ConsentTimeout = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly TimeSpan _timeout = consentTimeout ?? ConsentTimeout;
    private Task<bool>? _consent;
    private (int X, int Y)? _baseline;
    private bool _agentActing;
    private DrivePhase _phase;
    private DriveEnd? _endedBy;

    public AgentAccessMode Access { get; } = accessSnapshot;
    public DrivePhase Phase { get { lock (_gate) return _phase; } }
    public DriveEnd? EndedBy { get { lock (_gate) return _endedBy; } }

    /// <summary>
    /// True once the user said yes. Preview never asks and never drives; otherwise the user is asked once per session and the
    /// answer is kept. No, or no answer within 60 s, ends the session as declined. Each caller's own token cancels its wait.
    /// </summary>
    public Task<bool> EnsureConsentAsync(CancellationToken ct)
    {
        if (Access == AgentAccessMode.Preview) return Task.FromResult(false);
        lock (_gate)
        {
            if (_consent is null)
            {
                if (_phase == DrivePhase.Ended) return Task.FromResult(false);
                _phase = DrivePhase.AwaitingConsent;
                _consent = AskAsync(ct);
            }
            return _consent.WaitAsync(ct);
        }
    }

    private async Task<bool> AskAsync(CancellationToken ct)
    {
        bool yes;
        // On timeout the prompt is cancelled too, so its card or dialog comes down.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try { yes = await askConsent(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { yes = false; }
        lock (_gate)
        {
            if (yes && _phase == DrivePhase.AwaitingConsent) { _phase = DrivePhase.Driving; return true; }
        }
        End(DriveEnd.Declined);
        return false;
    }

    /// <summary>
    /// Physical (never injected) input at the cursor position. True when it takes control back: only while driving, for any key or
    /// button, or a mouse move of 40 px or more from the baseline. A move while an agent action is in flight never counts (the
    /// cursor may be where the agent put it); the first move with no baseline yet becomes the baseline.
    /// </summary>
    public bool OnPhysicalInput(PhysicalKind kind, int cursorX, int cursorY)
    {
        lock (_gate)
        {
            if (_phase != DrivePhase.Driving) return false;
            if (kind == PhysicalKind.MouseMove)
            {
                if (_agentActing) return false;
                if (_baseline is not { } b) { _baseline = (cursorX, cursorY); return false; }
                long dx = cursorX - b.X, dy = cursorY - b.Y;
                if (dx * dx + dy * dy < (long)TakeBackDistancePx * TakeBackDistancePx) return false;
            }
            _phase = DrivePhase.TakenBack;
            return true;
        }
    }

    /// <summary>An agent action is about to send input: its own cursor moves are not the user's until <see cref="NoteAgentCursor"/>.</summary>
    public void BeginAgentAction() { lock (_gate) _agentActing = true; }

    /// <summary>After each agent action and at the drive start: the cursor is where the agent left it.</summary>
    public void NoteAgentCursor(int x, int y) { lock (_gate) { _agentActing = false; _baseline = (x, y); } }

    /// <summary>The user's pointer is on OpenClicky's own window or a confirm dialog: the baseline follows it.</summary>
    public void Rebase(int x, int y) { lock (_gate) _baseline = (x, y); }

    /// <summary>OpenClicky stopped driving on its own (the window refused input): held like a take-back until the user resumes.</summary>
    public void Suspend() { lock (_gate) if (_phase == DrivePhase.Driving) _phase = DrivePhase.TakenBack; }

    /// <summary>The user resumed after taking over: driving again, watched from where the cursor is now.</summary>
    public void Resume(int x, int y)
    {
        lock (_gate)
        {
            if (_phase != DrivePhase.TakenBack) return;
            _phase = DrivePhase.Driving;
            _agentActing = false;
            _baseline = (x, y);
        }
    }

    /// <summary>Ends the session; the first reason is kept.</summary>
    public void End(DriveEnd reason)
    {
        lock (_gate)
        {
            if (_phase == DrivePhase.Ended) return;
            _phase = DrivePhase.Ended;
            _endedBy = reason;
        }
    }
}

public static class DriveGuard
{
    /// <summary>
    /// Agent input is never sent into OpenClicky itself: not while any of its confirm dialogs is open (another run's click or
    /// Tab+Space could answer it), and not when the foreground window or the click/scroll point (<paramref name="pointIsOwn"/>,
    /// null for keys) is one of its windows.
    /// </summary>
    public static bool RefusesOwnTarget(bool confirmOpen, bool foregroundIsOwn, bool? pointIsOwn) =>
        confirmOpen || foregroundIsOwn || pointIsOwn == true;

    /// <summary>
    /// A window whose process integrity (Low 0x1000, Medium 0x2000, High 0x3000, System 0x4000) is above ours, or cannot be
    /// read, is refused: Windows (UIPI) would silently drop our input. Never elevate or work around it.
    /// </summary>
    public static bool Refuses(int? targetIntegrityRid, int ownIntegrityRid) => targetIntegrityRid is null || targetIntegrityRid > ownIntegrityRid;
}

/// <summary>Asks for drive consent (once) before the inner per-action confirm; declining skips the inner one.</summary>
public sealed class DriveConsentConfirm(IAgentConfirm inner, DriveSession drive) : IAgentConfirm
{
    public async Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default) =>
        await drive.EnsureConsentAsync(cancellationToken).ConfigureAwait(false)
        && await inner.ConfirmAsync(summary, cancellationToken).ConfigureAwait(false);

    /// <summary>Opening an app (or a connector call) sends no input to the foreground control: it is not driving.</summary>
    public Task<bool> ConfirmNonInputAsync(string summary, CancellationToken cancellationToken = default) =>
        inner.ConfirmNonInputAsync(summary, cancellationToken);
}
