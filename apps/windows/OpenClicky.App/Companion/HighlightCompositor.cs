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
        // The describer's rule: a stroke belongs to the sent screen holding its centre, a rest to the one holding it.
        return screens.Select((s, i) => Draw(s,
            g.Strokes.Where(st => st.Points.Count >= 2 && HighlightDescriber.ScreenOf(st.Bounds, screens) == i).ToList(),
            g.Dwells.Where(d => HighlightDescriber.ScreenOf(d, screens) == i).ToList(), colour)).ToList();
    }

    private static ScreenCaptureResult Draw(ScreenCaptureResult s, List<HighlightStroke> strokes, List<Pt> dwells, Color colour)
    {
        if (strokes.Count == 0 && dwells.Count == 0 || !s.LooksLikeJpeg) return s;

        var sx = s.PixelWidth / (float)s.MonitorWidth;
        var sy = s.PixelHeight / (float)s.MonitorHeight;
        PointF Map(Pt p) => new((p.X - s.MonitorLeft) * sx, (p.Y - s.MonitorTop) * sy);
        using var stream = new MemoryStream(s.JpegBytes);
        using var image = new Bitmap(stream);
        using (var graphics = Graphics.FromImage(image))
        using (var pen = new Pen(colour, StrokeWidth * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // One polyline each, so a stroke's overlaps do not darken; a part on another display is clipped off this image.
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
}
