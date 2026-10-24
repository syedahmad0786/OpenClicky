using System.Runtime.InteropServices;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Overlay;
using CoreDrawing = OpenClicky.Core.Drawing;

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

        var messageFailure = ValidateMessageBubble();
        if (messageFailure is not null)
        {
            Console.Error.WriteLine("FAIL: message bubble " + messageFailure);
            return 13;
        }

        var layoutFailure = ValidateDrawingLayout();
        if (layoutFailure is not null)
        {
            Console.Error.WriteLine("FAIL: drawing layout " + layoutFailure);
            return 17;
        }

        var helperFailure = ValidateHelperLayout();
        if (helperFailure is not null)
        {
            Console.Error.WriteLine("FAIL: helper layout " + helperFailure);
            return 18;
        }

        if (args.Contains("--geometry-only", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("PASS: region, response bubble, message bubble, drawing layout and helper (graph, card, button) geometry");
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
        if (overlay.WindowHandles.Count != 7 || overlay.WindowHandles.Distinct().Count() != 7 || overlay.WindowHandles.Contains(IntPtr.Zero)
            || overlay.BuddyTipHandle != overlay.WindowHandle || overlay.WindowHandles[3] != overlay.DrawingHandle)
        {
            Console.Error.WriteLine($"FAIL: expected the cursor, ring, click-catcher, drawing and three drawing-button windows, got [{string.Join(",", overlay.WindowHandles)}]");
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

        // The real destination: the cursor window Windows shows must cover the POINT target.
        if (!NativeMethods.GetWindowRect(overlay.WindowHandle, out var cursorRect)
            || cursorRect.Left > targetX || cursorRect.Top > targetY || cursorRect.Right <= targetX || cursorRect.Bottom <= targetY)
        {
            Console.Error.WriteLine(
                $"FAIL: cursor window [{cursorRect.Left},{cursorRect.Top},{cursorRect.Right},{cursorRect.Bottom}] does not contain ({targetX},{targetY})");
            return 5;
        }

        Console.WriteLine($"cursor_window=[{cursorRect.Left},{cursorRect.Top},{cursorRect.Right - cursorRect.Left}x{cursorRect.Bottom - cursorRect.Top}]");

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

        var ringFailure = ValidateClickRing(overlay, vs, targetX, targetY);
        if (ringFailure is not null)
        {
            Console.Error.WriteLine("FAIL: click ring " + ringFailure);
            return 15;
        }
        Console.WriteLine("PASS: spring flight onto the target, dashed click ring, return flight, Hide clears the ring");

        var drawingFailure = ValidateDrawing(overlay, vs);
        if (drawingFailure is not null)
        {
            Console.Error.WriteLine("FAIL: drawing " + drawingFailure);
            return 16;
        }
        Console.WriteLine("PASS: drawing reveals in its own window, Hide hides it, it persists, an empty drawing clears it");

        var helperLiveFailure = ValidateHelperDrawing(overlay, vs);
        if (helperLiveFailure is not null)
        {
            Console.Error.WriteLine("FAIL: helper drawing " + helperLiveFailure);
            return 19;
        }
        Console.WriteLine("PASS: spotlight dims the screen outside its box, a card paints, button click targets raise ButtonClicked and follow Hide and a running task");

        var figureFailure = ValidateFigure(overlay, fixture);
        if (figureFailure is not null)
        {
            Console.Error.WriteLine("FAIL: figure " + figureFailure);
            return 27;
        }
        Console.WriteLine("PASS: a figure opens its own click-through window over its box without taking focus, paints, follows Hide, and closes with the drawing");

        var dockFailure = ValidateDockAndDetach(overlay, fixture, vs, targetX, targetY);
        if (dockFailure is not null)
        {
            Console.Error.WriteLine("FAIL: dock / detach " + dockFailure);
            return 14;
        }
        Console.WriteLine("PASS: dock into the notch, hide, shake to detach, click to talk");

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

    /// <summary>
    /// A POINT with a control frame lands the tip exactly on the target and draws the ring round the frame (checked on the real
    /// screen, not just the window rectangles); ShowIdle clears the ring and flies the companion back to the cursor; Hide clears
    /// the ring. Only the cursor position is set; nothing is clicked or typed anywhere.
    /// </summary>
    private static string? ValidateClickRing(BuddyOverlayController overlay, Rectangle vs, int targetX, int targetY)
    {
        var cursorWindow = overlay.WindowHandle;
        var drawing = overlay.WindowHandles[1];
        static bool Visible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);
        static void Pump(int ms) => PumpUntil(() => false, TimeSpan.FromMilliseconds(ms));
        static bool Covers(IntPtr hwnd, Point p) =>
            NativeMethods.GetWindowRect(hwnd, out var r) && r.Left <= p.X && p.X < r.Right && r.Top <= p.Y && p.Y < r.Bottom;

        var saved = Cursor.Position;
        var home = new Point(vs.Left + vs.Width * 3 / 4, vs.Top + vs.Height * 3 / 4);
        try
        {
            Cursor.Position = home;
            overlay.ApplyStyle(new CompanionStyle("#3380FF", CursorPresence.Following, true, true, false));
            overlay.ShowIdle();
            Pump(900);

            // The frame is wide so its left edge (and glow) is far from the companion's triangle at the target.
            const int halfW = 90, halfH = 24;
            var frame = new OpenClicky.Core.Drawing.Box(targetX - halfW, targetY - halfH, halfW * 2, halfH * 2);
            overlay.PointAt(targetX, targetY, "ring-check", null, frame);
            if (Visible(drawing)) return "the ring was drawn before the tip landed";
            if (!PumpUntil(() => overlay.IsPointFlyComplete, TimeSpan.FromSeconds(5))) return "the flight did not land";
            if (!overlay.TryGetBuddyVirtualPoint(out var bx, out var by) || bx != targetX || by != targetY)
                return $"the tip landed at ({bx},{by}), not exactly on ({targetX},{targetY})";
            if (!PumpUntil(() => Visible(drawing), TimeSpan.FromSeconds(2))) return "the ring window never appeared after landing";
            if (!Covers(drawing, new Point(targetX - halfW, targetY - halfH)) || !Covers(drawing, new Point(targetX + halfW - 1, targetY + halfH - 1)))
                return "the ring window does not cover the control frame";
            Pump(500); // past the spring reveal

            // The destination: pixels left of the frame (the companion is 80+ px away) differ with and without the ring.
            var strip = new Rectangle(targetX - halfW - 14, targetY - 12, 24, 24);
            if (Environment.GetEnvironmentVariable("OPENCLICKY_RING_SHOT") is { Length: > 0 } shot) // local eyeballing only
                Grab(new Rectangle(targetX - halfW - 40, targetY - halfH - 70, halfW * 2 + 80, halfH * 2 + 110)).Save(shot, System.Drawing.Imaging.ImageFormat.Png);
            var away = new Rectangle(strip.X, strip.Y + 160, strip.Width, strip.Height); // control: nothing of ours is drawn here
            var withRing = Grab(strip);
            var awayBefore = Grab(away);
            overlay.Hide();
            if (overlay.WindowHandles.Any(Visible)) return "Hide left an overlay window (the ring?) visible";
            Pump(IOverlayController.HideSettleMilliseconds + 60);
            var without = Grab(strip);
            var changed = CountChanged(withRing, without);
            var awayChanged = CountChanged(awayBefore, Grab(away));
            if (changed < 40 || awayChanged > changed / 4) return $"the screen shows no ring left of the control (changed pixels={changed}, control strip={awayChanged})";
            Console.WriteLine($"ring_pixels_changed={changed} control_pixels_changed={awayChanged}");

            // Back after a Hide the held POINT flies in again and re-draws its ring; then ShowIdle clears the ring at once.
            overlay.ShowResponding("ring check");
            if (!PumpUntil(() => overlay.IsPointFlyComplete && Visible(drawing), TimeSpan.FromSeconds(5))) return "the held POINT did not fly in and re-draw its ring after Hide";
            overlay.ShowIdle();
            if (Visible(drawing)) return "ShowIdle left the ring up";

            // Return flight: still near the target a moment after ShowIdle, then at the resting spot beside the real cursor.
            var restingTip = new Point(home.X + (int)(CursorConstants.TipOffsetX * DpiOf(home)), home.Y + (int)(CursorConstants.TipOffsetY * DpiOf(home)));
            Pump(60);
            if (Covers(cursorWindow, restingTip)) return "the companion jumped to the cursor instead of flying back";
            if (!PumpUntil(() => Covers(cursorWindow, restingTip), TimeSpan.FromSeconds(3))) return "the companion did not fly back to the cursor";
            return null;
        }
        finally
        {
            overlay.Hide();
            Cursor.Position = saved;
        }

        static float DpiOf(Point at) => DpiScale.ForPoint(at);
    }

    private static Bitmap Grab(Rectangle area)
    {
        var bitmap = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(area.Location, Point.Empty, area.Size);
        return bitmap;
    }

    private static int CountChanged(Bitmap a, Bitmap b)
    {
        var changed = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 12) changed++;
            }
        }
        return changed;
    }

    private static OpenClicky.Core.Drawing.DrawPart Part(string id, OpenClicky.Core.Drawing.DrawOpKind kind, OpenClicky.Core.Drawing.Box box,
        OpenClicky.Core.Drawing.Pt[]? points = null, string? label = null)
    {
        var args = new Dictionary<string, string>();
        if (label is not null) args["label"] = label;
        var op = new OpenClicky.Core.Drawing.DrawOp(kind, id, points ?? [], box, label, "#FF4C4C", 1, args, 0);
        return new(id, op, op, 1, 1);
    }

    /// <summary>Layout keeps on-screen parts with bounds covering what they paint, separates their pills, and drops off-screen or NaN parts.</summary>
    private static string? ValidateDrawingLayout()
    {
        var vs = SystemInformation.VirtualScreen;
        int x = vs.Left + 200, y = vs.Top + 200;
        var rect = Part("a", OpenClicky.Core.Drawing.DrawOpKind.Rect, new(x, y, 120, 60), label: "first");
        var twin = Part("b", OpenClicky.Core.Drawing.DrawOpKind.Rect, new(x, y, 120, 60), label: "second");
        var off = Part("c", OpenClicky.Core.Drawing.DrawOpKind.Rect, new(vs.Right - 10, y, 120, 60));
        var broken = Part("d", OpenClicky.Core.Drawing.DrawOpKind.Line, new(x, y, float.NaN, 1), [new(x, y), new(float.NaN, y)]);
        var layouts = DrawingRenderer.Layout([rect, twin, off, broken]);
        if (layouts.Count != 2) return $"kept {layouts.Count} parts, expected the two on-screen ones";
        var bounds = layouts[0].Bounds;
        if (!bounds.Contains(new RectangleF(x, y, 120, 60))) return $"bounds {bounds} do not cover the rect";
        if (layouts[0].Pill is not { } p0 || layouts[1].Pill is not { } p1 || p0.IntersectsWith(p1)) return "label pills overlap";
        if (DrawingRenderer.PenTip(layouts[0], 0.5f) is not { } tip || !bounds.Contains(tip.X, tip.Y)) return "pen tip outside the part";

        // A star whose strokes leave its monitor is dropped before its wobble is built (even past the parser's size cap);
        // one that fits is kept, and a highlight hugging the monitor's bottom edge keeps its band.
        var mon = Screen.PrimaryScreen!.Bounds;
        OpenClicky.Core.Drawing.DrawPart Star(string id, float sx, float sy, string size)
        {
            var p = Part(id, OpenClicky.Core.Drawing.DrawOpKind.Star, new(sx, sy, 0, 0), [new(sx, sy)]);
            return p with { Op = p.Op with { Args = new Dictionary<string, string> { ["size"] = size } } };
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var huge = DrawingRenderer.Layout([Star("huge", mon.Left + 100, mon.Top + 100, "99999")]);
        if (huge.Count != 0 || clock.ElapsedMilliseconds > 200) return $"a size=99999 star was kept ({huge.Count}) or took {clock.ElapsedMilliseconds} ms";
        if (DrawingRenderer.Layout([Star("edge", mon.Right - 20, mon.Top + 300, "400")]).Count != 0) return "a star crossing its monitor's edge was kept";
        if (DrawingRenderer.Layout([Star("fits", mon.Left + 300, mon.Top + 300, "400")]).Count != 1) return "a size=400 star that fits was dropped";
        var bar = Part("bar", OpenClicky.Core.Drawing.DrawOpKind.Highlight, new(mon.Left, mon.Bottom - 40, mon.Width, 40));
        if (DrawingRenderer.Layout([bar]).Count != 1) return "a highlight on the monitor's bottom edge was dropped";
        return null;
    }

    /// <summary>
    /// The model's drawing reveals in its own window over the part (checked on the real screen), that window is not the whole
    /// virtual screen, Hide hides it, the next Show brings it back (it persists), and an empty drawing clears it. Only the cursor
    /// position is set; nothing is clicked or typed anywhere.
    /// </summary>
    private static string? ValidateDrawing(BuddyOverlayController overlay, Rectangle vs)
    {
        var ink = overlay.DrawingHandle;
        static bool Visible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);
        static void Pump(int ms) => PumpUntil(() => false, TimeSpan.FromMilliseconds(ms));
        static bool Covers(IntPtr hwnd, Point p) =>
            NativeMethods.GetWindowRect(hwnd, out var r) && r.Left <= p.X && p.X < r.Right && r.Top <= p.Y && p.Y < r.Bottom;

        var saved = Cursor.Position;
        // On the primary monitor: a point a quarter across the virtual screen can sit in an L-shaped layout's dead zone,
        // and parts whose strokes leave their monitor are dropped.
        var mon = Screen.PrimaryScreen!.Bounds;
        int x = mon.Left + mon.Width / 4, y = mon.Top + mon.Height / 2;
        try
        {
            Cursor.Position = new Point(vs.Left + vs.Width * 3 / 4, vs.Top + vs.Height * 3 / 4); // the companion stays clear
            overlay.ShowIdle();
            Pump(300);
            var part = Part("box", OpenClicky.Core.Drawing.DrawOpKind.Rect, new(x, y, 200, 80));
            overlay.ShowDrawing([part], [new(part, TimeSpan.Zero, TimeSpan.FromSeconds(0.4), false)]);
            if (!overlay.HasDrawing) return "HasDrawing stayed false";
            if (!PumpUntil(() => Visible(ink), TimeSpan.FromSeconds(2))) return "the drawing window never appeared";
            if (!Covers(ink, new Point(x, y)) || !Covers(ink, new Point(x + 199, y + 79))) return "the drawing window does not cover the part";
            if (!NativeMethods.GetWindowRect(ink, out var r) || r.Right - r.Left >= vs.Width) return "the drawing window spans the whole virtual screen";
            Pump(1200); // past the reveal and the companion's ride back

            var edge = new Rectangle(x + 60, y - 6, 80, 12); // across the top stroke
            var away = new Rectangle(x + 60, y - 80, 80, 12); // nothing of ours here
            var withInk = Grab(edge);
            var awayBefore = Grab(away);
            overlay.Hide();
            if (Visible(ink)) return "Hide left the drawing visible";
            Pump(IOverlayController.HideSettleMilliseconds + 60);
            var changed = CountChanged(withInk, Grab(edge));
            var awayChanged = CountChanged(awayBefore, Grab(away));
            if (changed < 40 || awayChanged > changed / 4) return $"the screen shows no stroke (changed pixels={changed}, control strip={awayChanged})";
            Console.WriteLine($"ink_pixels_changed={changed} control_pixels_changed={awayChanged}");

            overlay.ShowIdle();
            if (!PumpUntil(() => Visible(ink), TimeSpan.FromSeconds(2))) return "the drawing did not come back after Hide";
            overlay.ShowDrawing([], []);
            if (overlay.HasDrawing) return "HasDrawing stayed true after clearing";
            if (!PumpUntil(() => !Visible(ink), TimeSpan.FromSeconds(2))) return "an empty drawing left its window up";
            return null;
        }
        finally
        {
            overlay.ShowDrawing([], []);
            overlay.Hide();
            Cursor.Position = saved;
        }
    }

    /// <summary>A part parsed from a real tag, as the model would write it (screenshot pixels, scale 1).</summary>
    private static CoreDrawing.DrawPart Tagged(string tag, int seq = 1)
    {
        var op = Assert1(CoreDrawing.DrawCommandParser.Parse(tag).Ops);
        return new(op.Id ?? $"t{seq}", op, op, 1, seq);

        static CoreDrawing.DrawOp Assert1(IReadOnlyList<CoreDrawing.DrawOp> ops) =>
            ops.Count == 1 ? ops[0] : throw new InvalidOperationException($"the smoke tag did not parse to one op: {ops.Count}");
    }

    /// <summary>
    /// The screen helpers lay out whole on the monitor: graphs and timelines get strokes and a text path, a card is 280 DIP (or its
    /// <c>w</c>) wide with at most eight lines of text and a small tilt, a sticky is 160 DIP square, the paper stocks are ours,
    /// only three buttons are kept, and anything that would leave the monitor is dropped.
    /// </summary>
    private static string? ValidateHelperLayout()
    {
        var mon = Screen.PrimaryScreen!.Bounds;
        int x = mon.Left + 200, y = mon.Top + 200;
        static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        DrawingRenderer.PartLayout One(string tag, int seq = 1) =>
            DrawingRenderer.Layout([Tagged(tag, seq)]) is [var only] ? only : throw new InvalidOperationException("not laid out: " + tag);

        try
        {
            var graph = One($"[DRAW:graph id=g x={x} y={y} w=400 h=240 xlabel=\"day\" ylabel=\"users\" series=\"3,5,4,9;2,2,3,4\"]");
            if (graph.Strokes.Count != 13 || graph.StrokeColours.Count != 13 || graph.LabelPath is null || graph.LabelBox is not { } graphText)
                return $"graph strokes={graph.Strokes.Count} colours={graph.StrokeColours.Count} labels={graph.LabelPath is not null}";
            if (graph.StrokeColours[0] != ColorTranslator.FromHtml("#1F2430") || graph.StrokeColours[1] is not null || graph.StrokeColours[2] is null)
                return "graph stroke colours: ink axes, the part's own first line, a distinct second line";
            if (!graph.Bounds.Contains(graphText)) return "graph bounds do not cover its text";

            var timeline = One($"[DRAW:timeline id=t x={x} y={y} w=500 items=\"one;two;three\"]");
            if (timeline.Strokes.Count != 4 || timeline.LabelPath is null) return $"timeline strokes={timeline.Strokes.Count}";
            if (One($"[DRAW:scroll id=s x={x} y={y} dir=down]").Strokes.Count != 3) return "scroll is not three chevrons";
            if (One($"[DRAW:tie id=i pts={x},{y},{x + 200},{y}]").Strokes.Count != 1) return "tie is not one arc";

            var spot = One($"[DRAW:spotlight id=o x={x} y={y} w=200 h=100]");
            if (spot.Spot is not { } s || s.Hole != new RectangleF(x, y, 200, 100) || !s.Screen.Contains(s.Hole)) return "spotlight hole is not the box inside the dimmed area";
            if (One($"[DRAW:badge id=b x={x} y={y} n=3]").Badge is not { Text: "3" }) return "badge has no numeral";

            // Only the last spotlight is kept (they would stack to black); other parts are not touched.
            var spots = DrawingRenderer.Layout(
            [
                Tagged($"[DRAW:spotlight id=o1 x={x} y={y} w=200 h=100]", 1),
                Tagged($"[DRAW:badge id=b x={x} y={y} n=3]", 2),
                Tagged($"[DRAW:spotlight id=o2 x={x + 300} y={y} w=100 h=50]", 3),
                Tagged($"[DRAW:spotlight id=o3 x={x + 500} y={y} w=100 h=50]", 4),
            ]);
            if (spots.Count != 2 || spots.Count(l => l.Part.Op.Kind == CoreDrawing.DrawOpKind.Spotlight) != 1 || spots.Single(l => l.Spot is not null).Part.Id != "o3")
                return $"spotlights: {string.Join(",", spots.Select(l => l.Part.Id))}, expected the badge and the last spotlight";
            // The newest wins even from earlier in the document (a part replaced by id keeps its place there).
            var replaced = DrawingRenderer.Layout(
            [
                Tagged($"[DRAW:spotlight id=o1 x={x} y={y} w=200 h=100]", 5),
                Tagged($"[DRAW:spotlight id=o2 x={x + 300} y={y} w=100 h=50]", 3),
            ]);
            if (replaced.Count != 1 || replaced[0].Part.Id != "o1") return $"spotlights by seq: {string.Join(",", replaced.Select(l => l.Part.Id))}, expected o1 (seq 5)";
            // A spotlight lasts 30 s, then fades over 600 ms (120 ms with reduced motion) and is gone.
            static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;
            if (!Near(DrawingRenderer.SpotlightAlpha(0, false), 1) || !Near(DrawingRenderer.SpotlightAlpha(30, false), 1)
                || !Near(DrawingRenderer.SpotlightAlpha(30.3, false), 0.5f) || DrawingRenderer.SpotlightAlpha(30.7, false) != 0
                || !Near(DrawingRenderer.SpotlightAlpha(30.06, true), 0.5f) || DrawingRenderer.SpotlightAlpha(30.2, true) != 0)
                return "spotlight lifetime: 1 until 30 s, then a 600 ms fade (120 ms reduced) to 0";

            var card = One($"[DRAW:card id=c x={x} y={y} title=Hello text=\"Some words here\" stock=sky]");
            if (card.Paper is not { } paper) return "card has no paper";
            if (Math.Abs(paper.Rect.Width - 280 * card.Dip) > 1) return $"card width {paper.Rect.Width} is not 280 DIP";
            if (Hex(paper.Fill) != "#DCEBFF" || Math.Abs(paper.Tilt) > 2) return $"card stock {Hex(paper.Fill)} tilt {paper.Tilt}";
            if (!card.Bounds.Contains(paper.Rect)) return "card bounds do not cover the paper";
            if (One($"[DRAW:card id=c x={x} y={y} w=360 title=Hello text=\"Some words here\"]").Paper!.Rect.Width != 360) return "card ignores its w";
            var sticky = One($"[DRAW:sticky id=k x={x} y={y} text=\"Remember this\"]").Paper;
            if (sticky is null || Math.Abs(sticky.Rect.Width - 160 * card.Dip) > 1 || Math.Abs(sticky.Rect.Height - sticky.Rect.Width) > 1 || Hex(sticky.Fill) != "#FFF1B8")
                return "sticky is not a 160 DIP butter square";

            // Eight lines at most: a very long body is cut to the same height however long it is.
            DrawingRenderer.PartLayout Body(int chars)
            {
                var text = string.Join(' ', Enumerable.Repeat("word", chars / 5));
                var part = Tagged($"[DRAW:card id=c x={x} y={y} title=Hello text=\"hi\"]");
                var op = part.Op with { Text = text, Args = new Dictionary<string, string>(part.Op.Args) { ["text"] = text } };
                return DrawingRenderer.Layout([part with { Op = op, Source = op }])[0];
            }
            var shortCard = Body(10).Paper!.Rect.Height;
            var cappedA = Body(2000).Paper!.Rect.Height;
            var cappedB = Body(5000).Paper!.Rect.Height;
            if (!(shortCard < cappedA) || Math.Abs(cappedA - cappedB) > 0.5f) return $"card height not capped at eight lines: {shortCard} / {cappedA} / {cappedB}";

            // At most three buttons are kept; the fourth is dropped.
            var buttons = Enumerable.Range(0, 4).Select(i => Tagged($"[DRAW:button id=b{i} x={x} y={y + i * 60} text=\"Go {i}\" prompt=\"more\"]", i + 1)).ToArray();
            var kept = DrawingRenderer.Layout(buttons);
            if (kept.Count != 3 || kept.Any(k => k.ButtonRect is not { Height: > 0 })) return $"{kept.Count} buttons kept, expected 3";

            if (Environment.GetEnvironmentVariable("OPENCLICKY_HELPER_SHOT") is { Length: > 0 } shot) // local eyeballing only
            {
                var sheet = new[]
                {
                    Tagged($"[DRAW:graph id=g x={x} y={y} w=420 h=260 xlabel=\"week\" ylabel=\"signups\" series=\"3,5,4,9,7,12;2,2,3,4,6,5\"]", 1),
                    Tagged($"[DRAW:timeline id=t x={x} y={y + 340} w=520 items=\"plan;build;test a longer one;ship\"]", 2),
                    Tagged($"[DRAW:scroll id=s x={x + 470} y={y + 40} dir=down]", 3),
                    Tagged($"[DRAW:tie id=i pts={x + 500},{y + 150},{x + 640},{y + 260}]", 4),
                    Tagged($"[DRAW:badge id=b x={x + 700} y={y + 20} n=7]", 5),
                    Tagged($"[DRAW:card id=c x={x + 720} y={y + 60} title=\"Try this\" text=\"Open the settings page, then pick the Cursor tab and choose a colour you like.\" stock=sky]", 6),
                    Tagged($"[DRAW:sticky id=k x={x + 1060} y={y + 60} text=\"Remember to save before closing the window.\"]", 7),
                    Tagged($"[DRAW:button id=u x={x + 720} y={y + 300} text=\"Show me an example\" prompt=\"show one\"]", 8),
                };
                var layouts = DrawingRenderer.Layout(sheet);
                var union = layouts.Select(l => l.Bounds).Aggregate(RectangleF.Union);
                using var picture = new Bitmap((int)Math.Ceiling(union.Width), (int)Math.Ceiling(union.Height));
                using (var g = Graphics.FromImage(picture))
                {
                    g.Clear(Color.FromArgb(0xEE, 0xEE, 0xF2));
                    CursorRenderer.Prepare(g);
                    g.TranslateTransform(-union.X, -union.Y);
                    foreach (var l in layouts) DrawingRenderer.Draw(g, l, 1, 1, Color.FromArgb(0x33, 0x80, 0xFF));
                }
                picture.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
            }

            // Whole on the monitor or dropped: a card at the right edge, a badge at the corner.
            if (DrawingRenderer.Layout([Tagged($"[DRAW:card id=c x={mon.Right - 60} y={y} title=Hello text=\"edge\"]")]).Count != 0) return "a card running off its monitor was kept";
            if (DrawingRenderer.Layout([Tagged($"[DRAW:badge id=b x={mon.Right - 2} y={mon.Top + 2} n=1]")]).Count != 0) return "a badge half off its monitor was kept";
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Live proof for the helpers (the real screen, not the window rectangles): a spotlight darkens a strip outside its box and
    /// leaves one inside alone; a card paints; each fully drawn button gets a click target over its pill that raises
    /// ButtonClicked with its label and prompt (a click sent to our own target window, a press dragged off it is no click); the
    /// targets are down after Hide, while an agent task runs, and once the drawing is cleared. Nothing else is clicked or typed.
    /// </summary>
    private static string? ValidateHelperDrawing(BuddyOverlayController overlay, Rectangle vs)
    {
        const int WmLButtonDown = 0x201, WmLButtonUp = 0x202;
        static bool Visible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);
        static void Pump(int ms) => PumpUntil(() => false, TimeSpan.FromMilliseconds(ms));
        static IntPtr Lp(int px, int py) => (IntPtr)((py << 16) | (px & 0xFFFF));
        var catchers = overlay.WindowHandles.Skip(4).ToArray();

        var saved = Cursor.Position;
        var mon = Screen.PrimaryScreen!.Bounds;
        int x = mon.Left + mon.Width / 4, y = mon.Top + mon.Height / 3;
        var clicks = new List<(string Label, string Prompt)>();
        void OnClick(string label, string prompt) => clicks.Add((label, prompt));
        overlay.ButtonClicked += OnClick;
        try
        {
            Cursor.Position = new Point(vs.Left + vs.Width * 3 / 4, vs.Top + vs.Height * 3 / 4); // the companion stays clear
            overlay.ShowIdle();
            Pump(300);

            // Spotlight: dim outside, clear inside.
            var hole = new Rectangle(x, y, 240, 140);
            var spot = Tagged($"[DRAW:spotlight id=o x={hole.X} y={hole.Y} w={hole.Width} h={hole.Height}]");
            overlay.ShowDrawing([spot], [new(spot, TimeSpan.Zero, TimeSpan.FromSeconds(0.3), false)]);
            if (!PumpUntil(() => Visible(overlay.DrawingHandle), TimeSpan.FromSeconds(2))) return "the spotlight window never appeared";
            Pump(1000);
            var outside = new Rectangle(hole.Right + 40, hole.Y + 20, 60, 60);
            var inside = new Rectangle(hole.X + 60, hole.Y + 40, 60, 60);
            var dimmed = Grab(outside);
            var clear = Grab(inside);
            overlay.Hide();
            Pump(IOverlayController.HideSettleMilliseconds + 60);
            var dimChanged = CountChanged(dimmed, Grab(outside));
            var holeChanged = CountChanged(clear, Grab(inside));
            if (dimChanged < 1500 || holeChanged > 40) return $"spotlight pixels: outside changed {dimChanged} (of 3600), inside {holeChanged}";
            Console.WriteLine($"spotlight_outside_changed={dimChanged} inside_changed={holeChanged}");

            // A spotlight expires: driven by the clock handed to Show/Render, so there is no 30 s wait. It stays up until then, fades
            // (still animating), is dropped, and the same part shown again does not come back; a newer spotlight does.
            using (var ink = new DrawingWindow())
            {
                var accent = Color.FromArgb(0x33, 0x80, 0xFF);
                var first = Tagged($"[DRAW:spotlight id=o x={hole.X} y={hole.Y} w={hole.Width} h={hole.Height}]", 1);
                ink.Show([first], [], 0);
                ink.Render(10, accent, false);
                if (!ink.HasParts) return "the spotlight was gone before its 30 s";
                if (!ink.Render(30.3, accent, false) || !ink.HasParts) return "the spotlight did not fade after 30 s";
                if (ink.Render(31, accent, false) || ink.HasParts) return "the spotlight was not dropped after its fade";
                ink.Show([first], [], 32);
                if (ink.HasParts) return "an expired spotlight came back when its drawing was shown again";
                ink.Show([Tagged($"[DRAW:spotlight id=o x={hole.X} y={hole.Y} w={hole.Width} h={hole.Height}]", 2)], [], 40);
                if (!ink.HasParts) return "a newer spotlight was not shown";
                ink.Render(40.1, accent, false);
                if (ink.Render(70.5, accent, true) || ink.HasParts) return "with reduced motion the spotlight did not go 120 ms after its 30 s";
                ink.Show([], [], 71);
                ink.Render(71, accent, false);
            }

            // A card paints where it was put.
            var card = Tagged($"[DRAW:card id=c x={x} y={y} title=Notes text=\"Check the drawing\" stock=mint]");
            overlay.ShowDrawing([card], [new(card, TimeSpan.Zero, TimeSpan.FromSeconds(0.3), false)]);
            overlay.ShowIdle();
            Pump(1000);
            var dip = DpiScale.ForPoint(new Point(x, y));
            var paperBox = new Rectangle(x + (int)(20 * dip), y + (int)(26 * dip), 30, 6); // well inside the tilted paper
            var onScreen = Grab(paperBox);
            overlay.Hide();
            Pump(IOverlayController.HideSettleMilliseconds + 60);
            var paperChanged = CountChanged(onScreen, Grab(paperBox));
            if (paperChanged < 90) return $"the screen shows no card paper (changed pixels={paperChanged} of 180)";

            // Buttons.
            var parts = new[]
            {
                Tagged($"[DRAW:button id=b1 x={x} y={y} text=\"First\" prompt=\"say one\"]", 1),
                Tagged($"[DRAW:button id=b2 x={x} y={y + 80} text=\"Second\" prompt=\"say two\"]", 2),
            };
            var pills = DrawingRenderer.Layout(parts).Select(l => Rectangle.Round(l.ButtonRect!.Value)).ToArray();
            overlay.ShowDrawing(parts, parts.Select((p, i) => new CoreDrawing.PartAnimation(p, TimeSpan.FromMilliseconds(100 * i), TimeSpan.FromSeconds(0.3), false)).ToArray());
            overlay.ShowIdle();
            if (!PumpUntil(() => Visible(catchers[0]) && Visible(catchers[1]), TimeSpan.FromSeconds(3))) return "the click targets never appeared over the buttons";
            if (Visible(catchers[2])) return "a third click target was up for two buttons";
            for (var i = 0; i < 2; i++)
            {
                if (!NativeMethods.GetWindowRect(catchers[i], out var r) || new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) != pills[i])
                    return $"click target {i} is not over its pill {pills[i]}";
            }
            if (Visible(catchers[0]) && Environment.GetEnvironmentVariable("OPENCLICKY_BUTTON_SHOT") is { Length: > 0 } shot) // local eyeballing only
                Grab(Rectangle.Inflate(pills[0], 30, 30)).Save(shot, System.Drawing.Imaging.ImageFormat.Png);
            var pixels = Grab(pills[0]);
            overlay.Hide();
            if (catchers.Any(Visible)) return "Hide left a click target visible";
            Pump(IOverlayController.HideSettleMilliseconds + 60);
            var buttonChanged = CountChanged(pixels, Grab(pills[0]));
            if (buttonChanged < pills[0].Width * pills[0].Height / 3) return $"the screen shows no button pill (changed pixels={buttonChanged})";
            overlay.ShowIdle();
            if (!PumpUntil(() => Visible(catchers[0]) && Visible(catchers[1]), TimeSpan.FromSeconds(3))) return "the click targets did not come back after Hide";

            // A click on our own target raises the event; a press dragged off it does not.
            NativeMethods.SendMessage(catchers[1], WmLButtonDown, (IntPtr)1, Lp(5, 5));
            NativeMethods.SendMessage(catchers[1], WmLButtonUp, IntPtr.Zero, Lp(5, 5));
            if (clicks.Count != 1 || clicks[0] != ("Second", "say two")) return $"a click raised {clicks.Count} events: {string.Join("; ", clicks)}";
            NativeMethods.SendMessage(catchers[0], WmLButtonDown, (IntPtr)1, Lp(5, 5));
            NativeMethods.SendMessage(catchers[0], WmLButtonUp, IntPtr.Zero, Lp(5000, 5));
            if (clicks.Count != 1) return "a press released outside the button counted as a click";

            // A running agent task takes the targets down (its own clicks must reach the screen) and a stray click is ignored.
            overlay.DetachSuppressed = () => true;
            if (!PumpUntil(() => !Visible(catchers[0]) && !Visible(catchers[1]), TimeSpan.FromSeconds(2))) return "a running task left the click targets up";
            NativeMethods.SendMessage(catchers[0], WmLButtonDown, (IntPtr)1, Lp(5, 5));
            NativeMethods.SendMessage(catchers[0], WmLButtonUp, IntPtr.Zero, Lp(5, 5));
            if (clicks.Count != 1) return "a click was acted on while a task was running";
            overlay.DetachSuppressed = () => false;
            if (!PumpUntil(() => Visible(catchers[0]) && Visible(catchers[1]), TimeSpan.FromSeconds(2))) return "the click targets did not return after the task";

            // An open host dialog takes them down too (they are topmost and invisible, and would steal a click meant for Yes/No).
            overlay.DrawingButtonsSuppressed = () => true;
            if (!PumpUntil(() => !Visible(catchers[0]) && !Visible(catchers[1]), TimeSpan.FromSeconds(2))) return "an open dialog left the click targets up";
            NativeMethods.SendMessage(catchers[0], WmLButtonDown, (IntPtr)1, Lp(5, 5));
            NativeMethods.SendMessage(catchers[0], WmLButtonUp, IntPtr.Zero, Lp(5, 5));
            if (clicks.Count != 1) return "a click was acted on while a dialog was open";
            overlay.DrawingButtonsSuppressed = () => false;
            if (!PumpUntil(() => Visible(catchers[0]) && Visible(catchers[1]), TimeSpan.FromSeconds(2))) return "the click targets did not return after the dialog";

            // With the companion off, the frame loop stops once the drawing is still: the targets must still come back after a task.
            overlay.ApplyStyle(CompanionStyle.From(new AppSettings { Paused = true }, reducedMotion: false));
            Pump(500);
            overlay.DetachSuppressed = () => true;
            if (!PumpUntil(() => !Visible(catchers[0]) && !Visible(catchers[1]), TimeSpan.FromSeconds(2))) return "a running task left the click targets up (companion off)";
            Pump(300);
            overlay.DetachSuppressed = () => false;
            if (!PumpUntil(() => Visible(catchers[0]) && Visible(catchers[1]), TimeSpan.FromSeconds(2))) return "the click targets did not return after the task while the frame loop was stopped";
            overlay.ApplyStyle(CompanionStyle.From(new AppSettings(), reducedMotion: false));

            overlay.ShowDrawing([], []);
            if (!PumpUntil(() => !catchers.Any(Visible), TimeSpan.FromSeconds(2))) return "clearing the drawing left a click target up";
            return null;
        }
        finally
        {
            overlay.ButtonClicked -= OnClick;
            overlay.DetachSuppressed = null;
            overlay.DrawingButtonsSuppressed = null;
            overlay.ApplyStyle(CompanionStyle.From(new AppSettings(), reducedMotion: false));
            overlay.ShowDrawing([], []);
            overlay.Hide();
            Cursor.Position = saved;
        }
    }

    /// <summary>
    /// A figure part (parsed from a real block) opens its own window exactly over its box (the window rectangle Windows
    /// reports) with the click-through, non-activating styles, leaves the fixture in front, and paints the solid (pixels on the
    /// real screen); Hide hides it before returning, the next Show brings it back, and clearing the drawing destroys it. Only
    /// the cursor position is set; nothing is clicked or typed.
    /// </summary>
    private static string? ValidateFigure(BuddyOverlayController overlay, Form fixture)
    {
        const int GwlExStyle = -20;
        const long Required = 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000 | 0x00000008; // layered, transparent, tool, no-activate, topmost
        static bool Visible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);
        static void Pump(int ms) => PumpUntil(() => false, TimeSpan.FromMilliseconds(ms));
        var saved = Cursor.Position;
        var mon = Screen.PrimaryScreen!.Bounds;
        var box = new Rectangle(mon.Left + mon.Width / 4, mon.Top + mon.Height / 4, 320, 240);
        var (text, figures) = CoreDrawing.FigureBlockParser.Parse("A gear. ```figure\n{\"id\":\"g\",\"box\":[0,0,320,240],\"spin\":true,\"parts\":["
            + "{\"kind\":\"gear\",\"size\":[1,0.3,1],\"color\":\"#26C281\",\"label\":\"drive gear\",\"teeth\":14},{\"kind\":\"cylinder\",\"size\":[0.2,1,0.2]}]}\n```");
        if (text != "A gear." || figures.Count != 1) return $"the block did not parse: text=\"{text}\" figures={figures.Count}";
        // The op's box in virtual pixels, as after mapping.
        var op = CoreDrawing.FigureBlockParser.ToOp(figures[0]) with { Box = new(box.X, box.Y, box.Width, box.Height) };
        var part = new CoreDrawing.DrawPart("g", op, op, 1, 1);
        var baseline = overlay.WindowHandles.Count;
        try
        {
            Cursor.Position = new Point(mon.Right - 40, mon.Bottom - 40); // the companion stays clear
            overlay.ShowIdle();
            Pump(300);
            overlay.ShowDrawing([part], [new(part, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), false)]);
            if (!PumpUntil(() => overlay.WindowHandles.Count == baseline + 1, TimeSpan.FromSeconds(2))) return "no figure window was added to WindowHandles";
            var hwnd = overlay.WindowHandles[^1];
            if (!PumpUntil(() => Visible(hwnd), TimeSpan.FromSeconds(2))) return "the figure window never appeared";
            if (!NativeMethods.GetWindowRect(hwnd, out var r) || new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) != box)
                return $"the figure window [{r.Left},{r.Top},{r.Right - r.Left}x{r.Bottom - r.Top}] is not at its box {box}";
            var ex = (long)NativeMethods.GetWindowLongPtr(hwnd, GwlExStyle);
            if ((ex & Required) != Required) return $"the figure window styles 0x{ex:X} miss click-through / no-activate / topmost";
            if (NativeMethods.GetForegroundWindow() != fixture.Handle) return "the figure took the foreground";
            Pump(900); // past the 0.5 s scale-in

            var centre = new Rectangle(box.X + box.Width / 2 - 30, box.Y + box.Height / 2 - 30, 60, 60);
            if (Environment.GetEnvironmentVariable("OPENCLICKY_FIGURE_SHOT") is { Length: > 0 } shot) // local eyeballing only
                Grab(box).Save(shot, System.Drawing.Imaging.ImageFormat.Png);
            var shown = Grab(centre);
            overlay.Hide();
            if (Visible(hwnd)) return "Hide left the figure visible";
            Pump(IOverlayController.HideSettleMilliseconds + 60);
            var changed = CountChanged(shown, Grab(centre));
            if (changed < 600) return $"the screen shows no figure (changed pixels={changed} of 3600)";
            Console.WriteLine($"figure_pixels_changed={changed}");

            overlay.ShowIdle();
            if (!PumpUntil(() => Visible(hwnd), TimeSpan.FromSeconds(2))) return "the figure did not come back after Hide";
            overlay.ShowDrawing([], []);
            if (!PumpUntil(() => overlay.WindowHandles.Count == baseline && !NativeMethods.IsWindow(hwnd), TimeSpan.FromSeconds(2)))
                return "clearing the drawing left the figure window";
            return null;
        }
        finally
        {
            overlay.ShowDrawing([], []);
            overlay.Hide();
            Cursor.Position = saved;
        }
    }

    /// <summary>Six lines at most with "…" when cut, never wider than 300 DIP, and the 4 s hold then 400 ms fade.</summary>
    private static string? ValidateMessageBubble()
    {
        const float scale = 1.5f;
        var cut = CursorRenderer.LayoutMessage(string.Join(' ', Enumerable.Repeat("the companion explains this part of the screen", 40)), scale);
        if (cut.Lines.Length is 0 or > CursorRenderer.MessageMaxLines) return $"line count {cut.Lines.Length}";
        if (!cut.Lines[^1][^1].EndsWith('…')) return "cut text has no trailing ellipsis";
        if (cut.Size.Width > 300 * scale) return $"width {cut.Size.Width} > {300 * scale}";
        var whole = CursorRenderer.LayoutMessage("Short line.", scale);
        if (whole.Lines.Length != 1 || whole.Lines[0][^1].EndsWith('…')) return "short text was cut";
        static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.001f;
        if (!Near(BuddyOverlayController.MessageAlpha(4, 0, null, false), 1) || !Near(BuddyOverlayController.MessageAlpha(4.4, 0, null, false), 0))
            return "hold/fade timing";
        if (!Near(BuddyOverlayController.MessageAlpha(1, 0, 1, false), 1) || !Near(BuddyOverlayController.MessageAlpha(1.4, 0, 1, false), 0))
            return "idle fade timing";
        return null;
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

    /// <summary>
    /// Real windows, real hit-testing: docking flies to the notch and hides, a POINT grows out of it, a physical-style shake
    /// (the cursor is moved, never clicked) detaches the companion under a 36 DIP click target that really receives the
    /// click, and it reattaches after 3 s. Restores the cursor position.
    /// </summary>
    private static string? ValidateDockAndDetach(BuddyOverlayController overlay, Form fixture, Rectangle vs, int targetX, int targetY)
    {
        var cursorWindow = overlay.WindowHandle;
        var catcher = overlay.WindowHandles[2];
        var notch = new Point(vs.Left + vs.Width / 2, vs.Top + 4);
        overlay.SetNotchPoint(notch.X, notch.Y);
        static CompanionStyle Style(CursorPresence presence, bool shake = true) => new("#3380FF", presence, shake, true, false);
        static bool Visible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);
        static void Pump(int ms) => PumpUntil(() => false, TimeSpan.FromMilliseconds(ms));
        static bool Covers(IntPtr hwnd, Point p) =>
            NativeMethods.GetWindowRect(hwnd, out var r) && r.Left <= p.X && p.X < r.Right && r.Top <= p.Y && p.Y < r.Bottom;

        overlay.ApplyStyle(Style(CursorPresence.Docked));
        overlay.ShowIdle();
        overlay.ShowListening();
        overlay.ShowProcessing();
        Pump(400);
        if (Visible(cursorWindow)) return "docked companion showed for listening / thinking";

        overlay.PointAt(targetX, targetY, "dock-point");
        if (!Visible(cursorWindow) || !Covers(cursorWindow, notch)) return "a POINT did not start from the notch";
        if (!PumpUntil(() => overlay.IsPointFlyComplete, TimeSpan.FromSeconds(5))) return "the POINT flight from the notch did not settle";
        var lastVisibleAtNotch = false;
        overlay.ShowIdle();
        Pump(150);
        if (!Visible(cursorWindow)) return "ShowIdle did not fly the companion back";
        var docked = PumpUntil(() =>
        {
            if (Visible(cursorWindow)) lastVisibleAtNotch = Covers(cursorWindow, notch);
            return !Visible(cursorWindow);
        }, TimeSpan.FromSeconds(4));
        if (!docked) return "ShowIdle did not dock the companion";
        if (!lastVisibleAtNotch) return "the companion vanished away from the notch";

        overlay.ApplyStyle(Style(CursorPresence.Following));
        overlay.ShowIdle();
        if (!PumpUntil(() => Visible(cursorWindow), TimeSpan.FromSeconds(2))) return "leaving Docked did not bring the companion back";
        Pump(700);
        overlay.ApplyStyle(Style(CursorPresence.Docked));
        Pump(500);
        if (!Visible(cursorWindow)) return "docking did not hold its bubble first";
        if (!PumpUntil(() => !Visible(cursorWindow), TimeSpan.FromSeconds(5))) return "docking never finished";

        // Shake to detach. Only the cursor position is set; nothing is clicked or typed anywhere.
        overlay.ApplyStyle(Style(CursorPresence.Following));
        overlay.ShowIdle();
        Pump(900);
        var saved = Cursor.Position;
        var home = new Point(vs.Left + vs.Width / 2, vs.Top + vs.Height / 2);
        var clicks = 0;
        overlay.DetachedClicked += () => clicks++;
        void ShakeAt(Point at)
        {
            for (var i = 0; i < 16; i++)
            {
                Cursor.Position = new Point(at.X + (i % 2 == 0 ? 40 : -40), at.Y);
                Pump(25);
            }
        }
        void Shake() => ShakeAt(home);
        try
        {
            Shake();
            if (!PumpUntil(() => Visible(catcher), TimeSpan.FromSeconds(2))) return "a shake did not detach the companion";
            NativeMethods.GetWindowRect(catcher, out var box);
            var middle = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
            if (NativeMethods.WindowFromPoint(middle) != catcher) return "the click target is not what Windows hit-tests at the companion";
            // Press, drag away, release elsewhere: not a click, and it must not arm the next one.
            NativeMethods.SendMessage(catcher, 0x201, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.SendMessage(catcher, 0x202, IntPtr.Zero, (IntPtr)((500 << 16) | 500));
            Pump(100);
            if (clicks != 0 || !Visible(catcher)) return "a press dragged off the click target counted as a click";
            NativeMethods.SendMessage(catcher, 0x202, IntPtr.Zero, IntPtr.Zero);
            Pump(100);
            if (clicks != 0) return "a stray release armed a click";
            NativeMethods.SendMessage(catcher, 0x201, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.SendMessage(catcher, 0x202, IntPtr.Zero, IntPtr.Zero);
            if (!PumpUntil(() => clicks == 1 && !Visible(catcher), TimeSpan.FromSeconds(1))) return $"the click did not raise DetachedClicked once (clicks={clicks})";

            Pump(1100); // the detector's cooldown
            Shake();
            if (!PumpUntil(() => Visible(catcher), TimeSpan.FromSeconds(2))) return "a second shake did not detach";
            if (!PumpUntil(() => !Visible(catcher), TimeSpan.FromSeconds(4)) || clicks != 1) return "an unclicked companion did not reattach after 3 s";

            overlay.ApplyStyle(Style(CursorPresence.Following, shake: false));
            Pump(1100);
            Shake();
            Pump(400);
            if (Visible(catcher)) return "a shake detached with Shake to detach off";

            overlay.ApplyStyle(Style(CursorPresence.Following));
            Pump(1100);

            // A drag with the button held (over our own fixture) is a scribble, not a shake.
            var drag = new Point(fixture.Left + fixture.Width / 2, fixture.Top + fixture.Height / 2);
            Cursor.Position = drag;
            Pump(100);
            NativeMethods.mouse_event(0x0002, 0, 0, 0, 0);
            try
            {
                ShakeAt(drag);
                Pump(300);
                if (Visible(catcher)) return "a drag with the button held detached the companion";
            }
            finally
            {
                NativeMethods.mouse_event(0x0004, 0, 0, 0, 0);
                Pump(50);
            }

            // A running agent task injects input: no detaching, and a detached companion is dropped when one starts.
            var taskRunning = true;
            overlay.DetachSuppressed = () => taskRunning;
            Pump(1100);
            Shake();
            Pump(300);
            if (Visible(catcher)) return "a shake detached the companion while an agent task was running";
            taskRunning = false;
            Pump(1100);
            Shake();
            if (!PumpUntil(() => Visible(catcher), TimeSpan.FromSeconds(2))) return "a shake did not detach once the task stopped";
            taskRunning = true;
            if (!PumpUntil(() => !Visible(catcher), TimeSpan.FromSeconds(1))) return "a starting task left the click target up";
            overlay.DetachSuppressed = null;

            // A full-screen foreground window of another app (a game, F11 video) owns the mouse; one of ours (the shell surface
            // while it takes input) does not. A second process is not available here, so the window test is run on the geometry
            // check directly (it does see the fixture as full-screen once its own process is not ignored), and the live shake
            // proves that OpenClicky's own full-screen window leaves the companion free to detach.
            var monitor = Screen.FromPoint(home).Bounds;
            using (var full = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = monitor, Text = "OpenClicky full-screen fixture" })
            {
                full.Show();
                full.Activate();
                if (!PumpUntil(() => NativeMethods.GetForegroundWindow() == full.Handle, TimeSpan.FromSeconds(5))) return "the full-screen fixture did not become foreground";
                if (!ForegroundFullscreen.IsFullscreen(full.Handle, ignoreProcessId: 0)) return "a window covering its monitor was not seen as full-screen";
                if (ForegroundFullscreen.IsActive()) return "OpenClicky's own full-screen window counted as a full-screen app";
                Pump(1100);
                Shake();
                if (!PumpUntil(() => Visible(catcher), TimeSpan.FromSeconds(2))) return "a shake did not detach over OpenClicky's own full-screen window";
                if (!PumpUntil(() => !Visible(catcher), TimeSpan.FromSeconds(4))) return "an unclicked companion did not reattach after 3 s";
            }
            fixture.Activate();
            Pump(1100);
            Shake();
            if (!PumpUntil(() => Visible(catcher), TimeSpan.FromSeconds(2))) return "a shake did not detach after the full-screen window closed";
            overlay.Hide();
            if (overlay.WindowHandles.Any(Visible)) return "Hide left an overlay window (the click target?) visible";
        }
        finally
        {
            Cursor.Position = saved;
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

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern void mouse_event(int flags, int dx, int dy, int data, IntPtr extraInfo);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    }
}
