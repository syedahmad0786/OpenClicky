using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenClicky.Core;

namespace OpenClicky.Capture;

/// <summary>
/// GDI BitBlt fallback when Windows.Graphics.Capture / D3D11 is unavailable.
/// Captures stay in memory — nothing is written to disk.
/// </summary>
public sealed class GdiScreenCaptureService : IScreenCaptureService
{
    public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursor = Cursor.Position;
        var results = new List<ScreenCaptureResult>();
        var screens = Screen.AllScreens
            .OrderByDescending(s => s.Bounds.Contains(cursor))
            .ThenBy(s => s.Bounds.Left)
            .ToArray();

        for (var index = 0; index < screens.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var screen = screens[index];
            var bounds = screen.Bounds;
            var isCursor = bounds.Contains(cursor);

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            var (jpeg, pixelW, pixelH) = CaptureImageEncoder.BitmapToJpeg(bitmap, maxEdge: 1280);

            var label = screens.Length == 1
                ? "user's screen (cursor is here)"
                : isCursor
                    ? $"screen {index + 1} of {screens.Length} — cursor is on this screen (primary focus)"
                    : $"screen {index + 1} of {screens.Length} — secondary screen";

            results.Add(new ScreenCaptureResult(
                JpegBytes: jpeg,
                PixelWidth: pixelW,
                PixelHeight: pixelH,
                MonitorLeft: bounds.Left,
                MonitorTop: bounds.Top,
                MonitorWidth: bounds.Width,
                MonitorHeight: bounds.Height,
                IsCursorMonitor: isCursor,
                Label: label));
        }

        return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>(results);
    }
}
