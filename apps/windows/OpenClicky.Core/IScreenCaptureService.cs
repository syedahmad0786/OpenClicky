namespace OpenClicky.Core;

public interface IScreenCaptureService
{
    Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken cancellationToken = default);
    Task<ScreenCaptureResult?> CaptureRegionJpegAsync((int X, int Y, int Width, int Height) bounds, CancellationToken cancellationToken = default)
        => Task.FromResult<ScreenCaptureResult?>(null);
}
