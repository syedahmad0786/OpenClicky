using System.Runtime.InteropServices;
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

    /// <summary>Talk screenshots cover every display without OpenClicky's own windows (the agent keeps the plain capture).</summary>
    private IScreenCaptureService CreateTalkCapture() => new OwnWindowsExcludedCapture(_capture, OwnWindowsForCapture,
        h => GetWindowDisplayAffinity(h, out var affinity) ? affinity : null, SetWindowDisplayAffinity, _overlay.Hide, Log);

    /// <summary>
    /// Every visible top-level window of this process (shell, dock, forms, confirm dialogs, menus, setup windows),
    /// plus the buddy overlay and its tip. EnumWindows touches no WinForms handles, so it is safe off the UI thread;
    /// only the overlay handles are read through the UI thread.
    /// </summary>
    private IReadOnlyList<OwnWindow> OwnWindowsForCapture()
    {
        var overlay = new HashSet<nint>();
        _uiContext.Send(_ =>
        {
            foreach (var hwnd in new[] { _overlay.WindowHandle, _overlay.BuddyTipHandle })
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
