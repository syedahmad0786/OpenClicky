using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using OpenClicky.Core;

namespace OpenClicky.Overlay;

/// <summary>
/// Layered, topmost, click-through, non-activating overlay spanning the virtual screen.
/// </summary>
public sealed class BuddyOverlayController : IOverlayController
{
    private readonly OverlayForm _form;
    private bool _disposed;

    public BuddyOverlayController()
    {
        _form = new OverlayForm();
        _ = _form.Handle;
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

    public void ShowResponding(string spokenText)
    {
        _form.BeginInvoke(() =>
        {
            _form.BuddyText = spokenText;
            // Talk path calls PointAt then ShowResponding — keep the fly-to anchor so the
            // buddy does not snap back to the cursor while TTS plays.
            if (_form.IsPointingActive)
            {
                _form.Mode = OverlayMode.Pointing;
            }
            else
            {
                _form.Mode = OverlayMode.Responding;
            }

            _form.ShowInactive();
        });
    }

    public void PointAt(int virtualX, int virtualY, string? label)
    {
        _form.BeginInvoke(() =>
        {
            _form.BeginFlyTo(new Point(virtualX, virtualY), label);
            _form.ShowInactive();
        });
    }

    public void Hide()
    {
        _form.BeginInvoke(() =>
        {
            _form.ClearPointing();
            _form.BuddyText = null;
            _form.Mode = OverlayMode.Hidden;
            _form.Hide();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Bounds = SystemInformation.VirtualScreen;

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
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

                Invalidate();
            };
            _timer.Start();
        }

        public OverlayMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                Invalidate();
            }
        }

        public string? BuddyText { get; set; }
        public Point? PointTarget { get; set; }
        public string? PointLabel { get; set; }

        /// <summary>True while flying to a target or holding at a POINT location.</summary>
        public bool IsPointingActive =>
            _mode == OverlayMode.Pointing
            || _flyProgress < 1f
            || PointTarget is not null
            || _flyTo is not null;

        public void ClearPointing()
        {
            PointTarget = null;
            PointLabel = null;
            _flyFrom = null;
            _flyTo = null;
            _flyProgress = 1f;
        }

        public void BeginFlyTo(Point virtualTarget, string? label)
        {
            var localTarget = PointToClient(virtualTarget);
            var cursor = PointToClient(Cursor.Position);
            _flyFrom = _mode == OverlayMode.Pointing ? _buddyAnchor : new PointF(cursor.X + 28, cursor.Y + 20);
            _flyTo = new PointF(localTarget.X, localTarget.Y);
            _flyProgress = 0f;
            _buddyAnchor = _flyFrom.Value;
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
            Bounds = SystemInformation.VirtualScreen;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_mode == OverlayMode.Hidden)
            {
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

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

            var size = 22 + (float)Math.Sin(_pulse) * 3f;
            using var brush = new SolidBrush(Color.FromArgb(220, 56, 132, 255));
            using var pen = new Pen(Color.FromArgb(255, 180, 220, 255), 2f);
            var points = new[]
            {
                new PointF(anchor.X, anchor.Y),
                new PointF(anchor.X + size, anchor.Y + size * 0.45f),
                new PointF(anchor.X + size * 0.2f, anchor.Y + size),
            };
            g.FillPolygon(brush, points);
            g.DrawPolygon(pen, points);

            if (_mode == OverlayMode.Listening)
            {
                DrawWaveform(g, anchor.X + size + 10, anchor.Y, brush);
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
                    DrawBubble(g, anchor.X + size + 8, anchor.Y - 8, text!);
                }
            }
        }

        private static void DrawBubble(Graphics g, float x, float y, string text)
        {
            var clipped = text.Length > 180 ? text[..177] + "…" : text;
            using var font = new Font("Segoe UI", 10f, FontStyle.Regular);
            var size = g.MeasureString(clipped, font, 320);
            var rect = new RectangleF(x, y, size.Width + 16, size.Height + 12);
            using var bg = new SolidBrush(Color.FromArgb(210, 20, 24, 32));
            using var fg = new SolidBrush(Color.FromArgb(240, 240, 245));
            g.FillRoundedRectangle(bg, rect, 8);
            g.DrawString(clipped, font, fg, new RectangleF(rect.X + 8, rect.Y + 6, size.Width, size.Height));
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
            }

            base.Dispose(disposing);
        }
    }

    private static class NativeMethods
    {
        public static readonly IntPtr HwndTopMost = new(-1);
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;

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
