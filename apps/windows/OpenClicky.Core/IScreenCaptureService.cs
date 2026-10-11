namespace OpenClicky.Core;

public interface IScreenCaptureService
{
    Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken cancellationToken = default);
}
