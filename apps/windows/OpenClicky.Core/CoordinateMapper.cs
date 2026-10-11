namespace OpenClicky.Core;

/// <summary>
/// Maps screenshot-pixel POINT coords (top-left origin) to virtual-screen pixels on Windows.
/// </summary>
public static class CoordinateMapper
{
    public static (int virtualX, int virtualY)? ToVirtualScreen(
        PointingParseResult point,
        IReadOnlyList<ScreenCaptureResult> screens)
    {
        if (point.IsNone || point.X is null || point.Y is null || screens.Count == 0)
        {
            return null;
        }

        ScreenCaptureResult? target = null;
        if (point.ScreenNumber is int n && n >= 1 && n <= screens.Count)
        {
            target = screens[n - 1];
        }
        else
        {
            target = screens.FirstOrDefault(s => s.IsCursorMonitor) ?? screens[0];
        }

        var scaleX = target.MonitorWidth / (double)Math.Max(1, target.PixelWidth);
        var scaleY = target.MonitorHeight / (double)Math.Max(1, target.PixelHeight);
        var x = target.MonitorLeft + (int)Math.Round(point.X.Value * scaleX);
        var y = target.MonitorTop + (int)Math.Round(point.Y.Value * scaleY);
        return (x, y);
    }
}
