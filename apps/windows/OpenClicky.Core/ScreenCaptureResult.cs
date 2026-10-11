namespace OpenClicky.Core;

public sealed record ScreenCaptureResult(
    byte[] JpegBytes,
    int PixelWidth,
    int PixelHeight,
    int MonitorLeft,
    int MonitorTop,
    int MonitorWidth,
    int MonitorHeight,
    bool IsCursorMonitor,
    string Label)
{
    public bool LooksLikeJpeg =>
        JpegBytes.Length >= 2 && JpegBytes[0] == 0xFF && JpegBytes[1] == 0xD8;
}
