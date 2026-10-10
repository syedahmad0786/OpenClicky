using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenClicky.Core;

namespace OpenClicky.Capture;

/// <summary>
/// Phase A capture via GDI BitBlt of each monitor (reliable fallback).
/// WGC-backed implementation can replace this in a follow-up without changing the orchestrator.
/// Captures stay in memory — nothing is written to disk.
/// </summary>
public sealed class GdiScreenCaptureService : IScreenCaptureService
{
    public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken cancellationToken = default)
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

            // Downscale long edge to ~1280 like open Clicky (keeps Phase B vision cheap).
            using var scaled = Downscale(bitmap, maxEdge: 1280);
            using var stream = new MemoryStream();
            scaled.Save(stream, ImageFormat.Jpeg);
            var jpeg = stream.ToArray();

            var label = screens.Length == 1
                ? "user's screen (cursor is here)"
                : isCursor
                    ? $"screen {index + 1} of {screens.Length} — cursor is on this screen (primary focus)"
                    : $"screen {index + 1} of {screens.Length} — secondary screen";

            results.Add(new ScreenCaptureResult(
                JpegBytes: jpeg,
                PixelWidth: scaled.Width,
                PixelHeight: scaled.Height,
                MonitorLeft: bounds.Left,
                MonitorTop: bounds.Top,
                MonitorWidth: bounds.Width,
                MonitorHeight: bounds.Height,
                IsCursorMonitor: isCursor,
                Label: label));
        }

        return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>(results);
    }

    private static Bitmap Downscale(Bitmap source, int maxEdge)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxEdge)
        {
            return new Bitmap(source);
        }

        var scale = maxEdge / (double)longEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return result;
    }
}
