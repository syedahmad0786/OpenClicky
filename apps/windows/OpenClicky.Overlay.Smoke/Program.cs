using System.Runtime.InteropServices;
using OpenClicky.Core;
using OpenClicky.Overlay;

namespace OpenClicky.Overlay.Smoke;

// Windows CI / local proof: real BuddyOverlayController POINT fly-to to a known
// virtual-screen coordinate, then assert the companion tip landed there.
// No screenshots. Does NOT exercise tray, PTT, Talk, STT, vision, or TTS.
internal static class Program
{
    private const int SettleTolerancePx = 8;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.WriteLine("OpenClicky overlay POINT smoke starting…");
        using (var stroke = new System.Drawing.Drawing2D.GraphicsPath())
        {
            stroke.AddLine(10, 20, 110, 20);
            using var half = BuddyOverlayController.PartialStroke(stroke, .5f);
            using var empty = BuddyOverlayController.PartialStroke(stroke, 0);
            using var whole = BuddyOverlayController.PartialStroke(stroke, 1);
            if (empty.PointCount != 0 || half.GetLastPoint() != new PointF(60, 20) || whole.GetLastPoint() != new PointF(110, 20))
                throw new InvalidOperationException("Progressive stroke geometry failed.");
            stroke.Reset(); stroke.AddRectangle(new Rectangle(0, 0, 100, 100));
            using var most = BuddyOverlayController.PartialStroke(stroke, .875f);
            if (most.GetLastPoint() != new PointF(0, 50)) throw new InvalidOperationException("Closing stroke progress failed.");
        }
        var geometryBounds = new Rectangle(-1600, -300, 3520, 1380);
        var clipped = ScreenRegionSelector.Normalize(
            new Point(2100, 1200), new Point(-1700, -400), geometryBounds);
        if (clipped != geometryBounds
            || ScreenRegionSelector.Normalize(new Point(-20, -20), new Point(-16, -16), geometryBounds) is not null)
        {
            Console.Error.WriteLine($"FAIL: region geometry clipping/minimum mismatch: {clipped}");
            return 1;
        }

        var placementFailure = ValidateResponseBubblePlacement();
        if (placementFailure is not null)
        {
            Console.Error.WriteLine("FAIL: response bubble placement " + placementFailure);
            return 12;
        }

        if (args.Contains("--geometry-only", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("PASS: region and response bubble geometry");
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // as the app: overlay coordinates are physical pixels
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
            return 2;
        }

        Console.WriteLine($"overlay_hwnd=0x{overlay.WindowHandle:X}");
        if (overlay.WindowHandles.Count != 2 || overlay.WindowHandles.Distinct().Count() != 2 || overlay.WindowHandles.Contains(IntPtr.Zero)
            || overlay.BuddyTipHandle != overlay.WindowHandle)
        {
            Console.Error.WriteLine($"FAIL: expected the cursor and drawing windows, got [{string.Join(",", overlay.WindowHandles)}]");
            return 2;
        }

        using var fixture = new Form
        {
            Text = "OpenClicky foreground fixture",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(vs.Left + 40, vs.Top + 40, 320, 180),
        };
        fixture.Controls.Add(new Label { Text = "Foreground retention check", AutoSize = true, Location = new Point(20, 20) });
        fixture.Show();
        fixture.Activate();
        Console.WriteLine("Activate OpenClicky foreground fixture to begin");
        if (!PumpUntil(() => NativeMethods.GetForegroundWindow() == fixture.Handle, TimeSpan.FromSeconds(30)))
        {
            Console.Error.WriteLine(
                $"FAIL: fixture did not become foreground fixture=0x{fixture.Handle:X} actual=0x{NativeMethods.GetForegroundWindow():X}");
            return 20;
        }

        if (args.Contains("--annotations", StringComparer.OrdinalIgnoreCase))
        {
            var primary = Screen.PrimaryScreen?.WorkingArea ?? vs;
            fixture.Bounds = primary;
            fixture.Controls.Clear();
            var closed = false;
            var close = new Button { Text = "Close annotation fixture", AutoSize = true, Location = new Point(20, 20) };
            close.Click += (_, _) => closed = true;
            fixture.Controls.Add(close);
            fixture.Controls.Add(new Label
            {
                Text = "Inspect all seven annotation shapes. The overlay should not take focus; use the button to close.",
                AutoSize = true,
                Location = new Point(20, 65),
            });
            fixture.Activate();

            var left = primary.Left + Math.Max(80, primary.Width / 10);
            var top = primary.Top + Math.Max(140, primary.Height / 5);
            var column = Math.Max(220, primary.Width / 3);
            var row = Math.Max(180, primary.Height / 3);
            void ShowMarks()
            {
              overlay.ShowResponding("Annotation fixture");
              overlay.ShowAnnotations([
                new("rect", left, top, 150, 90, "rectangle"),
                new("circle", left + column, top, 140, 100, "circle"),
                new("arrow", left + column * 2, top, left + column * 2 + 150, top + 100, "arrow"),
                new("polygon", left, top + row, 0, 0, "polygon", [new(left, top + row), new(left + 180, top + row + 20), new(left + 120, top + row + 140)]),
                new("freehand", left + column, top + row, 0, 0, "freehand", [new(left + column, top + row), new(left + column + 55, top + row + 80), new(left + column + 110, top + row + 20), new(left + column + 165, top + row + 120)]),
                new("curve", left + column * 2, top + row, 0, 0, "curved stroke", [new(left + column * 2, top + row), new(left + column * 2 + 30, top + row + 140), new(left + column * 2 + 120, top + row), new(left + column * 2 + 165, top + row + 120)]),
                new("text", left + column * 2, top + row + 165, 0, 0, "text label"),
              ]);
            }
            var replay = new Button { Text = "Replay drawing", AutoSize = true, Location = new Point(240, 20) };
            replay.Click += (_, _) => ShowMarks();
            fixture.Controls.Add(replay);
            ShowMarks();
            if (!PumpUntil(() => NativeMethods.IsWindowVisible(overlay.WindowHandle), TimeSpan.FromSeconds(2))
                || NativeMethods.GetForegroundWindow() != fixture.Handle)
            {
                Console.Error.WriteLine("FAIL: annotation fixture was not visible or changed foreground");
                return 26;
            }
            Console.WriteLine("ANNOTATION FIXTURE: inspect all seven shapes, then click Close annotation fixture");
            while (!closed && fixture.Visible)
            {
                Application.DoEvents();
                Thread.Sleep(16);
            }
            overlay.Hide();
            return 0;
        }

        overlay.ShowProcessing();
        if (!PumpUntil(() => NativeMethods.IsWindowVisible(overlay.WindowHandle), TimeSpan.FromSeconds(2)))
        {
            Console.Error.WriteLine("FAIL: processing overlay did not become visible; foreground check would be inconclusive");
            return 21;
        }
        if (NativeMethods.GetForegroundWindow() != fixture.Handle)
        {
            Console.Error.WriteLine(
                $"FAIL: ShowProcessing changed foreground fixture=0x{fixture.Handle:X} actual=0x{NativeMethods.GetForegroundWindow():X} overlay=0x{overlay.WindowHandle:X}");
            return 22;
        }

        overlay.Hide();
        Application.DoEvents();
        if (NativeMethods.IsWindowVisible(overlay.WindowHandle)
            || NativeMethods.GetForegroundWindow() != fixture.Handle)
        {
            Console.Error.WriteLine(
                $"FAIL: Hide did not preserve fixture foreground actual=0x{NativeMethods.GetForegroundWindow():X} visible={NativeMethods.IsWindowVisible(overlay.WindowHandle)}");
            return 23;
        }

        // Fly-to, then ShowResponding (Talk sequencing) — tip must stay at the POINT.
        overlay.PointAt(targetX, targetY, "ci-point");
        overlay.ShowResponding("overlay smoke");

        if (!PumpUntil(() => overlay.IsPointFlyComplete, TimeSpan.FromSeconds(5)))
        {
            Console.Error.WriteLine($"FAIL: fly-to did not settle (complete={overlay.IsPointFlyComplete})");
            return 3;
        }

        if (NativeMethods.GetForegroundWindow() != fixture.Handle)
        {
            Console.Error.WriteLine(
                $"FAIL: PointAt/ShowResponding changed foreground fixture=0x{fixture.Handle:X} actual=0x{NativeMethods.GetForegroundWindow():X} overlay=0x{overlay.WindowHandle:X}");
            return 24;
        }

        if (!NativeMethods.IsWindowVisible(overlay.WindowHandle))
        {
            Console.Error.WriteLine("FAIL: the cursor window is not visible while pointing");
            return 4;
        }

        if (!overlay.TryGetBuddyVirtualPoint(out var buddyX, out var buddyY))
        {
            Console.Error.WriteLine("FAIL: TryGetBuddyVirtualPoint returned false after settle");
            return 6;
        }

        if (Math.Abs(buddyX - targetX) > SettleTolerancePx || Math.Abs(buddyY - targetY) > SettleTolerancePx)
        {
            Console.Error.WriteLine(
                $"FAIL: buddy virtual point ({buddyX},{buddyY}) mismatch target ({targetX},{targetY})");
            return 7;
        }

        Console.WriteLine(
            $"PASS buddy=({buddyX},{buddyY}) target=({targetX},{targetY}) tol={SettleTolerancePx}");
        overlay.Hide();
        Application.DoEvents();
        if (NativeMethods.GetForegroundWindow() != fixture.Handle)
        {
            Console.Error.WriteLine(
                $"FAIL: Hide after pointing changed foreground fixture=0x{fixture.Handle:X} actual=0x{NativeMethods.GetForegroundWindow():X}");
            return 25;
        }

        var selection = overlay.SelectRegionAsync();
        Application.DoEvents();
        overlay.Hide();
        if (!PumpUntil(() => selection.IsCompleted, TimeSpan.FromSeconds(2))
            || selection.IsFaulted
            || selection.GetAwaiter().GetResult() is not null)
        {
            Console.Error.WriteLine("FAIL: Hide did not cancel and close active region selection");
            return 11;
        }

        return 0;
    }

    private static string? ValidateResponseBubblePlacement()
    {
        var working = new RectangleF(0, 0, 1000, 700);
        var topMarks = new[]
        {
            new RectangleF(9, 69, 180, 51),
            new RectangleF(6, 141, 459, 54),
            new RectangleF(255, 87, 99, 45),
        };
        var below = ResponseBubblePlacement.Place(new RectangleF(183, 67, 320, 100), working, topMarks);
        if (below.Top < topMarks.Max(bounds => bounds.Bottom) + 12
            || topMarks.Any(below.IntersectsWith))
        {
            return $"did not move below top marks: {below}";
        }

        var bottomMarks = new[] { new RectangleF(40, 500, 500, 160) };
        var above = ResponseBubblePlacement.Place(new RectangleF(100, 540, 320, 100), working, bottomMarks);
        if (above.Bottom > bottomMarks[0].Top - 12 || above.IntersectsWith(bottomMarks[0]))
        {
            return $"did not move above bottom marks: {above}";
        }

        var negativeWorking = new RectangleF(-1536, -200, 1536, 1000);
        var existingClamp = ResponseBubblePlacement.Place(
            new RectangleF(-1600, -240, 400, 100), negativeWorking, []);
        if (existingClamp != new RectangleF(-1536, -200, 400, 100))
        {
            return $"changed the no-mark clamp: {existingClamp}";
        }

        var negativeMark = new RectangleF(-1500, -150, 420, 170);
        var mixedMarks = new[]
        {
            negativeMark,
            new RectangleF(100, 40, 300, 100), // A mark on another monitor must not affect this screen.
        };
        var negativePlacement = ResponseBubblePlacement.Place(
            new RectangleF(-1450, -120, 320, 100), negativeWorking, mixedMarks);
        if (!negativeWorking.Contains(negativePlacement)
            || negativePlacement.IntersectsWith(negativeMark)
            || negativePlacement.X >= 0)
        {
            return $"failed negative-origin/mixed-screen placement: {negativePlacement}";
        }

        return null;
    }

    private static bool PumpUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            if (predicate())
            {
                // Extra frames so the settled position is rendered.
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
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);
    }
}
