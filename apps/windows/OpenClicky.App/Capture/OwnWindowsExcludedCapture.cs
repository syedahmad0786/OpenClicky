using System.Runtime.InteropServices;
using OpenClicky.Core;

namespace OpenClicky.App.Capture;

/// <summary>A window of this process that must not appear in a Talk screenshot.</summary>
internal readonly record struct OwnWindow(nint Handle, bool IsBuddyOverlay);

/// <summary>
/// Captures every monitor without OpenClicky's own windows: each one gets WDA_EXCLUDEFROMCAPTURE for the duration of the
/// capture and its previous affinity (including the user's "show in recordings" choice) is restored afterwards.
/// Region captures pass straight through.
/// </summary>
internal sealed class OwnWindowsExcludedCapture : IScreenCaptureService
{
    private readonly IScreenCaptureService _inner;
    private readonly Func<IReadOnlyList<OwnWindow>> _windows;
    private readonly Action _hideOverlay;
    private readonly Action<string> _log;
    private readonly TimeSpan _settle;

    /// <param name="windows">Our visible windows; called on the capture thread, so it must marshal to the UI thread itself.</param>
    /// <param name="hideOverlay">Fallback for the buddy overlay when its affinity cannot be changed.</param>
    /// <param name="settle">How long DWM needs to drop the excluded windows from the frame.</param>
    public OwnWindowsExcludedCapture(IScreenCaptureService inner, Func<IReadOnlyList<OwnWindow>> windows,
        Action hideOverlay, Action<string> log, TimeSpan? settle = null)
    {
        _inner = inner;
        _windows = windows;
        _hideOverlay = hideOverlay;
        _log = log;
        _settle = settle ?? TimeSpan.FromMilliseconds(120);
    }

    public Task<ScreenCaptureResult?> CaptureRegionJpegAsync((int X, int Y, int Width, int Height) bounds, CancellationToken cancellationToken = default)
        => _inner.CaptureRegionJpegAsync(bounds, cancellationToken);

    public async Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OwnWindow> own;
        try { own = _windows(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _log("capture_own_windows_error=" + ex.GetType().Name); own = []; }

        var scope = new DisplayAffinityScope(own.Select(w => w.Handle), GetAffinity, SetAffinity);
        try
        {
            if (scope.Failed.Count > 0)
            {
                _log($"capture_affinity_failed={scope.Failed.Count}");
                if (own.Any(w => w.IsBuddyOverlay && scope.Failed.Contains(w.Handle))) _hideOverlay();
            }
            // ponytail: fixed 120 ms; DWM has no "frame presented" signal for a display-affinity change.
            if (scope.ChangedCount > 0 || scope.Failed.Count > 0) await Task.Delay(_settle, cancellationToken).ConfigureAwait(false);
            return await _inner.CaptureAllMonitorsJpegAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            scope.Dispose(); // restores every changed window to its exact previous affinity, also when the capture throws or is cancelled
            if (scope.RestoreFailures > 0) _log($"capture_affinity_restore_failed={scope.RestoreFailures}");
        }
    }

    private static uint? GetAffinity(nint hwnd) => GetWindowDisplayAffinity(hwnd, out var affinity) ? affinity : null;
    private static bool SetAffinity(nint hwnd, uint affinity) => SetWindowDisplayAffinity(hwnd, affinity);

    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
