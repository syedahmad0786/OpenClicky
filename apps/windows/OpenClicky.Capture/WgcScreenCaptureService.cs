using System.Windows.Forms;
using OpenClicky.Core;

namespace OpenClicky.Capture;

/// <summary>
/// Preferred capture entry point: Windows.Graphics.Capture + D3D11 frame-pool when available,
/// otherwise GDI BitBlt. Captures stay in memory — nothing is written to disk.
/// </summary>
public sealed class WgcScreenCaptureService : IScreenCaptureService, IDisposable
{
    private readonly GdiScreenCaptureService _fallback = new();
    private readonly Action<string>? _log;
    private readonly Func<bool>? _forceGdi;
    private readonly object _gate = new();
    private WgcFrameCapturer? _capturer;
    private bool _probed;
    private bool _disposed;

    public WgcScreenCaptureService(Action<string>? log = null, Func<bool>? forceGdi = null)
    {
        _log = log;
        _forceGdi = forceGdi;
    }

    /// <summary>True when WGC + D3D11 device initialized successfully.</summary>
    public bool GraphicsCaptureSupported { get; private set; }

    /// <summary>Backend used for the last capture: "wgc" or "gdi".</summary>
    public string LastBackend { get; private set; } = "gdi";

    public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_forceGdi?.Invoke() == true)
        {
            LastBackend = "gdi";
            _log?.Invoke("capture_backend=gdi (forced)");
            return _fallback.CaptureAllMonitorsJpegAsync(cancellationToken);
        }

        EnsureCapturer();

        if (_capturer is null)
        {
            LastBackend = "gdi";
            _log?.Invoke("capture_backend=gdi (wgc unavailable)");
            return _fallback.CaptureAllMonitorsJpegAsync(cancellationToken);
        }

        try
        {
            var results = CaptureWithWgc(cancellationToken);
            LastBackend = "wgc";
            _log?.Invoke($"capture_backend=wgc monitors={results.Count}");
            return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>(results);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"wgc_capture_error={ex.Message}; falling back to gdi");
            LastBackend = "gdi";
            return _fallback.CaptureAllMonitorsJpegAsync(cancellationToken);
        }
    }

    private List<ScreenCaptureResult> CaptureWithWgc(CancellationToken cancellationToken)
    {
        var capturer = _capturer ?? throw new InvalidOperationException("WGC capturer not initialized.");
        var cursor = Cursor.Position;
        var screens = Screen.AllScreens
            .OrderByDescending(s => s.Bounds.Contains(cursor))
            .ThenBy(s => s.Bounds.Left)
            .ToArray();

        var results = new List<ScreenCaptureResult>(screens.Length);
        for (var index = 0; index < screens.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var screen = screens[index];
            var bounds = screen.Bounds;
            var isCursor = bounds.Contains(cursor);
            var (jpeg, pixelW, pixelH) = capturer.CaptureMonitorJpeg(
                screen,
                maxEdge: 1280,
                timeout: TimeSpan.FromSeconds(2));

            var label = screens.Length == 1
                ? "user's screen (cursor is here)"
                : isCursor
                    ? $"screen {index + 1} of {screens.Length} — cursor is on this screen (primary focus)"
                    : $"screen {index + 1} of {screens.Length} — secondary screen";

            results.Add(new ScreenCaptureResult(
                JpegBytes: jpeg,
                PixelWidth: pixelW,
                PixelHeight: pixelH,
                MonitorLeft: bounds.Left,
                MonitorTop: bounds.Top,
                MonitorWidth: bounds.Width,
                MonitorHeight: bounds.Height,
                IsCursorMonitor: isCursor,
                Label: label));
        }

        return results;
    }

    private void EnsureCapturer()
    {
        if (_probed)
        {
            return;
        }

        lock (_gate)
        {
            if (_probed)
            {
                return;
            }

            _probed = true;
            if (WgcFrameCapturer.TryCreate(out var capturer, out var error))
            {
                _capturer = capturer;
                GraphicsCaptureSupported = true;
                _log?.Invoke("wgc_init=ok");
            }
            else
            {
                GraphicsCaptureSupported = false;
                _log?.Invoke($"wgc_init_failed={error}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _capturer?.Dispose();
            _capturer = null;
        }
    }
}
