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
        if (point.IsNone || point.X is null || point.Y is null || screens.Count == 0
            || !double.IsFinite(point.X.Value) || !double.IsFinite(point.Y.Value)
            || point.ScreenNumber is int number && (number < 1 || number > screens.Count))
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

        if (target.PixelWidth <= 0 || target.PixelHeight <= 0 || target.MonitorWidth <= 0 || target.MonitorHeight <= 0
            || point.X < 0 || point.Y < 0 || point.X >= target.PixelWidth || point.Y >= target.PixelHeight)
            return null;
        var scaleX = target.MonitorWidth / (double)target.PixelWidth;
        var scaleY = target.MonitorHeight / (double)Math.Max(1, target.PixelHeight);
        var x = target.MonitorLeft + Math.Min(target.MonitorWidth - 1, (int)Math.Round(point.X.Value * scaleX));
        var y = target.MonitorTop + Math.Min(target.MonitorHeight - 1, (int)Math.Round(point.Y.Value * scaleY));
        return (x, y);
    }
}
