using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using OpenClicky.Core;

namespace OpenClicky.Overlay;

internal static class ResponseBubblePlacement
{
    private const float Gap = 12f;

    public static RectangleF Place(
        RectangleF desired,
        RectangleF workingArea,
        IReadOnlyList<RectangleF> annotationBounds)
    {
        var clamped = Clamp(desired, workingArea);
        var obstacles = annotationBounds
            .Where(bounds => bounds.Width > 0 && bounds.Height > 0 && bounds.IntersectsWith(workingArea))
            .ToArray();
        if (obstacles.Length == 0 || obstacles.All(bounds => !bounds.IntersectsWith(clamped)))
        {
            return clamped;
        }

        var union = obstacles[0];
        foreach (var bounds in obstacles.Skip(1))
        {
            union = RectangleF.Union(union, bounds);
        }

        var candidates = new[]
        {
            new RectangleF(clamped.X, union.Bottom + Gap, clamped.Width, clamped.Height),
            new RectangleF(clamped.X, union.Top - Gap - clamped.Height, clamped.Width, clamped.Height),
            new RectangleF(union.Right + Gap, clamped.Y, clamped.Width, clamped.Height),
            new RectangleF(union.Left - Gap - clamped.Width, clamped.Y, clamped.Width, clamped.Height),
        };
        foreach (var candidate in candidates)
        {
            if (Contains(workingArea, candidate)
                && obstacles.All(bounds => !bounds.IntersectsWith(candidate)))
            {
                return candidate;
            }
        }

        return clamped;
    }

    private static RectangleF Clamp(RectangleF rect, RectangleF bounds)
    {
        rect.X = Math.Clamp(rect.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - rect.Width));
        rect.Y = Math.Clamp(rect.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - rect.Height));
        return rect;
    }

    private static bool Contains(RectangleF outer, RectangleF inner) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top
        && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}

/// <summary>
/// Layered, topmost, click-through, non-activating overlay spanning the virtual screen.
/// </summary>
public sealed class BuddyOverlayController : IOverlayController
{
    private readonly OverlayForm _form;
    private CancellationTokenSource? _selectionCancellation;
    private bool _disposed;

    public BuddyOverlayController()
    {
        _form = new OverlayForm();
        _form.Text = "OpenClicky Overlay";
        _ = _form.Handle;
    }

    /// <summary>Underlying overlay HWND (full virtual-screen host). Smoke / diagnostics only.</summary>
    public IntPtr WindowHandle => _form.IsHandleCreated ? _form.Handle : IntPtr.Zero;

    /// <summary>
    /// Small tip HWND parked at the buddy tip during POINT (for position asserts / a11y).
    /// </summary>
    public IntPtr BuddyTipHandle => InvokeRead(() => _form.BuddyTipHandle);

    /// <summary>
    /// True once a POINT fly-to has finished easing to its target (still in Pointing mode).
    /// </summary>
    public bool IsPointFlyComplete => InvokeRead(() => _form.IsFlyComplete);

    /// <summary>Current fading trail sample count. Smoke / diagnostics only.</summary>
    public int TrailPointCount => InvokeRead(() => _form.TrailPointCount);

    /// <summary>
    /// Buddy tip position in virtual-screen coordinates after / during POINT.
    /// Returns false when not pointing.
    /// </summary>
    public bool TryGetBuddyVirtualPoint(out int virtualX, out int virtualY)
    {
        var x = 0;
        var y = 0;
        var ok = InvokeRead(() => _form.TryGetBuddyVirtual(out x, out y));
        virtualX = x;
        virtualY = y;
        return ok;
    }

    private T InvokeRead<T>(Func<T> read)
    {
        if (_form.IsHandleCreated && _form.InvokeRequired)
        {
            return (T)_form.Invoke(read)!;
        }

        return read();
    }

    public void ShowListening()
    {
        _form.BeginInvoke(() =>
        {
            _form.ClearPointing();
            _form.BuddyText = null;
            _form.Mode = OverlayMode.Listening;
            _form.ShowInactive();
        });
    }

    public void ShowProcessing()
    {
        _form.BeginInvoke(() =>
        {
            _form.ClearPointing();
            _form.BuddyText = null;
            _form.Mode = OverlayMode.Processing;
            _form.ShowInactive();
        });
    }

    public void ShowTranscript(string text) => _form.BeginInvoke(() =>
    {
        if (_form.Mode != OverlayMode.Listening) return;
        _form.BuddyText = text;
        _form.Invalidate();
    });

    public void ShowAnnotations(IReadOnlyList<VirtualAnnotation> marks) => _form.BeginInvoke(() =>
    {
        _form.Marks = marks.ToArray();
        _form.Invalidate();
    });

    public void ShowResponding(string spokenText)
    {
        _form.BeginInvoke(() =>
        {
            _form.BuddyText = spokenText;
            // Keep an in-flight / held POINT fly-to; Responding mode re-anchors to the cursor.
            var spokenMode = OverlayRespondingMode.Resolve(_form.IsPointingActive);
            _form.Mode = spokenMode switch
            {
                OverlaySpokenVisualMode.Pointing => OverlayMode.Pointing,
                OverlaySpokenVisualMode.Responding => OverlayMode.Responding,
                _ => throw new InvalidOperationException($"Unexpected spoken visual mode: {spokenMode}"),
            };

            _form.ShowInactive();
        });
    }

    public void PointAt(int virtualX, int virtualY, string? label, string? spokenText = null)
    {
        _form.BeginInvoke(() =>
        {
            _form.BeginFlyTo(new Point(virtualX, virtualY), label);
            if (spokenText is not null)
            {
                _form.BuddyText = spokenText;
            }

            _form.ShowInactive();
        });
    }

    public Task<Rectangle?> SelectRegionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromException<Rectangle?>(new ObjectDisposedException(nameof(BuddyOverlayController)));
        }

        if (!_form.InvokeRequired)
        {
            return SelectRegionOnUiThreadAsync(cancellationToken);
        }

        var completion = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _form.BeginInvoke(async () =>
        {
            try
            {
                completion.SetResult(await SelectRegionOnUiThreadAsync(cancellationToken));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private async Task<Rectangle?> SelectRegionOnUiThreadAsync(CancellationToken cancellationToken)
    {
        Hide();
        var selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _selectionCancellation = selectionCancellation;
        try
        {
            return await ScreenRegionSelector.SelectAsync(selectionCancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, selectionCancellation))
            {
                _selectionCancellation = null;
            }
            selectionCancellation.Dispose();
        }
    }

    public void Hide()
    {
        void HideNow()
        {
            _selectionCancellation?.Cancel();
            _form.ClearPointing();
            _form.Marks = [];
            _form.BuddyText = null;
            _form.Mode = OverlayMode.Hidden;
            _form.Hide();
        }
        if (_form.InvokeRequired) _form.Invoke(HideNow);
        else HideNow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _selectionCancellation?.Cancel();
        if (_form.IsHandleCreated)
        {
            _form.BeginInvoke(() => _form.Dispose());
        }
        else
        {
            _form.Dispose();
        }
    }

    private enum OverlayMode
    {
        Hidden,
        Listening,
        Processing,
        Responding,
        Pointing,
    }

    private sealed class OverlayForm : Form
    {
        private OverlayMode _mode = OverlayMode.Hidden;
        private float _pulse;
        private readonly System.Windows.Forms.Timer _timer;
        private PointF? _flyFrom;
        private PointF? _flyTo;
        private float _flyProgress = 1f;
        private PointF _buddyAnchor;
        private BuddyTipForm? _buddyTip;
        private readonly List<TrailPoint> _trail = [];
        private PointF? _lastTrailPoint;

        private const int MaxTrailPoints = 18;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            // Form.Show focuses managed TopMost forms; native SetWindowPos supplies topmost without activation.
            StartPosition = FormStartPosition.Manual;
            // Anti-aliased edges blend with the transparency key; black keeps their fringe neutral.
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
            DoubleBuffered = true;
            Bounds = SystemInformation.VirtualScreen;

            _timer = new System.Windows.Forms.Timer { Interval = 33 };
            _timer.Tick += (_, _) =>
            {
                _pulse += 0.12f;
                if (_flyProgress < 1f && _flyFrom is { } from && _flyTo is { } to)
                {
                    _flyProgress = Math.Min(1f, _flyProgress + 0.06f);
                    // Ease-out cubic
                    var t = 1f - MathF.Pow(1f - _flyProgress, 3f);
                    _buddyAnchor = new PointF(
                        from.X + (to.X - from.X) * t,
                        from.Y + (to.Y - from.Y) * t);
                    if (_flyProgress >= 1f)
                    {
                        PointTarget = new Point((int)to.X, (int)to.Y);
                    }
                }
                else if (_mode != OverlayMode.Pointing)
                {
                    var cursor = PointToClient(Cursor.Position);
                    _buddyAnchor = new PointF(cursor.X + 28, cursor.Y + 20);
                }

                UpdateTrail();

                SyncBuddyTipHwnd();
                Invalidate();
            };
        }

        public IntPtr BuddyTipHandle =>
            _buddyTip is { IsHandleCreated: true } ? _buddyTip.Handle : IntPtr.Zero;

        public OverlayMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                if (value == OverlayMode.Hidden) _timer.Stop();
                Invalidate();
            }
        }

        public string? BuddyText { get; set; }
        private IReadOnlyList<VirtualAnnotation> _marks = [];
        private long _marksStarted;
        public IReadOnlyList<VirtualAnnotation> Marks
        {
            get => _marks;
            set { _marks = value; _marksStarted = Environment.TickCount64; }
        }
        public Point? PointTarget { get; set; }
        public string? PointLabel { get; set; }

        /// <summary>True while flying to a target or holding at a POINT location.</summary>
        public bool IsPointingActive =>
            _mode == OverlayMode.Pointing
            || _flyProgress < 1f
            || PointTarget is not null
            || _flyTo is not null;

        public bool IsFlyComplete =>
            _mode == OverlayMode.Pointing && _flyProgress >= 1f && _flyTo is not null;

        public int TrailPointCount => _trail.Count;

        public bool TryGetBuddyVirtual(out int virtualX, out int virtualY)
        {
            virtualX = 0;
            virtualY = 0;
            if (_mode != OverlayMode.Pointing && PointTarget is null && _flyTo is null)
            {
                return false;
            }

            var local = _flyProgress < 1f && _flyTo is { } to
                ? new Point((int)Math.Round(_buddyAnchor.X), (int)Math.Round(_buddyAnchor.Y))
                : PointTarget ?? new Point((int)Math.Round(_buddyAnchor.X), (int)Math.Round(_buddyAnchor.Y));
            var screen = PointToScreen(local);
            virtualX = screen.X;
            virtualY = screen.Y;
            return true;
        }

        public void ClearPointing()
        {
            PointTarget = null;
            PointLabel = null;
            _flyFrom = null;
            _flyTo = null;
            _flyProgress = 1f;
            ClearTrail();
            HideBuddyTip();
        }

        private void UpdateTrail()
        {
            for (var i = _trail.Count - 1; i >= 0; i--)
            {
                var sample = _trail[i] with { Life = _trail[i].Life - 0.055f };
                if (sample.Life <= 0f) _trail.RemoveAt(i);
                else _trail[i] = sample;
            }

            if (_mode == OverlayMode.Hidden)
            {
                ClearTrail();
                return;
            }

            if (_lastTrailPoint is not { } last)
            {
                _lastTrailPoint = _buddyAnchor;
                return;
            }

            var dx = _buddyAnchor.X - last.X;
            var dy = _buddyAnchor.Y - last.Y;
            if (dx * dx + dy * dy < 4f)
            {
                return;
            }

            _trail.Add(new TrailPoint(last, 1f));
            if (_trail.Count > MaxTrailPoints)
            {
                _trail.RemoveRange(0, _trail.Count - MaxTrailPoints);
            }
            _lastTrailPoint = _buddyAnchor;
        }

        private void ClearTrail()
        {
            _trail.Clear();
            _lastTrailPoint = null;
        }

        private void SyncBuddyTipHwnd()
        {
            if (_mode != OverlayMode.Pointing)
            {
                HideBuddyTip();
                return;
            }

            _buddyTip ??= new BuddyTipForm();
            var screen = PointToScreen(new Point(
                (int)Math.Round(_buddyAnchor.X),
                (int)Math.Round(_buddyAnchor.Y)));
            const int tip = 24;
            if (!_buddyTip.Visible)
            {
                _buddyTip.ShowInactive();
            }
            NativeMethods.SetWindowPos(
                _buddyTip.Handle,
                NativeMethods.HwndTopMost,
                screen.X,
                screen.Y,
                tip,
                tip,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }

        private void HideBuddyTip()
        {
            if (_buddyTip is { IsHandleCreated: true, Visible: true })
            {
                _buddyTip.Hide();
            }
        }

        public void BeginFlyTo(Point virtualTarget, string? label)
        {
            var localTarget = PointToClient(virtualTarget);
            var cursor = PointToClient(Cursor.Position);
            _flyFrom = _mode == OverlayMode.Pointing ? _buddyAnchor : new PointF(cursor.X + 28, cursor.Y + 20);
            _flyTo = new PointF(localTarget.X, localTarget.Y);
            _flyProgress = SystemInformation.UIEffectsEnabled ? 0f : 1f;
            _buddyAnchor = _flyProgress == 1f ? _flyTo.Value : _flyFrom.Value;
            ClearTrail();
            _lastTrailPoint = _buddyAnchor;
            PointTarget = localTarget;
            PointLabel = label;
            // Keep any spoken bubble text set by a subsequent ShowResponding invoke.
            Mode = OverlayMode.Pointing;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WsExLayered = 0x00080000;
                const int WsExTransparent = 0x00000020;
                const int WsExToolWindow = 0x00000080;
                const int WsExNoActivate = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
                return cp;
            }
        }

        public void ShowInactive()
        {
            var bounds = SystemInformation.VirtualScreen;
            _timer.Start();
            if (!Visible)
            {
                Show();
            }

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HwndTopMost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_mode == OverlayMode.Hidden)
            {
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawMarks(g);

            PointF anchor;
            if (_mode is OverlayMode.Pointing)
            {
                anchor = _buddyAnchor;
            }
            else
            {
                var cursor = Cursor.Position;
                var origin = PointToClient(cursor);
                anchor = new PointF(origin.X + 28, origin.Y + 20);
                _buddyAnchor = anchor;
            }

            DrawTrail(g);

            var size = 24 + (float)Math.Sin(_pulse) * 2f;
            using var brush = new SolidBrush(Color.FromArgb(208, 230, 187));
            using var pen = new Pen(Color.FromArgb(37, 102, 76), 2f);
            g.FillEllipse(brush, anchor.X, anchor.Y, size, size);
            g.DrawEllipse(pen, anchor.X, anchor.Y, size, size);
            using var eye = new SolidBrush(Color.FromArgb(29, 43, 36));
            g.FillEllipse(eye, anchor.X + size * .29f, anchor.Y + size * .34f, 3, 3);
            g.FillEllipse(eye, anchor.X + size * .57f, anchor.Y + size * .34f, 3, 3);
            g.DrawArc(pen, anchor.X + size * .31f, anchor.Y + size * .36f, size * .38f, size * .34f, 20, 140);

            if (_mode == OverlayMode.Listening)
            {
                DrawWaveform(g, anchor.X + size + 10, anchor.Y, brush);
                if (!string.IsNullOrEmpty(BuddyText)) DrawBubble(g, anchor.X + size + 45, anchor.Y - 8, BuddyText);
            }
            else if (_mode == OverlayMode.Processing)
            {
                DrawSpinner(g, anchor.X + size + 16, anchor.Y + 6, pen);
            }
            else if (_mode is OverlayMode.Responding or OverlayMode.Pointing)
            {
                var text = BuddyText;
                if (_mode == OverlayMode.Pointing && !string.IsNullOrEmpty(PointLabel))
                {
                    text = string.IsNullOrEmpty(text) ? PointLabel : $"{PointLabel} — {text}";
                }

                if (!string.IsNullOrEmpty(text))
                {
                    DrawBubble(g, anchor.X + size + 8, anchor.Y - 8, text!, avoidAnnotations: true);
                }
            }
        }

        private void DrawTrail(Graphics g)
        {
            foreach (var sample in _trail)
            {
                var diameter = 4f + 8f * sample.Life;
                using var brush = new SolidBrush(Color.FromArgb(
                    Math.Clamp((int)(150 * sample.Life), 0, 150), 126, 232, 169));
                g.FillEllipse(
                    brush,
                    sample.Position.X + 12f - diameter / 2f,
                    sample.Position.Y + 12f - diameter / 2f,
                    diameter,
                    diameter);
            }
        }

        private void DrawMarks(Graphics g)
        {
            using var outline = new Pen(Color.FromArgb(126, 232, 169), 4f);
            using var halo = new Pen(Color.FromArgb(29, 43, 36), 8f);
            for (var index = 0; index < Marks.Count; index++)
            {
                var mark = Marks[index];
                var progress = SystemInformation.IsMenuAnimationEnabled
                    ? Math.Clamp((Environment.TickCount64 - _marksStarted - index * 350) / 350f, 0, 1) : 1;
                if (progress <= 0) continue;
                var start = PointToClient(new Point(mark.X, mark.Y));
                using var path = new GraphicsPath();
                if (mark.Shape is "polygon" or "freehand" or "curve" && mark.Points is { Count: >= 2 } points)
                {
                    var local = points.Select(point => PointToClient(new Point(point.X, point.Y))).ToArray();
                    if (mark.Shape == "curve" && local.Length == 4) path.AddBezier(local[0], local[1], local[2], local[3]);
                    else if (mark.Shape == "polygon") path.AddPolygon(local);
                    else path.AddLines(local);
                }
                else if (mark.Shape == "text")
                {
                    if (progress >= 1) DrawBubble(g, start.X, start.Y, mark.Label);
                    continue;
                }
                else if (mark.Shape == "arrow")
                {
                    var end = PointToClient(new Point(mark.A, mark.B));
                    path.AddLine(start, end);
                }
                else
                {
                    var rect = new Rectangle(start.X, start.Y, mark.A, mark.B);
                    if (mark.Shape == "circle") path.AddEllipse(rect);
                    else path.AddRectangle(rect);
                }
                using var visible = PartialStroke(path, progress);
                if (visible.PointCount > 1)
                {
                    using var head = new AdjustableArrowCap(4, 5, true);
                    if (mark.Shape == "arrow" && progress >= 1) outline.CustomEndCap = head;
                    g.DrawPath(halo, visible); g.DrawPath(outline, visible);
                    outline.EndCap = LineCap.Flat;
                }
                if (progress >= 1 && !string.IsNullOrWhiteSpace(mark.Label)) DrawBubble(g, start.X, start.Y - 36, mark.Label);
            }
        }

        private void DrawBubble(Graphics g, float x, float y, string text, bool avoidAnnotations = false)
        {
            var clipped = text.Length > 180 ? text[..177] + "…" : text;
            using var font = new Font("Segoe UI", 10f, FontStyle.Regular);
            var size = g.MeasureString(clipped, font, 320);
            var rect = new RectangleF(x, y, size.Width + 16, size.Height + 12);
            var bounds = RectangleToClient(Screen.FromPoint(PointToScreen(new Point((int)x, (int)y))).WorkingArea);
            rect = ResponseBubblePlacement.Place(
                rect,
                bounds,
                avoidAnnotations ? AnnotationBounds(bounds) : []);
            using var bg = new SolidBrush(Color.FromArgb(29, 43, 36));
            using var fg = new SolidBrush(Color.FromArgb(240, 240, 245));
            g.FillRoundedRectangle(bg, rect, 8);
            g.DrawString(clipped, font, fg, new RectangleF(rect.X + 8, rect.Y + 6, size.Width, size.Height));
        }

        private IReadOnlyList<RectangleF> AnnotationBounds(Rectangle workingArea)
        {
            var bounds = new List<RectangleF>(Marks.Count);
            foreach (var mark in Marks)
            {
                var start = PointToClient(new Point(mark.X, mark.Y));
                RectangleF markBounds;
                if (mark.Shape is "polygon" or "freehand" or "curve" && mark.Points is { Count: >= 2 } points)
                {
                    var local = points.Select(point => PointToClient(new Point(point.X, point.Y))).ToArray();
                    markBounds = RectangleF.FromLTRB(
                        local.Min(point => point.X), local.Min(point => point.Y),
                        local.Max(point => point.X), local.Max(point => point.Y));
                }
                else if (mark.Shape == "text")
                {
                    markBounds = new RectangleF(start.X, start.Y, 320, 60);
                }
                else if (mark.Shape == "arrow")
                {
                    var end = PointToClient(new Point(mark.A, mark.B));
                    markBounds = RectangleF.FromLTRB(
                        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                        Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
                }
                else
                {
                    markBounds = new RectangleF(start.X, start.Y, mark.A, mark.B);
                }

                // Include the widest painted halo and leave breathing room around the annotation.
                markBounds.Inflate(6f, 6f);
                if (markBounds.IntersectsWith(workingArea))
                {
                    bounds.Add(markBounds);
                }
            }

            return bounds;
        }

        private void DrawWaveform(Graphics g, float x, float y, Brush brush)
        {
            for (var i = 0; i < 4; i++)
            {
                var h = 6 + (float)Math.Abs(Math.Sin(_pulse + i)) * 14f;
                g.FillRectangle(brush, x + i * 7, y + 10 - h / 2, 4, h);
            }
        }

        private void DrawSpinner(Graphics g, float x, float y, Pen pen)
        {
            var rect = new RectangleF(x, y, 16, 16);
            g.DrawArc(pen, rect, (_pulse * 40f) % 360f, 270f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _buddyTip?.Dispose();
                _buddyTip = null;
            }

            base.Dispose(disposing);
        }

        private readonly record struct TrailPoint(PointF Position, float Life);
    }

    /// <summary>
    /// Click-through tip HWND parked at the buddy tip during POINT so callers can assert screen position.
    /// </summary>
    private sealed class BuddyTipForm : Form
    {
        public BuddyTipForm()
        {
            Text = "OpenClicky Buddy";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(24, 24);
            BackColor = Color.FromArgb(56, 132, 255);
            Opacity = 0.01; // nearly invisible; position is what matters for asserts
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WsExTransparent = 0x00000020;
                const int WsExToolWindow = 0x00000080;
                const int WsExNoActivate = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
                return cp;
            }
        }

        public void ShowInactive()
        {
            if (!Visible)
            {
                Show();
            }

            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HwndTopMost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
    }

    internal static GraphicsPath PartialStroke(GraphicsPath source, float progress)
    {
        if (progress >= 1) return (GraphicsPath)source.Clone();
        var visible = new GraphicsPath();
        if (progress <= 0 || source.PointCount < 2) return visible;
        using var flat = (GraphicsPath)source.Clone();
        flat.Flatten(null, .5f);
        var points = flat.PathPoints.ToList();
        if ((flat.PathTypes[^1] & (byte)PathPointType.CloseSubpath) != 0) points.Add(points[0]);
        static float Distance(PointF a, PointF b) => MathF.Sqrt(MathF.Pow(b.X - a.X, 2) + MathF.Pow(b.Y - a.Y, 2));
        var remaining = Enumerable.Range(1, points.Count - 1).Sum(i => Distance(points[i - 1], points[i])) * progress;
        for (var i = 1; i < points.Count && remaining > 0; i++)
        {
            var a = points[i - 1]; var b = points[i]; var length = Distance(a, b);
            if (length <= 0) continue;
            var portion = Math.Min(1, remaining / length);
            visible.AddLine(a, new PointF(a.X + (b.X - a.X) * portion, a.Y + (b.Y - a.Y) * portion));
            remaining -= length;
        }
        return visible;
    }

    private static class NativeMethods
    {
        public static readonly IntPtr HwndTopMost = new(-1);
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
