using System.Drawing.Drawing2D;
using System.Drawing.Text;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Overlay;

/// <summary>
/// GDI+ drawing of the companion: the glowing triangle, listening bars, thinking dots and the two bubbles. Every size is
/// in DIP and multiplied by <c>s</c> (the scale of the monitor under the tip); coordinates are virtual-screen pixels.
/// UI thread only (fonts and the measuring surface are shared).
/// </summary>
internal static class CursorRenderer
{
    private static readonly float[] GlowWidths = [20, 16, 12, 8, 4];
    private static readonly int[] GlowAlphas = [5, 10, 18, 28, 40];
    private static readonly float[] BarWeights = [0.55f, 0.8f, 1f, 0.8f, 0.55f];
    public const int BarCount = 5;
    private const float BarWidth = 2, BarGap = 2, BarRadius = 1.5f, BarsX = 14, BarsY = -4, BarMaxHeight = 12;
    private const float DotRadius = 2.3f, DotOrbit = 8.2f;
    private const double OrbitSeconds = 2.16, DotBeatSeconds = 0.72;
    private const float FontDip = 11, LineDip = 15, BubbleRadius = 6;
    private const float MessageMaxWidth = 300, MessagePadX = 10, MessagePadY = 7, LabelPadX = 8, LabelPadY = 4;
    public const int MessageMaxLines = 6;

    public static readonly Color MessageFill = Color.FromArgb(0xE6, 0x1C, 0x1F, 0x26);
    public static readonly Color MessageText = Color.FromArgb(0xF2, 0xF4, 0xF7);

    private static readonly string FontFamilyName =
        FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    private static readonly Dictionary<float, Font> Fonts = [];
    private static readonly Graphics Measure = CreateMeasure();

    private static Graphics CreateMeasure()
    {
        var g = Graphics.FromImage(new Bitmap(1, 1));
        Prepare(g);
        return g;
    }
    private static readonly StringFormat LineFormat = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        // ClearType needs an opaque background; per-pixel alpha text must be greyscale.
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    public static PointF[] Triangle(PointF tip, float s) =>
        CursorShape.Triangle(new Pt(tip.X, tip.Y), s, CursorConstants.RestRotationDegrees).Select(p => new PointF(p.X, p.Y)).ToArray();

    public static PointF Centroid(PointF[] t) => new((t[0].X + t[1].X + t[2].X) / 3, (t[0].Y + t[1].Y + t[2].Y) / 3);

    /// <summary>The triangle plus its widest glow stroke.</summary>
    public static RectangleF CompanionBounds(PointF[] triangle, float s)
    {
        var box = RectangleF.FromLTRB(triangle.Min(p => p.X), triangle.Min(p => p.Y), triangle.Max(p => p.X), triangle.Max(p => p.Y));
        box.Inflate(CursorConstants.GlowMax / 2 * s + 1, CursorConstants.GlowMax / 2 * s + 1);
        return box;
    }

    public static void DrawCompanion(Graphics g, PointF[] triangle, float s, Color accent)
    {
        using var path = RoundedTriangle(triangle, CursorConstants.CornerFraction * CursorConstants.TriangleSide * s);
        for (var i = 0; i < GlowWidths.Length; i++)
        {
            using var glow = new Pen(Color.FromArgb(GlowAlphas[i], accent), GlowWidths[i] * s) { LineJoin = LineJoin.Round };
            g.DrawPath(glow, path);
        }
        using (var fill = new SolidBrush(accent)) g.FillPath(fill, path);
        using var rim = new Pen(Lerp(accent, Color.White, 0.35f), 1 * s) { LineJoin = LineJoin.Round };
        g.DrawPath(rim, path);
    }

    /// <summary>Target bar height in DIP for a microphone level (0..1).</summary>
    public static float BarTarget(int index, double level) => 3 + 9 * (float)Math.Clamp(double.IsFinite(level) ? level : 0, 0, 1) * BarWeights[index];

    public static RectangleF BarsBounds(PointF tip, float s) => new(
        tip.X + BarsX * s, tip.Y + (BarsY - BarMaxHeight / 2) * s, (BarCount * BarWidth + (BarCount - 1) * BarGap) * s, BarMaxHeight * s);

    public static void DrawBars(Graphics g, PointF tip, float s, float[] heightsDip, Color accent)
    {
        using var brush = new SolidBrush(accent);
        var centreY = tip.Y + BarsY * s;
        for (var i = 0; i < BarCount; i++)
        {
            var h = heightsDip[i] * s;
            FillRounded(g, brush, new RectangleF(tip.X + (BarsX + i * (BarWidth + BarGap)) * s, centreY - h / 2, BarWidth * s, h), BarRadius * s);
        }
    }

    public static RectangleF DotsBounds(PointF centroid, float s)
    {
        var r = (DotOrbit + DotRadius) * s + 1;
        return new(centroid.X - r, centroid.Y - r, 2 * r, 2 * r);
    }

    /// <summary>Three dots orbiting the centroid; each one's alpha peaks a beat after the previous. <paramref name="still"/> (reduced
    /// motion): no orbit and no pulse.</summary>
    public static void DrawDots(Graphics g, PointF centroid, float s, double seconds, bool still, Color accent)
    {
        var orbit = still ? 0 : seconds / OrbitSeconds * 2 * Math.PI;
        for (var i = 0; i < 3; i++)
        {
            var angle = orbit + i * 2 * Math.PI / 3 - Math.PI / 2;
            var pulse = still ? 1 : 0.5 + 0.5 * Math.Cos(2 * Math.PI * (seconds - i * DotBeatSeconds) / OrbitSeconds);
            using var brush = new SolidBrush(Color.FromArgb((int)(255 * (0.3 + 0.7 * pulse)), accent));
            var x = centroid.X + (float)Math.Cos(angle) * DotOrbit * s;
            var y = centroid.Y + (float)Math.Sin(angle) * DotOrbit * s;
            g.FillEllipse(brush, x - DotRadius * s, y - DotRadius * s, 2 * DotRadius * s, 2 * DotRadius * s);
        }
    }

    /// <summary>A wrapped message: at most six lines (the last ends with "…" when text was cut), sized in px for scale <c>s</c>.</summary>
    public sealed record MessageLayout(string Text, float Scale, string[][] Lines, SizeF Size)
    {
        public int WordCount => Lines.Sum(line => line.Length);
    }

    public static MessageLayout LayoutMessage(string text, float s)
    {
        var font = FontFor(s);
        var maxText = (MessageMaxWidth - 2 * MessagePadX) * s;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<List<string>>();
        var current = new List<string>();
        var index = 0;
        for (; index < words.Length; index++)
        {
            if (current.Count > 0 && Width(string.Join(' ', current.Append(words[index])), font) > maxText)
            {
                lines.Add(current);
                current = [];
                if (lines.Count == MessageMaxLines) break;
            }
            current.Add(words[index]);
        }
        if (current.Count > 0 && lines.Count < MessageMaxLines) lines.Add(current);
        if (index < words.Length && lines.Count > 0)
        {
            var last = lines[^1];
            while (last.Count > 1 && Width(string.Join(' ', last) + "…", font) > maxText) last.RemoveAt(last.Count - 1);
            last[^1] += "…";
        }
        var width = lines.Count == 0 ? 0 : Math.Min(maxText, lines.Max(line => Width(string.Join(' ', line), font)));
        return new(text, s, lines.Select(line => line.ToArray()).ToArray(),
            new SizeF(width + 2 * MessagePadX * s, lines.Count * LineDip * s + 2 * MessagePadY * s));
    }

    /// <summary>The bubble at <paramref name="box"/> showing the first <paramref name="revealedWords"/> words, faded by <paramref name="alpha"/>.</summary>
    public static void DrawMessage(Graphics g, RectangleF box, MessageLayout layout, int revealedWords, float alpha)
    {
        var s = layout.Scale;
        using var fill = new SolidBrush(Color.FromArgb((int)(MessageFill.A * alpha), MessageFill));
        FillRounded(g, fill, box, BubbleRadius * s);
        using var text = new SolidBrush(Color.FromArgb((int)(255 * alpha), MessageText));
        var font = FontFor(s);
        var y = box.Y + MessagePadY * s;
        foreach (var line in layout.Lines)
        {
            if (revealedWords <= 0) break;
            var shown = string.Join(' ', line.Take(revealedWords));
            revealedWords -= line.Length;
            g.DrawString(shown, font, text, new RectangleF(box.X + MessagePadX * s, y, box.Width - 2 * MessagePadX * s, LineDip * s), LineFormat);
            y += LineDip * s;
        }
    }

    public static SizeF LabelSize(string label, float s) =>
        new(Math.Min(MessageMaxWidth * s, Width(label, FontFor(s)) + 2 * LabelPadX * s), LineDip * s + 2 * LabelPadY * s);

    public static void DrawLabel(Graphics g, RectangleF box, string label, float s, Color accent)
    {
        using var fill = new SolidBrush(accent);
        FillRounded(g, fill, box, BubbleRadius * s);
        g.DrawString(label, FontFor(s), Brushes.White,
            new RectangleF(box.X + LabelPadX * s, box.Y + LabelPadY * s, box.Width - 2 * LabelPadX * s, LineDip * s), LineFormat);
    }

    public static void FillRounded(Graphics g, Brush brush, RectangleF rect, float radius)
    {
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        if (radius <= 0) { g.FillRectangle(brush, rect); return; }
        using var path = RoundedRect(rect, radius);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>How far past the ring's box the click ring paints (pulse inflate, widest glow stroke), per side, in DIP.</summary>
    public const float RingPulseDip = ClickRingLayout.PulseInflateDip, RingLineDip = 2.5f;
    public const float RingPaintMarginDip = RingPulseDip + (RingLineDip + 12) / 2 + 1;
    private static readonly float[] RingGlowExtraDip = [4, 8, 12]; // added to the line width
    private static readonly int[] RingGlowAlphas = [8, 18, 30]; // widest stroke is the faintest
    private const int RingFillAlpha = 20; // 8 %

    /// <summary>
    /// The click ring: the accent at 8 % inside, a three-stroke glow, then a dashed outline (6 DIP dashes, 4 DIP gaps, 2.5 DIP wide).
    /// <paramref name="alpha"/> (0..1) fades all of it.
    /// </summary>
    public static void DrawClickRing(Graphics g, RectangleF ring, float corner, float s, Color accent, float alpha)
    {
        corner = Math.Min(corner, Math.Min(ring.Width, ring.Height) / 2);
        using var path = RoundedRect(ring, corner);
        Color Tint(int a) => Color.FromArgb((int)Math.Round(a * Math.Clamp(alpha, 0, 1)), accent);
        using (var fill = new SolidBrush(Tint(RingFillAlpha))) g.FillPath(fill, path);
        for (var i = 0; i < RingGlowExtraDip.Length; i++)
        {
            using var glow = new Pen(Tint(RingGlowAlphas[i]), (RingLineDip + RingGlowExtraDip[^(i + 1)]) * s) { LineJoin = LineJoin.Round };
            g.DrawPath(glow, path);
        }
        using var dashed = new Pen(Tint(255), RingLineDip * s)
        {
            LineJoin = LineJoin.Round,
            DashStyle = DashStyle.Custom,
            DashPattern = [6 / RingLineDip, 4 / RingLineDip], // in pen widths
        };
        g.DrawPath(dashed, path);
    }

    public static Color Lerp(Color a, Color b, float t) => Color.FromArgb(a.A,
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>The triangle with each corner replaced by a true arc of <paramref name="radius"/> tangent to both sides.</summary>
    private static GraphicsPath RoundedTriangle(PointF[] v, float radius)
    {
        var path = new GraphicsPath();
        for (var i = 0; i < 3; i++)
        {
            var corner = v[i];
            var toPrev = Unit(v[(i + 2) % 3], corner);
            var toNext = Unit(v[(i + 1) % 3], corner);
            var half = Math.Acos(Math.Clamp(toPrev.X * toNext.X + toPrev.Y * toNext.Y, -1, 1)) / 2;
            var along = (float)(radius / Math.Tan(half));
            var bisector = Unit(new PointF(toPrev.X + toNext.X, toPrev.Y + toNext.Y), PointF.Empty);
            var centreDistance = (float)(radius / Math.Sin(half));
            var centre = new PointF(corner.X + bisector.X * centreDistance, corner.Y + bisector.Y * centreDistance);
            var start = new PointF(corner.X + toPrev.X * along, corner.Y + toPrev.Y * along);
            var end = new PointF(corner.X + toNext.X * along, corner.Y + toNext.Y * along);
            var startAngle = Degrees(start, centre);
            var sweep = Degrees(end, centre) - startAngle;
            if (sweep > 180) sweep -= 360;
            else if (sweep <= -180) sweep += 360;
            path.AddArc(centre.X - radius, centre.Y - radius, 2 * radius, 2 * radius, startAngle, sweep);
        }
        path.CloseFigure();
        return path;
    }

    private static PointF Unit(PointF to, PointF from)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        return length > 0 ? new PointF(dx / length, dy / length) : PointF.Empty;
    }

    private static float Degrees(PointF p, PointF centre) => MathF.Atan2(p.Y - centre.Y, p.X - centre.X) * 180 / MathF.PI;

    private static float Width(string text, Font font) => Measure.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;

    private static Font FontFor(float s)
    {
        if (!Fonts.TryGetValue(s, out var font)) Fonts[s] = font = new Font(FontFamilyName, FontDip * s, GraphicsUnit.Pixel);
        return font;
    }
}
