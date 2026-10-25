using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Overlay;

public enum ReplayMode
{
    /// <summary>A 960×600 DIP paper window with a title bar, centred on the cursor's monitor; the parts are scaled into it.</summary>
    Window,
    /// <summary>The parts at their original screen coordinates over the live screen; click-through, nothing takes focus.</summary>
    FullScreen,
    /// <summary>A full-monitor paper board; the parts are scaled to fit inside 48 DIP margins.</summary>
    Board,
}

/// <param name="Speak">Says one line and returns when it has been said; null paces each line by its saved duration instead.</param>
public sealed record ReplayOptions(Color Accent, bool ReducedMotion, bool ExcludeFromCapture, Func<string, CancellationToken, Task>? Speak, Action<string>? Log = null);

/// <summary>
/// Replays a saved drawing: builds a fresh <see cref="DrawingDocument"/> and applies each saved line in order, saying it (or
/// pacing by its duration) while its parts are drawn by the same renderer as the live drawing. Window and Board put the ink over
/// an owned paper form that takes focus (Space pauses or resumes, Esc closes); FullScreen draws over the live screen and takes
/// nothing. Pausing holds the pen and stops after the line being said. UI thread only; create, use and close it there.
/// </summary>
public sealed class DrawingReplayWindow : IDisposable
{
    public const float WindowWidthDip = 960, WindowHeightDip = 600, TitleBarDip = 44, CornerDip = 16, BoardMarginDip = 48, PaperPadDip = 24;
    private const int MaxFigures = 4;
    private const double FullScreenHoldSeconds = 4, LineGapSeconds = 0.25;
    private const uint WdaNone = 0, WdaExcludeFromCapture = 0x11;
    public static readonly Color Paper = ColorTranslator.FromHtml("#FBF6EA");

    private readonly SavedDrawing _drawing;
    private readonly ReplayMode _mode;
    private readonly ReplayOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _frame = new() { Interval = 16 };
    /// <summary>Makes sure the thread has the WinForms synchronization context, so the play loop resumes on the UI thread in every mode.</summary>
    private readonly Control _ui = new();
    private readonly ReplayForm? _form;
    private readonly DrawingWindow _ink;
    private readonly Dictionary<string, (DrawPart Part, FigureWindow Window)> _figures = [];
    private readonly Box? _fit;
    private double _held; // replay seconds before the last resume
    private double _resumedAt; // stopwatch seconds at the last resume
    private bool _paused, _closed;

    /// <summary>Raised once (UI thread) when the replay window closes, by the user, by Esc, or at the end of a full-screen replay.</summary>
    public event Action? Closed;

    /// <summary>Opens the replay and starts it. The play task never throws; a failure is logged and closes the replay.</summary>
    public DrawingReplayWindow(SavedDrawing drawing, ReplayMode mode, ReplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(options);
        _drawing = drawing;
        _mode = mode;
        _options = options;
        nint owner = 0;
        if (mode != ReplayMode.FullScreen)
        {
            var cursor = Cursor.Position;
            var screen = Screen.FromPoint(cursor);
            _form = new ReplayForm(mode, drawing.Title, DpiScale.ForPoint(cursor), screen, Close, TogglePause);
            _fit = _form.InkTarget;
            owner = _form.Handle;
            if (options.ExcludeFromCapture) Exclude(owner);
        }
        _ink = new DrawingWindow(options.Log, owner);
        if (options.ExcludeFromCapture) Exclude(_ink.Handle);
        _form?.ShowReplay();
        _frame.Tick += (_, _) => _ink.Render(Now, options.Accent, options.ReducedMotion);
        _frame.Start();
        _ = PlayAsync();
    }

    public bool IsOpen => !_closed;
    public bool IsPaused => _paused;

    /// <summary>Every window of the replay (the form, the ink and the figures), for capture exclusion and tests.</summary>
    public IReadOnlyList<nint> Handles => _closed ? [] : [.. (_form is null ? [] : new[] { _form.Handle }), _ink.Handle, .. _figures.Values.Select(f => f.Window.Handle)];

    /// <summary>Seconds of replay time: it stops while paused, which holds the pen where it is.</summary>
    private double Now => _paused ? _held : _held + _stopwatch.Elapsed.TotalSeconds - _resumedAt;

    /// <summary>Space: holds the pen and, once the line being said ends, the next line; or lets it go on.</summary>
    public void TogglePause()
    {
        if (_closed) return;
        if (_paused) { _paused = false; _resumedAt = _stopwatch.Elapsed.TotalSeconds; }
        else { _held = Now; _paused = true; }
        _form?.SetPaused(_paused);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _stop.Cancel();
        _frame.Stop();
        foreach (var (_, window) in _figures.Values) window.Close();
        _figures.Clear();
        _ink.HideWindow();
        _ink.Dispose();
        _frame.Dispose();
        _ui.Dispose();
        _form?.CloseReplay();
        Closed?.Invoke();
    }

    public void Dispose() => Close();

    private async Task PlayAsync()
    {
        try
        {
            var document = new DrawingDocument();
            foreach (var line in DrawingReplayPlan.Build(_drawing, _fit))
            {
                await WaitWhilePausedAsync();
                var animations = document.Apply(line.Ops, line.Duration);
                _ink.Show(document.Parts, animations, Now);
                SyncFigures(document.Parts, animations);
                if (_options.Speak is { } speak && line.Spoken.Length > 0)
                {
                    try { await speak(line.Spoken, _stop.Token); }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                    {
                        _options.Log?.Invoke("replay_speak_error=" + ex.GetType().Name);
                        await DelayAsync(line.Duration.TotalSeconds);
                    }
                    await DelayAsync(LineGapSeconds);
                }
                else await DelayAsync(line.Duration.TotalSeconds);
            }
            if (_mode == ReplayMode.FullScreen)
            {
                await DelayAsync(FullScreenHoldSeconds);
                Close();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _options.Log?.Invoke("replay_error=" + ex.GetType().Name);
            Close();
        }
    }

    private async Task WaitWhilePausedAsync()
    {
        while (_paused) await Task.Delay(50, _stop.Token);
        _stop.Token.ThrowIfCancellationRequested();
    }

    /// <summary>Waits <paramref name="seconds"/> of replay time (it does not run while paused).</summary>
    private async Task DelayAsync(double seconds)
    {
        var end = Now + Math.Max(0, seconds);
        while (Now < end) await Task.Delay(16, _stop.Token);
        _stop.Token.ThrowIfCancellationRequested();
    }

    /// <summary>One window per figure part (the newest few), as the live overlay does; owned by the paper form so they stay above it.</summary>
    private void SyncFigures(IReadOnlyList<DrawPart> parts, IReadOnlyList<PartAnimation> animations)
    {
        var wanted = parts.Where(p => p.Op is { Kind: DrawOpKind.Figure, Figure: not null }).OrderByDescending(p => p.Seq).Take(MaxFigures).ToDictionary(p => p.Id);
        foreach (var (id, (part, window)) in _figures.ToArray())
        {
            if (wanted.TryGetValue(id, out var keep) && Equals(keep, part)) continue;
            window.Close();
            _figures.Remove(id);
        }
        foreach (var part in wanted.Values)
        {
            if (_figures.ContainsKey(part.Id) || FigureWindow.BoundsOf(part.Op) is not { } bounds) continue;
            var reveal = animations.FirstOrDefault(a => !a.Erase && a.Part.Id == part.Id && a.Part.Seq == part.Seq);
            var window = new FigureWindow(part.Op.Figure!, bounds, _options.Accent, _options.ReducedMotion, reveal?.Start);
            if (_form is not null) new WindowInteropHelper(window) { Owner = _form.Handle };
            if (_options.ExcludeFromCapture) Exclude(window.Handle); // before it is first shown
            _figures[part.Id] = (part, window);
            window.ShowAt();
        }
    }

    private static void Exclude(nint hwnd)
    {
        if (hwnd != 0) SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    /// <summary>The paper: a rounded window with a dark title bar (Window) or a full-monitor board (Board). Paints no ink; the ink window above it does.</summary>
    private sealed class ReplayForm : Form
    {
        private readonly ReplayMode _mode;
        private readonly string _title;
        private readonly float _dip;
        private readonly Action _close, _togglePause;
        private readonly Rectangle _bounds;
        private bool _paused, _overClose;

        public ReplayForm(ReplayMode mode, string title, float dip, Screen screen, Action close, Action togglePause)
        {
            _mode = mode;
            _title = title;
            _dip = dip;
            _close = close;
            _togglePause = togglePause;
            var work = screen.WorkingArea;
            if (mode == ReplayMode.Board) _bounds = screen.Bounds;
            else
            {
                var size = new Size(Math.Min((int)(WindowWidthDip * dip), work.Width), Math.Min((int)(WindowHeightDip * dip), work.Height));
                _bounds = new Rectangle(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2, size.Width, size.Height);
            }
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Text = "OpenClicky replay";
            BackColor = Paper;
            Bounds = _bounds;
            FormClosed += (_, _) => _close(); // Alt+F4 or any other way the form goes
            if (mode == ReplayMode.Window)
            {
                using var path = RoundedPath(new RectangleF(0, 0, _bounds.Width, _bounds.Height), CornerDip * dip);
                Region = new Region(path);
            }
        }

        /// <summary>Where the ink goes, in virtual-screen pixels: the paper inset by its padding, or the board inset by its margins.</summary>
        public Box InkTarget
        {
            get
            {
                var top = _mode == ReplayMode.Window ? TitleBarDip * _dip : 0;
                var inset = (_mode == ReplayMode.Window ? PaperPadDip : BoardMarginDip) * _dip;
                return new Box(_bounds.X + inset, _bounds.Y + top + inset, _bounds.Width - 2 * inset, _bounds.Height - top - 2 * inset);
            }
        }

        public void ShowReplay()
        {
            Show();
            Activate();
        }

        public void CloseReplay() => Close();

        public void SetPaused(bool paused)
        {
            _paused = paused;
            Invalidate();
        }

        private RectangleF CloseRect => _mode == ReplayMode.Window
            ? new(Width - TitleBarDip * _dip, 0, TitleBarDip * _dip, TitleBarDip * _dip)
            : new(Width - 56 * _dip, 12 * _dip, 36 * _dip, 36 * _dip);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var semibold = new Font("Segoe UI Semibold", 13 * _dip, FontStyle.Regular, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 12 * _dip, FontStyle.Regular, GraphicsUnit.Pixel);
            var middle = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var close = CloseRect;
            if (_mode == ReplayMode.Window)
            {
                var bar = new RectangleF(0, 0, Width, TitleBarDip * _dip);
                using (var dark = new SolidBrush(Color.FromArgb(0x23, 0x26, 0x2B))) g.FillRectangle(dark, bar);
                using var light = new SolidBrush(Color.FromArgb(0xF2, 0xF4, 0xF7));
                var titleRect = new RectangleF(16 * _dip, 0, close.Left - 32 * _dip, bar.Height);
                g.DrawString(_paused ? _title + "  ·  paused" : _title, semibold, light, titleRect, middle);
                DrawClose(g, close, light, hover: _overClose);
            }
            else
            {
                using var ink = new SolidBrush(Color.FromArgb(0x3A, 0x3A, 0x42));
                using var faint = new SolidBrush(Color.FromArgb(0x8A, 0x86, 0x7C));
                g.DrawString(_paused ? _title + "  ·  paused" : _title, semibold, ink, new RectangleF(BoardMarginDip * _dip, 12 * _dip, close.Left - 2 * BoardMarginDip * _dip, 36 * _dip), middle);
                g.DrawString("Space pauses or resumes · Esc closes", small, faint,
                    new RectangleF(0, Height - 40 * _dip, Width, 28 * _dip), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                DrawClose(g, close, ink, hover: _overClose);
            }
        }

        private void DrawClose(Graphics g, RectangleF rect, Brush brush, bool hover)
        {
            if (hover)
            {
                using var back = new SolidBrush(Color.FromArgb(48, 128, 128, 128));
                g.FillRectangle(back, rect);
            }
            var c = new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            var r = 6 * _dip;
            using var pen = new Pen(brush is SolidBrush solid ? solid.Color : Color.Gray, 1.6f * _dip) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, c.X - r, c.Y - r, c.X + r, c.Y + r);
            g.DrawLine(pen, c.X - r, c.Y + r, c.X + r, c.Y - r);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var over = CloseRect.Contains(e.Location);
            if (over == _overClose) return;
            _overClose = over;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_overClose) return;
            _overClose = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location)) _close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) _close();
            else if (e.KeyCode == Keys.Space) _togglePause();
            else return;
            e.Handled = e.SuppressKeyPress = true;
        }

        private static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
