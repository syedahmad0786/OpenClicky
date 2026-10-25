namespace OpenClicky.Core;

/// <summary>
/// Keeps an overlay hidden across a capture: while a hold is open, <see cref="Allow"/> refuses (and remembers) every request to
/// show, so a pause or the end of a talk turn that calls ShowIdle mid-capture cannot put the companion back into the pixels.
/// When the last hold closes, one deferred show is run. Any thread.
/// </summary>
public sealed class OverlayHoldGate(Action showDeferred)
{
    /// <summary>A hold that holds nothing (overlays that do not defer shows).</summary>
    public static readonly IDisposable None = new NoHold();

    private readonly object _lock = new();
    private int _holds;
    private bool _deferred;

    public bool Held { get { lock (_lock) return _holds > 0; } }

    public IDisposable Hold()
    {
        lock (_lock) _holds++;
        return new Release(this);
    }

    /// <summary>False while held, and the refused show runs when the last hold closes.</summary>
    public bool Allow()
    {
        lock (_lock)
        {
            if (_holds == 0) return true;
            _deferred = true;
            return false;
        }
    }

    /// <summary>
    /// Forgets a refused show: a Hide that came later during the hold is the newest request, so the release must not undo it.
    /// </summary>
    public void CancelDeferred() { lock (_lock) _deferred = false; }

    private void Close()
    {
        bool run;
        lock (_lock)
        {
            run = --_holds == 0 && _deferred;
            if (run) _deferred = false;
        }
        if (run) showDeferred();
    }

    private sealed class Release(OverlayHoldGate gate) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) gate.Close(); }
    }

    private sealed class NoHold : IDisposable { public void Dispose() { } }
}
