using System.Runtime.InteropServices;
using OpenClicky.Core;

namespace OpenClicky.Agent;

/// <summary>
/// Captures and restores the interactive desktop focus around Agent confirm UI.
/// MessageBox / modal dialogs steal foreground; SendInput (win+r, type_text) then
/// hits the tray host instead of the intended desktop target unless we restore.
/// </summary>
public static class ForegroundFocusRestorer
{
    /// <summary>Settle delay after SetForegroundWindow so the next SendInput lands correctly.</summary>
    public const int SettleMilliseconds = 120;

    public static IntPtr Capture() => NativeMethods.GetForegroundWindow();

    /// <summary>
    /// Restore keyboard focus to <paramref name="prior"/> when it is a living window
    /// outside this process; otherwise fall back to the shell window (desktop).
    /// </summary>
    public static void Restore(IntPtr prior)
    {
        var target = (IntPtr)ForegroundRestorePolicy.ResolveTarget(
            prior,
            NativeMethods.IsWindow(prior),
            IsCurrentProcess(prior),
            NativeMethods.GetShellWindow());

        if (target != IntPtr.Zero)
        {
            ForceForeground(target);
        }

        Thread.Sleep(SettleMilliseconds);
    }

    private static bool IsCurrentProcess(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && pid == (uint)Environment.ProcessId;
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        }

        var fore = NativeMethods.GetForegroundWindow();
        var foreThread = NativeMethods.GetWindowThreadProcessId(fore, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();

        var attachedFore = false;
        var attachedTarget = false;
        try
        {
            if (foreThread != 0 && foreThread != currentThread)
            {
                attachedFore = NativeMethods.AttachThreadInput(currentThread, foreThread, true);
            }

            if (targetThread != 0 && targetThread != currentThread && targetThread != foreThread)
            {
                attachedTarget = NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            }

            _ = NativeMethods.BringWindowToTop(hwnd);
            _ = NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                _ = NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedFore)
            {
                _ = NativeMethods.AttachThreadInput(currentThread, foreThread, false);
            }
        }
    }

    private static class NativeMethods
    {
        public const int SwRestore = 9;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    }
}
