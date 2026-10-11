using System.Runtime.InteropServices;
using OpenClicky.Overlay;

namespace OpenClicky.Overlay.Smoke;

// Windows CI / local proof: real BuddyOverlayController POINT fly-to to a known
// virtual-screen coordinate, then assert the buddy tip HWND landed there.
// No screenshots. Does NOT exercise tray, PTT, Talk, STT, vision, or TTS.
internal static class Program
{
    private const int SettleTolerancePx = 8;

    [STAThread]
    private static int Main()
    {
        Console.WriteLine("OpenClicky overlay POINT smoke starting…");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var vs = SystemInformation.VirtualScreen;
        // Stay well inside the primary virtual bounds so DPI / multi-monitor edges don't bite.
        var targetX = vs.Left + Math.Clamp(vs.Width / 4, 80, Math.Max(80, vs.Width - 80));
        var targetY = vs.Top + Math.Clamp(vs.Height / 4, 80, Math.Max(80, vs.Height - 80));
        Console.WriteLine($"virtual_screen=[{vs.Left},{vs.Top},{vs.Width}x{vs.Height}] target=({targetX},{targetY})");

        using var overlay = new BuddyOverlayController();
        if (overlay.WindowHandle == IntPtr.Zero)
        {
            Console.Error.WriteLine("FAIL: overlay HWND was not created");
            return 1;
        }

        Console.WriteLine($"overlay_hwnd=0x{overlay.WindowHandle:X}");

        // Fly-to, then ShowResponding (Talk sequencing) — tip must stay at the POINT.
        overlay.PointAt(targetX, targetY, "ci-point");
        overlay.ShowResponding("overlay smoke");

        if (!PumpUntil(() => overlay.IsPointFlyComplete && overlay.BuddyTipHandle != IntPtr.Zero, TimeSpan.FromSeconds(5)))
        {
            Console.Error.WriteLine(
                $"FAIL: fly-to did not settle (complete={overlay.IsPointFlyComplete} tip=0x{overlay.BuddyTipHandle:X})");
            return 2;
        }

        var tip = overlay.BuddyTipHandle;
        if (!NativeMethods.GetWindowRect(tip, out var rect))
        {
            Console.Error.WriteLine($"FAIL: GetWindowRect tip failed win32={Marshal.GetLastWin32Error()}");
            return 3;
        }

        Console.WriteLine($"buddy_tip_hwnd=0x{tip:X} rect=[{rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top}]");

        var dx = Math.Abs(rect.Left - targetX);
        var dy = Math.Abs(rect.Top - targetY);
        if (dx > SettleTolerancePx || dy > SettleTolerancePx)
        {
            Console.Error.WriteLine(
                $"FAIL: buddy tip HWND at ({rect.Left},{rect.Top}) not within ±{SettleTolerancePx}px of ({targetX},{targetY}) dx={dx} dy={dy}");
            return 4;
        }

        if (!overlay.TryGetBuddyVirtualPoint(out var buddyX, out var buddyY))
        {
            Console.Error.WriteLine("FAIL: TryGetBuddyVirtualPoint returned false after settle");
            return 5;
        }

        if (Math.Abs(buddyX - targetX) > SettleTolerancePx || Math.Abs(buddyY - targetY) > SettleTolerancePx)
        {
            Console.Error.WriteLine(
                $"FAIL: buddy virtual point ({buddyX},{buddyY}) mismatch target ({targetX},{targetY})");
            return 6;
        }

        // Host overlay HWND must remain a real top-level window covering the virtual screen.
        if (!NativeMethods.GetWindowRect(overlay.WindowHandle, out var host))
        {
            Console.Error.WriteLine("FAIL: GetWindowRect overlay host failed");
            return 7;
        }

        if (host.Left > targetX || host.Top > targetY || host.Right <= targetX || host.Bottom <= targetY)
        {
            Console.Error.WriteLine(
                $"FAIL: overlay host HWND [{host.Left},{host.Top},{host.Right},{host.Bottom}] does not contain target");
            return 8;
        }

        Console.WriteLine(
            $"PASS tip=({rect.Left},{rect.Top}) buddy=({buddyX},{buddyY}) target=({targetX},{targetY}) tol={SettleTolerancePx}");
        overlay.Hide();
        Application.DoEvents();
        return 0;
    }

    private static bool PumpUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            if (predicate())
            {
                // Extra frames so SetWindowPos for the tip catches the final anchor.
                for (var i = 0; i < 4; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(16);
                }

                return predicate();
            }

            Thread.Sleep(16);
        }

        Application.DoEvents();
        return predicate();
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
    }
}
