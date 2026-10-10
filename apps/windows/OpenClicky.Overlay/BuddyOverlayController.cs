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
        // Force handle creation so BeginInvoke works from the keyboard-hook thread.
        _ = _form.Handle;
    }

    public void ShowListening()
    {
        _form.BeginInvoke(() =>
        {
            _form.Mode = OverlayMode.Listening;
            _form.ShowInactive();
        });
    }

    public void ShowProcessing()
    {
        _form.BeginInvoke(() =>
        {
            _form.Mode = OverlayMode.Processing;
            _form.ShowInactive();
        });
    }

    public void Hide()
    {
        _form.BeginInvoke(() =>
        {
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
    }

    private sealed class OverlayForm : Form
    {
        private OverlayMode _mode = OverlayMode.Hidden;
        private float _pulse;
        private readonly System.Windows.Forms.Timer _timer;

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

            // Buddy near primary cursor, offset slightly.
            var cursor = Cursor.Position;
            var origin = PointToClient(cursor);
            var x = origin.X + 28;
            var y = origin.Y + 20;
            var size = 22 + (float)Math.Sin(_pulse) * 3f;

            using var brush = new SolidBrush(Color.FromArgb(220, 56, 132, 255));
            using var pen = new Pen(Color.FromArgb(255, 180, 220, 255), 2f);
            var points = new[]
            {
                new PointF(x, y),
                new PointF(x + size, y + size * 0.45f),
                new PointF(x + size * 0.2f, y + size),
            };
            g.FillPolygon(brush, points);
            g.DrawPolygon(pen, points);

            if (_mode == OverlayMode.Listening)
            {
                DrawWaveform(g, x + size + 10, y, brush);
            }
            else if (_mode == OverlayMode.Processing)
            {
                DrawSpinner(g, x + size + 16, y + 6, pen);
            }
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
