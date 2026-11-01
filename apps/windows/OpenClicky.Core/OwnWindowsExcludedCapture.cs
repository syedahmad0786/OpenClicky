namespace OpenClicky.Core;

/// <summary>A window of this process that must not appear in a Talk screenshot.</summary>
public readonly record struct OwnWindow(nint Handle, bool IsBuddyOverlay);

/// <summary>
/// Captures every monitor without OpenClicky's own windows: each one gets WDA_EXCLUDEFROMCAPTURE for the duration of the
/// capture and its previous affinity (including the user's "show in recordings" choice) is restored afterwards.
/// Region captures pass straight through. The Win32 calls are injected so the policy is testable.
/// </summary>
public sealed class OwnWindowsExcludedCapture : IScreenCaptureService
{
    private readonly IScreenCaptureService _inner;
    private readonly Func<IReadOnlyList<OwnWindow>> _windows;
    private readonly Func<nint, uint?> _getAffinity;
    private readonly Func<nint, uint, bool> _setAffinity;
    private readonly Action _hideOverlay;
    private readonly Action<string> _log;
    private readonly TimeSpan _settle;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    // One capture at a time: an overlapping turn must not restore affinity while another capture is still running.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="windows">Our visible windows; called on the capture thread, so it must not touch WinForms handles unmarshalled.</param>
    /// <param name="getAffinity">GetWindowDisplayAffinity, or null when it cannot be read.</param>
    /// <param name="setAffinity">SetWindowDisplayAffinity; false when Windows refused.</param>
    /// <param name="hideOverlay">Fallback for the buddy overlay when its affinity cannot be changed.</param>
    /// <param name="settle">How long DWM needs to drop the excluded windows from the frame.</param>
    public OwnWindowsExcludedCapture(IScreenCaptureService inner, Func<IReadOnlyList<OwnWindow>> windows,
        Func<nint, uint?> getAffinity, Func<nint, uint, bool> setAffinity, Action hideOverlay, Action<string> log,
        TimeSpan? settle = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _inner = inner;
        _windows = windows;
        _getAffinity = getAffinity;
        _setAffinity = setAffinity;
        _hideOverlay = hideOverlay;
        _log = log;
        _settle = settle ?? TimeSpan.FromMilliseconds(120);
        _delay = delay ?? Task.Delay;
    }

    public Task<ScreenCaptureResult?> CaptureRegionJpegAsync((int X, int Y, int Width, int Height) bounds, CancellationToken cancellationToken = default)
        => _inner.CaptureRegionJpegAsync(bounds, cancellationToken);

    public async Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<OwnWindow> own;
            var listFailed = false;
            try { own = _windows(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Unknown windows count as failed ones: at least the buddy overlay is hidden.
                _log("capture_own_windows_error=" + ex.GetType().Name);
                own = [];
                listFailed = true;
            }

            var scope = new DisplayAffinityScope(own.Select(w => w.Handle), _getAffinity, _setAffinity);
            try
            {
                if (scope.Failed.Count > 0) _log($"capture_affinity_failed={scope.Failed.Count}");
                if (listFailed || own.Any(w => w.IsBuddyOverlay && scope.Failed.Contains(w.Handle))) _hideOverlay();
                // ponytail: fixed 120 ms; DWM has no "frame presented" signal for a display-affinity change.
                if (scope.ChangedCount > 0 || scope.Failed.Count > 0 || listFailed) await _delay(_settle, cancellationToken).ConfigureAwait(false);
                return await _inner.CaptureAllMonitorsJpegAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scope.Dispose(); // restores every changed window to its exact previous affinity, also when the capture throws or is cancelled
                if (scope.RestoreFailures > 0) _log($"capture_affinity_restore_failed={scope.RestoreFailures}");
            }
        }
        finally { _gate.Release(); }
    }
}
