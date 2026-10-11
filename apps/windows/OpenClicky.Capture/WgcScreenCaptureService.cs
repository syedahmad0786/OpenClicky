using OpenClicky.Core;

namespace OpenClicky.Capture;

/// <summary>
/// Preferred capture entry point. Probes Windows.Graphics.Capture availability,
/// then uses the reliable GDI BitBlt path until a full D3D11 frame-pool lands.
/// Captures stay in memory — nothing is written to disk.
/// </summary>
public sealed class WgcScreenCaptureService : IScreenCaptureService
{
    private readonly GdiScreenCaptureService _fallback = new();
    private readonly Action<string>? _log;
    private bool _probed;

    public WgcScreenCaptureService(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>True when the OS reports Graphics Capture is supported.</summary>
    public bool GraphicsCaptureSupported { get; private set; }

    /// <summary>Backend used for the last capture ("gdi" today; "wgc" when frame-pool ships).</summary>
    public string LastBackend { get; private set; } = "gdi";

    public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureProbed();
        // Full WGC frame-pool (Direct3D11CaptureFramePool + per-monitor GraphicsCaptureItem)
        // still needs a D3D11 WinRT device bridge (SharpDX/Vortice). Until that lands on a
        // Windows smoke machine, GDI remains the production path so Talk/Agent stay reliable.
        LastBackend = "gdi";
        _log?.Invoke(
            GraphicsCaptureSupported
                ? "capture_backend=gdi (wgc supported — frame-pool pending)"
                : "capture_backend=gdi (wgc unsupported on this session)");
        return _fallback.CaptureAllMonitorsJpegAsync(cancellationToken);
    }

    private void EnsureProbed()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;
        try
        {
            GraphicsCaptureSupported = ProbeGraphicsCaptureSupported();
        }
        catch (Exception ex)
        {
            GraphicsCaptureSupported = false;
            _log?.Invoke($"wgc_probe_error={ex.Message}");
        }
    }

    private static bool ProbeGraphicsCaptureSupported()
    {
        // Windows.Graphics.Capture is a WinRT type available on net8.0-windows10.0.19041+.
        // IsSupported() is cheap and does not require a D3D device.
        return Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();
    }
}
