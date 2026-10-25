using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenClicky.Core.Drawing;
using OpenClicky.Overlay;

namespace OpenClicky.Overlay.Smoke;

// Saved-drawing replay and print checks. ValidateReplayGeometry is pure (no window). ValidateReplayWindows opens the real
// replay windows for a few seconds and inspects them on the real screen; it posts Space and Esc only to the replay window
// itself and sends no input to any other window.
internal static partial class Program
{
    private static DrawPart Painted(string id, DrawOpKind kind, Box box, int seq, Pt[]? points = null, string? label = null, string colour = "#FF0000", FigureSpec? figure = null)
    {
        var args = new Dictionary<string, string>();
        if (label is not null) args["label"] = label;
        var op = new DrawOp(kind, id, points ?? [], box, label, colour, 1, args, 0, figure);
        return new(id, op, op, 1, seq);
    }

    private static FigureSpec GearFigure(float x, float y, float w, float h) => new("gear", new Box(x, y, w, h), null, false, "Gear train", [
        new FigurePart(FigureKind.Gear, new(2, 2, 0.5f), new(0, 0, 0), new(0, 0, 0), "#E4572E", "Driver", new Dictionary<string, double> { ["teeth"] = 12 }, null),
        new FigurePart(FigureKind.Box, new(1, 1, 1), new(3, 0, 0), new(0, 0, 0), "", "Follower", new Dictionary<string, double>(), null)]);

    /// <summary>
    /// Layout against an off-monitor rectangle (the print canvas) keeps what is inside it and drops what crosses it; RenderStill paints
    /// the finished parts on paper (ink where the rect is, a figure as its label pills, nothing for a spotlight).
    /// </summary>
    private static string? ValidateReplayGeometry()
    {
        var vs = SystemInformation.VirtualScreen;
        var canvas = new Rectangle(vs.Right + 5000, vs.Top, 600, 400); // on no monitor
        var inside = Painted("in", DrawOpKind.Rect, new(canvas.X + 50, canvas.Y + 50, 200, 100), 1, label: "pill");
        var crossing = Painted("out", DrawOpKind.Rect, new(canvas.X + 560, canvas.Y + 50, 100, 60), 2);
        if (DrawingRenderer.Layout([inside]).Count != 0) return "a part off every monitor was laid out against the live screen";
        var onCanvas = DrawingRenderer.Layout([inside, crossing], canvas, 2f);
        if (onCanvas.Count != 1 || onCanvas[0].Part.Id != "in" || onCanvas[0].Dip != 2f) return $"canvas layout kept {onCanvas.Count} parts (dip {onCanvas.FirstOrDefault()?.Dip})";
        if (onCanvas[0].Pill is not { } pill || !RectangleF.Inflate(canvas, 1, 1).Contains(pill)) return "the label pill left the canvas";

        var accent = Color.FromArgb(0x33, 0x80, 0xFF);
        var size = new Size(600, 400);
        DrawPart[] parts =
        [
            Painted("box", DrawOpKind.Rect, new(20, 20, 200, 100), 1),
            Painted("fig", DrawOpKind.Figure, new(300, 100, 200, 150), 2, figure: GearFigure(300, 100, 200, 150)),
            Painted("dim", DrawOpKind.Spotlight, new(100, 100, 50, 50), 3),
        ];
        using var still = DrawingRenderer.RenderStill(parts, size, Color.White, accent, 1f);
        if (still.Size != size) return "still has the wrong size";
        if (still.GetPixel(590, 390) != Color.FromArgb(255, 255, 255, 255)) return "a spotlight dimmed the printed page, or the paper is not white";
        int Count(Rectangle r, Func<Color, bool> test)
        {
            var n = 0;
            for (var y = r.Top; y < r.Bottom; y++)
                for (var x = r.Left; x < r.Right; x++)
                    if (test(still.GetPixel(x, y))) n++;
            return n;
        }
        if (Count(new(0, 0, 260, 160), c => c.R > 200 && c.G < 90 && c.B < 90) < 200) return "the rect's red ink is missing from the still";
        bool IsAccent(Color c) => Math.Abs(c.R - accent.R) < 10 && Math.Abs(c.G - accent.G) < 10 && Math.Abs(c.B - accent.B) < 10;
        if (Count(new(300, 100, 200, 150), IsAccent) < 300) return "the figure's label pills are missing from the still";
        if (Count(new(0, 150, 300, 250), c => c.R < 250 || c.G < 250 || c.B < 250) != 0 || Count(new(520, 0, 80, 400), c => c.R < 250 || c.G < 250 || c.B < 250) != 0)
            return "something was painted where no part is";
        return null;
    }

    private static string? ValidateReplayWindows()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        var mon = screen.Bounds;
        var dip = DpiScale.ForPoint(cursor);
        DrawOp Op(DrawOpKind kind, string id, Box box, string colour = "#FF0000", string? text = null, FigureSpec? figure = null, params Pt[] points) =>
            new(kind, id, points, box, text, colour, 1, new Dictionary<string, string>(), 0, figure);
        float cx = mon.Left + mon.Width / 2f, cy = mon.Top + mon.Height / 2f;
        var drawing = new SavedDrawing("smoke", "Smoke drawing", DateTimeOffset.UtcNow,
        [
            new("First line.", 0.8, [Op(DrawOpKind.Rect, "box", new(cx - 300, cy - 200, 360, 200)), Op(DrawOpKind.Dot, "dot", new(cx - 100, cy + 50, 0, 0), points: [new Pt(cx - 100, cy + 50)])]),
            new("Second line.", 0.8, [Op(DrawOpKind.Figure, "fig", new(cx + 100, cy - 150, 260, 200), figure: GearFigure(100, 100, 260, 200))]),
        ], [new Box(mon.Left, mon.Top, mon.Width, mon.Height)]);
        var accent = Color.FromArgb(0x33, 0x80, 0xFF);
        ReplayOptions Options(Func<string, CancellationToken, Task>? speak = null, bool exclude = false) => new(accent, false, exclude, speak, Console.WriteLine);
        var fg0 = ReplayNative.GetForegroundWindow();
        var closed = 0;

        // 1. Full screen: one click-through, non-activating window at the original coordinates; nothing takes focus.
        using var before = GrabLayered(Rectangle.Intersect(mon, new Rectangle((int)cx - 320, (int)cy - 220, 700, 320)));
        var full = new DrawingReplayWindow(drawing, ReplayMode.FullScreen, Options());
        full.Closed += () => closed++;
        PumpUntil(() => false, TimeSpan.FromMilliseconds(700));
        if (full.Handles.Count < 1 || !NativeMethods.IsWindowVisible(full.Handles[0])) return "full screen: the ink window is not showing";
        var ink = full.Handles[0];
        var ex = (long)NativeMethods.GetWindowLongPtr(ink, -20);
        if ((ex & 0x08000000) == 0 || (ex & 0x20) == 0 || (ex & 0x80000) == 0) return $"full screen: ink is not layered, click-through and non-activating (ex=0x{ex:X})";
        if (ReplayNative.GetForegroundWindow() != fg0) return "full screen: the foreground window changed";
        using (var during = GrabLayered(Rectangle.Intersect(mon, new Rectangle((int)cx - 320, (int)cy - 220, 700, 320))))
            if (CountChanged(before, during) < 150) return "full screen: the rect was not drawn over the live screen";
        PumpUntil(() => false, TimeSpan.FromMilliseconds(1400));
        if (full.Handles.Count < 2) return "full screen: the figure window did not open with the second line";
        full.Close();
        if (closed != 1 || full.IsOpen || NativeMethods.IsWindow(ink) || full.Handles.Count != 0) return "full screen: Close left a window or did not raise Closed once";
        full.Close();
        if (closed != 1) return "full screen: a second Close raised Closed again";

        // 1b. "Show in screen recordings" off: every replay window, a figure's too, is excluded from capture.
        var hidden = new DrawingReplayWindow(drawing, ReplayMode.FullScreen, Options(exclude: true));
        PumpUntil(() => hidden.Handles.Count >= 2, TimeSpan.FromSeconds(3));
        PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
        if (hidden.Handles.Count < 2 || hidden.Handles.Any(h => !NativeMethods.GetWindowDisplayAffinity(h, out var affinity) || affinity != 0x11)) return "full screen: a replay or figure window is not excluded from capture";
        hidden.Close();

        // 2. Window: a 960x600 DIP paper form centred on the cursor monitor, ink above it, Space pauses, Esc closes.
        var window = new DrawingReplayWindow(drawing, ReplayMode.Window, Options());
        window.Closed += () => closed++;
        PumpUntil(() => false, TimeSpan.FromMilliseconds(2600));
        var form = window.Handles[0];
        if (!NativeMethods.GetWindowRect(form, out var wr)) return "window: no form";
        var work = screen.WorkingArea;
        var wantW = Math.Min((int)(960 * dip), work.Width);
        var wantH = Math.Min((int)(600 * dip), work.Height);
        if (Math.Abs(wr.Right - wr.Left - wantW) > 2 || Math.Abs(wr.Bottom - wr.Top - wantH) > 2) return $"window: size {wr.Right - wr.Left}x{wr.Bottom - wr.Top}, wanted {wantW}x{wantH}";
        if (Math.Abs(wr.Left - (work.Left + (work.Width - wantW) / 2)) > 2 || Math.Abs(wr.Top - (work.Top + (work.Height - wantH) / 2)) > 2) return "window: not centred on the cursor's monitor";
        if (((long)NativeMethods.GetWindowLongPtr(form, -20) & 0x08000000) != 0) return "window: the paper form must be able to take focus";
        if (window.Handles.Count < 3) return $"window: expected the form, the ink and the figure, got {window.Handles.Count}";
        foreach (var owned in window.Handles.Skip(1))
            if (ReplayNative.GetWindow(owned, 4) != form) return "window: the ink or a figure is not owned by the form (it could fall behind it)";
        var rect = new Rectangle(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top);
        using (var shot = GrabLayered(rect))
        {
            bool Near(Color c, int r, int g, int b) => Math.Abs(c.R - r) <= 8 && Math.Abs(c.G - g) <= 8 && Math.Abs(c.B - b) <= 8;
            if (!Near(shot.GetPixel((int)(rect.Width * 0.62), (int)(10 * dip)), 0x23, 0x26, 0x2B)) return "window: no dark title bar";
            if (!Near(shot.GetPixel(rect.Width - (int)(30 * dip), rect.Height - (int)(40 * dip)), 0xFB, 0xF6, 0xEA)) return "window: the paper is not #FBF6EA";
            var red = 0;
            for (var y = (int)(44 * dip); y < rect.Height; y += 2)
                for (var x = 0; x < rect.Width; x += 2)
                    if (shot.GetPixel(x, y) is { R: > 200, G: < 90, B: < 90 }) red++;
            if (red < 40) return $"window: the ink is not over the paper ({red} red samples)";
        }
        PostKey(form, 0x20);
        PumpUntil(() => window.IsPaused, TimeSpan.FromSeconds(1));
        if (!window.IsPaused) return "window: Space did not pause";
        PostKey(form, 0x20);
        PumpUntil(() => !window.IsPaused, TimeSpan.FromSeconds(1));
        if (window.IsPaused) return "window: Space did not resume";
        PostKey(form, 0x1B);
        PumpUntil(() => !window.IsOpen, TimeSpan.FromSeconds(2));
        if (window.IsOpen || closed != 2 || NativeMethods.IsWindow(form)) return "window: Esc did not close the replay";

        // 2b. Alt+F4 (WM_CLOSE to the form): the replay closes once, without closing the form again from inside FormClosed.
        var alt = new DrawingReplayWindow(drawing, ReplayMode.Window, Options());
        var altClosed = 0;
        alt.Closed += () => altClosed++;
        PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
        var altForm = alt.Handles[0];
        ReplayNative.PostMessage(altForm, 0x0010, IntPtr.Zero, IntPtr.Zero);
        PumpUntil(() => !alt.IsOpen, TimeSpan.FromSeconds(2));
        if (alt.IsOpen || altClosed != 1 || NativeMethods.IsWindow(altForm)) return "window: Alt+F4 did not close the replay exactly once";

        // 3. Board: the whole monitor.
        var board = new DrawingReplayWindow(drawing, ReplayMode.Board, Options());
        PumpUntil(() => false, TimeSpan.FromMilliseconds(500));
        if (!NativeMethods.GetWindowRect(board.Handles[0], out var br) || new Rectangle(br.Left, br.Top, br.Right - br.Left, br.Bottom - br.Top) != mon) return "board: not the whole monitor";
        board.Close();

        // 4. Speaking: lines are said in order; pausing lets the line being said finish and holds the next one until resumed.
        var spoken = new List<string>();
        Task Speak(string text, CancellationToken token) { spoken.Add(text); return Task.Delay(400, token); }
        var talking = new DrawingReplayWindow(drawing, ReplayMode.FullScreen, Options(Speak));
        PumpUntil(() => false, TimeSpan.FromMilliseconds(100));
        if (!spoken.SequenceEqual(["First line."])) return "speech: the first line was not said first";
        talking.TogglePause();
        PumpUntil(() => false, TimeSpan.FromMilliseconds(1300));
        if (!spoken.SequenceEqual(["First line."])) return "speech: the second line started while paused";
        talking.TogglePause();
        PumpUntil(() => spoken.Count == 2, TimeSpan.FromSeconds(2));
        if (!spoken.SequenceEqual(["First line.", "Second line."])) return "speech: the second line did not follow after resuming";
        talking.Close();
        return null;
    }

    /// <summary>The screen as the user sees it: CAPTUREBLT includes layered windows (the ink), which a plain copy leaves out.</summary>
    private static Bitmap GrabLayered(Rectangle area)
    {
        var bitmap = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(bitmap);
        var destination = g.GetHdc();
        var screenDc = ReplayNative.GetDC(IntPtr.Zero);
        try { ReplayNative.BitBlt(destination, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, 0x00CC0020 | 0x40000000); }
        finally
        {
            ReplayNative.ReleaseDC(IntPtr.Zero, screenDc);
            g.ReleaseHdc(destination);
        }
        return bitmap;
    }


    private static void PostKey(IntPtr hwnd, int key)
    {
        ReplayNative.PostMessage(hwnd, 0x0100, (IntPtr)key, IntPtr.Zero);
        ReplayNative.PostMessage(hwnd, 0x0101, (IntPtr)key, IntPtr.Zero);
    }

    private static class ReplayNative
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}
