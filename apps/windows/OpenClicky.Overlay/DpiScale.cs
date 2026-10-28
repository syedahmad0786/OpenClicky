using System.Runtime.InteropServices;

namespace OpenClicky.Overlay;

/// <summary>DIP-to-pixel scale of the monitor under a virtual-screen point (the app is PerMonitorV2).</summary>
internal static class DpiScale
{
    public static float ForPoint(Point virtualPoint)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = virtualPoint.X, Y = virtualPoint.Y }, MonitorDefaultToNearest);
        return monitor != 0 && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96f : 1f;
    }

    private const int MonitorDefaultToNearest = 2, MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativePoint pt, int flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
