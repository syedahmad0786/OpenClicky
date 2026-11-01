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

    /// <summary>
    /// The reverse of <see cref="ToVirtualScreen"/>: the 1-based screen whose monitor holds the virtual-screen pixel (half-open)
    /// and the screenshot pixel there; null when no captured screen holds it.
    /// </summary>
    public static (int Screen, int X, int Y)? ToScreenshot(int virtualX, int virtualY, IReadOnlyList<ScreenCaptureResult> screens)
    {
        for (var i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            if (s.PixelWidth <= 0 || s.PixelHeight <= 0 || s.MonitorWidth <= 0 || s.MonitorHeight <= 0
                || virtualX < s.MonitorLeft || virtualX >= (long)s.MonitorLeft + s.MonitorWidth
                || virtualY < s.MonitorTop || virtualY >= (long)s.MonitorTop + s.MonitorHeight) continue;
            var (x, y) = ToPixels(s, virtualX, virtualY);
            return (i + 1, x, y);
        }
        return null;
    }

    /// <summary>A virtual-screen point in <paramref name="s"/>'s screenshot pixels, clamped to the image.</summary>
    internal static (int X, int Y) ToPixels(ScreenCaptureResult s, double virtualX, double virtualY) =>
        ((int)Math.Clamp(Math.Floor((virtualX - s.MonitorLeft) * s.PixelWidth / Math.Max(1, s.MonitorWidth)), 0, Math.Max(0, s.PixelWidth - 1)),
         (int)Math.Clamp(Math.Floor((virtualY - s.MonitorTop) * s.PixelHeight / Math.Max(1, s.MonitorHeight)), 0, Math.Max(0, s.PixelHeight - 1)));
}
