using System.Drawing.Drawing2D;
using OpenClicky.Capture;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;
using OpenClicky.Core.Settings;

namespace OpenClicky.App;

/// <summary>
/// Draws the user's highlight strokes into the screenshots in memory (never by capturing our own trail window): the accent at
/// 55 % alpha, 8 monitor px wide scaled to the screenshot, with a 10 px ring where the pointer rested. Screens without a mark
/// are returned untouched.
/// </summary>
internal static class HighlightCompositor
{
    private const int Alpha = 140; // 55 %
    private const float StrokeWidth = 8, RingRadius = 10;

    public static IReadOnlyList<ScreenCaptureResult> Composite(IReadOnlyList<ScreenCaptureResult> screens, HighlightGesture g, string accentHex)
    {
        var colour = Color.FromArgb(Alpha, ColorTranslator.FromHtml(SettingsCatalog.SnapAccent(accentHex)));
        return screens.Select(s => Draw(s, g, colour)).ToList();
    }

    private static ScreenCaptureResult Draw(ScreenCaptureResult s, HighlightGesture g, Color colour)
    {
        if (!s.LooksLikeJpeg || s.MonitorWidth <= 0 || s.MonitorHeight <= 0 || s.PixelWidth <= 0 || s.PixelHeight <= 0) return s;
        var monitor = new Box(s.MonitorLeft, s.MonitorTop, s.MonitorWidth, s.MonitorHeight);
        var strokes = g.Strokes.Where(st => st.Points.Count >= 2 && Overlaps(st.Bounds.Inflate(StrokeWidth), monitor)).ToList();
        var dwells = g.Dwells.Where(monitor.Contains).ToList();
        if (strokes.Count == 0 && dwells.Count == 0) return s;

        var sx = s.PixelWidth / (float)s.MonitorWidth;
        var sy = s.PixelHeight / (float)s.MonitorHeight;
        PointF Map(Pt p) => new((p.X - s.MonitorLeft) * sx, (p.Y - s.MonitorTop) * sy);
        using var stream = new MemoryStream(s.JpegBytes);
        using var image = new Bitmap(stream);
        using (var graphics = Graphics.FromImage(image))
        using (var pen = new Pen(colour, StrokeWidth * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // Drawn as one polyline each, so a stroke's overlaps do not darken (the image is clipped to its own bounds).
            foreach (var stroke in strokes) graphics.DrawLines(pen, stroke.Points.Select(Map).ToArray());
            foreach (var dwell in dwells)
            {
                var c = Map(dwell);
                graphics.DrawEllipse(pen, c.X - RingRadius, c.Y - RingRadius, 2 * RingRadius, 2 * RingRadius);
            }
        }
        var (jpeg, width, height) = CaptureImageEncoder.BitmapToJpeg(image);
        return s with { JpegBytes = jpeg, PixelWidth = width, PixelHeight = height };
    }

    private static bool Overlaps(Box a, Box b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
