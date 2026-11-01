using System.Runtime.InteropServices;
using System.Text;
using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed partial class TrayApplicationContext
{
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int size);
    private const int DwmwaExtendedFrameBounds = 9, DwmwaCloaked = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    /// <summary>Talk screenshots cover every display without OpenClicky's own windows (the agent keeps the plain capture).
    /// Affinity is read and changed on the UI thread, which owns those windows.</summary>
    private IScreenCaptureService CreateTalkCapture() => new OwnWindowsExcludedCapture(_capture, OwnWindowsForCapture,
        h => OnUiThread(() => GetWindowDisplayAffinity(h, out var affinity) ? affinity : (uint?)null, null),
        (h, affinity) => OnUiThread(() => SetWindowDisplayAffinity(h, affinity), false), _overlay.Hide, Log);

    /// <summary>Runs a Win32 call on the UI thread; a failed marshal (shutdown) returns <paramref name="failed"/>.</summary>
    private T OnUiThread<T>(Func<T> call, T failed)
    {
        var result = failed;
        try { _uiContext.Send(_ => result = call(), null); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("capture_affinity_marshal_error=" + ex.GetType().Name); }
        return result;
    }

    /// <summary>
    /// Other processes' visible, non-minimised, non-cloaked top-level windows that have a title, with their visible bounds
    /// (DWM frame, so a maximised window does not spill onto the next display). Any thread; throws when Windows cannot list
    /// them, so the Talk turn sends nothing.
    /// </summary>
    private static IReadOnlyList<VisibleWindow> OtherVisibleWindows()
    {
        var mine = (uint)Environment.ProcessId;
        var windows = new List<VisibleWindow>();
        var title = new StringBuilder(256);
        var listed = EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == mine) return true;
            if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true; // other virtual desktop, hidden UWP
            var length = GetWindowTextLength(hwnd);
            if (length <= 0) return true;
            title.Clear();
            title.EnsureCapacity(length + 1);
            if (GetWindowText(hwnd, title, title.Capacity) <= 0) return true;
            if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out NativeRect r, Marshal.SizeOf<NativeRect>()) != 0
                && !GetWindowRect(hwnd, out r)) return true;
            windows.Add(new(title.ToString(), (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
            return true;
        }, 0);
        if (!listed) throw new InvalidOperationException("EnumWindows failed.");
        return windows;
    }

    /// <summary>
    /// Every visible top-level window of this process (shell, dock, forms, confirm dialogs, menus, setup windows),
    /// plus every companion overlay window. EnumWindows touches no WinForms handles, so it is safe off the UI thread;
    /// only the overlay handles are read through the UI thread.
    /// </summary>
    private IReadOnlyList<OwnWindow> OwnWindowsForCapture()
    {
        var overlay = new HashSet<nint>();
        _uiContext.Send(_ =>
        {
            foreach (var hwnd in _overlay.WindowHandles)
                if (hwnd != 0) overlay.Add(hwnd);
        }, null);
        var mine = (uint)Environment.ProcessId;
        var windows = new List<OwnWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid == mine && !overlay.Contains(hwnd))
                windows.Add(new(hwnd, false));
            return true;
        }, 0);
        windows.AddRange(overlay.Select(h => new OwnWindow(h, true)));
        return windows;
    }
}
