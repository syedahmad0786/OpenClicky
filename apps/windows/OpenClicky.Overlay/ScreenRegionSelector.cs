using System.Drawing.Drawing2D;

namespace OpenClicky.Overlay;

/// <summary>Interactive, user-activated virtual-screen rectangle picker.</summary>
internal static class ScreenRegionSelector
{
    public static Task<Rectangle?> SelectAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<Rectangle?>(null);
        }

        var selector = new SelectionForm();
        _ = selector.Handle;
        var completion = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        selector.SelectionFinished += (_, region) => completion.TrySetResult(region);
        selector.FormClosed += (_, _) => completion.TrySetResult(null);
        var cancellation = cancellationToken.Register(() =>
        {
            if (selector.IsHandleCreated)
            {
                selector.BeginInvoke(selector.Cancel);
            }
        });
        _ = completion.Task.ContinueWith(
            _ =>
            {
                cancellation.Dispose();
                selector.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());

        selector.Show();
        selector.Activate();
        return completion.Task;
    }

    internal static Rectangle? Normalize(Point first, Point second, Rectangle virtualScreen)
    {
        if (virtualScreen.Width <= 0 || virtualScreen.Height <= 0)
        {
            return null;
        }

        var left = Math.Clamp(Math.Min(first.X, second.X), virtualScreen.Left, virtualScreen.Right);
        var top = Math.Clamp(Math.Min(first.Y, second.Y), virtualScreen.Top, virtualScreen.Bottom);
        var right = Math.Clamp(Math.Max(first.X, second.X), virtualScreen.Left, virtualScreen.Right);
        var bottom = Math.Clamp(Math.Max(first.Y, second.Y), virtualScreen.Top, virtualScreen.Bottom);
        var region = Rectangle.FromLTRB(left, top, right, bottom);
        return region.Width >= 8 && region.Height >= 8 ? region : null;
    }

    private sealed class SelectionForm : Form
    {
        private Point? _dragStart;
        private Point? _dragCurrent;

        public SelectionForm()
        {
            Text = "Select an area for OpenClicky";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            BackColor = Color.FromArgb(24, 32, 30);
            Opacity = 0.35;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
        }

        public event EventHandler<Rectangle?>? SelectionFinished;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = SystemInformation.VirtualScreen;
            Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Cancel();
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStart = PointToScreen(e.Location);
                _dragCurrent = _dragStart;
                Capture = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragStart is not null)
            {
                _dragCurrent = PointToScreen(e.Location);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _dragStart is { } start)
            {
                Capture = false;
                _dragCurrent = PointToScreen(e.Location);
                var region = Normalize(start, _dragCurrent.Value, SystemInformation.VirtualScreen);
                if (region is not null)
                {
                    Complete(region);
                    return;
                }

                _dragStart = null;
                _dragCurrent = null;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var titleFont = new Font("Segoe UI", 16f, FontStyle.Bold);
            using var hintFont = new Font("Segoe UI", 10f);
            using var textBrush = new SolidBrush(Color.White);
            e.Graphics.DrawString("Drag around the area you want to ask about", titleFont, textBrush, 28, 24);
            e.Graphics.DrawString("Press Esc to cancel", hintFont, textBrush, 30, 58);

            if (_dragStart is not { } start || _dragCurrent is not { } current)
            {
                return;
            }

            var region = Normalize(start, current, SystemInformation.VirtualScreen)
                ?? Rectangle.FromLTRB(
                    Math.Min(start.X, current.X), Math.Min(start.Y, current.Y),
                    Math.Max(start.X, current.X), Math.Max(start.Y, current.Y));
            var local = RectangleToClient(region);
            using var fill = new SolidBrush(Color.FromArgb(72, 126, 232, 169));
            using var outline = new Pen(Color.FromArgb(126, 232, 169), 3f);
            e.Graphics.FillRectangle(fill, local);
            e.Graphics.DrawRectangle(outline, local);
        }

        public void Cancel() => Complete(null);

        private void Complete(Rectangle? region)
        {
            if (IsDisposed)
            {
                return;
            }

            Capture = false;
            SelectionFinished?.Invoke(this, region);
            Close();
        }
    }
}
