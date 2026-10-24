using System.Runtime.InteropServices;
using System.Text;

namespace OpenClicky.Overlay;

/// <summary>Whether the foreground window is a full-screen app (a game, a video, F11): it covers its whole monitor and has no title bar.</summary>
internal static class ForegroundFullscreen
{
    private const int GwlStyle = -16, WsCaption = 0x00C00000, MonitorDefaultToNearest = 2;

    public static bool IsActive()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return false;
        // A maximised ordinary window has a title bar; the desktop and the taskbar cover a monitor without being an app.
        if ((GetWindowLong(hwnd, GwlStyle) & WsCaption) == WsCaption) return false;
        var name = new StringBuilder(64);
        if (GetClassName(hwnd, name, name.Capacity) > 0 && name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info)) return false;
        return rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
