using System.Runtime.InteropServices;
using System.Text;

namespace OpenClicky.Agent;

/// <summary>Read the foreground window title for soft agent deny-list checks.</summary>
public static class ForegroundWindowInfo
{
    public static IntPtr GetHandle() => GetForegroundWindow();
    public static bool IsCurrentProcessForeground() => IsCurrentProcessWindow(GetForegroundWindow());
    public static bool IsCompanionCaptureEligible() => !IsCompanionExcludedWindow(GetForegroundWindow());
    public static bool IsCurrentProcessWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }
    public static (int X, int Y, int Width, int Height)? GetBounds()
    {
        if (!GetWindowRect(GetForegroundWindow(), out var rect)) return null;
        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public static string? GetCompanionTitle()
    {
        var hwnd = GetForegroundWindow();
        if (IsCompanionExcludedWindow(hwnd)) return "";
        return GetTitle();
    }

    private static bool IsCompanionExcludedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return true;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            if (process.ProcessName.Contains("Codex", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("OpenClicky", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("OpenClicky.App", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch (ArgumentException) { return true; }
        return false;
    }
    public static string? GetTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return null;
        }

        var sb = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
