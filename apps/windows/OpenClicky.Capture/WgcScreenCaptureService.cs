using OpenClicky.Core;

namespace OpenClicky.Capture;

/// <summary>
/// Placeholder for a full Windows.Graphics.Capture path.
/// Phase A wires <see cref="GdiScreenCaptureService"/> so the silent demo is reliable;
/// this type documents the intended WGC entry point for Phase B hardening.
/// </summary>
public sealed class WgcScreenCaptureService : IScreenCaptureService
{
    private readonly GdiScreenCaptureService _fallback = new();

    public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken cancellationToken = default)
    {
        // TODO(phase-b): implement monitor items via GraphicsCaptureItem + D3D11 frame pool.
        // Fallback keeps Phase A exit criteria unblocked on machines where WGC picker UX is awkward.
        return _fallback.CaptureAllMonitorsJpegAsync(cancellationToken);
    }
}
