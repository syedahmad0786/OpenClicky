namespace OpenClicky.Core.Drawing;

/// <summary>
/// The hand-drawn look: a gentle wobble that is the same every time for the same part, a pen that tapers at both ends, the
/// highlighter band and the soft wash inside closed shapes. Sizes are DIP; the renderer multiplies them by the monitor scale.
/// </summary>
public static class InkStyle
{
    public const float HighlighterWidth = 14, HighlighterAlpha = 0.35f, WashAlpha = 0.18f;
    private const float SampleSpacing = 4, TaperFraction = 0.12f, TaperMinimum = 0.4f;

    /// <summary>
    /// Resamples <paramref name="polyline"/> every 4 px and nudges each sample along the segment's normal by a smooth wave of at
    /// most <paramref name="amplitude"/> px. Deterministic per <paramref name="seed"/>; the last vertex is always kept.
    /// </summary>
    public static IReadOnlyList<Pt> Wobble(IReadOnlyList<Pt> polyline, int seed, float amplitude = 1.2f, float wavelength = 28f)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        var random = new Random(seed);
        double phase1 = random.NextDouble() * 2 * Math.PI, phase2 = random.NextDouble() * 2 * Math.PI;
        var w = wavelength > 0 ? wavelength : 28;
        // Two incommensurate waves weighted 0.6 + 0.4, so the offset never exceeds the amplitude.
        float Offset(float s) => amplitude * (float)(0.6 * Math.Sin(2 * Math.PI * s / w + phase1) + 0.4 * Math.Sin(2 * Math.PI * s / (w * 0.53) + phase2));

        var result = new List<Pt>();
        float travelled = 0, next = 0, nx = 0, ny = 0;
        for (var i = 1; i < polyline.Count; i++)
        {
            Pt a = polyline[i - 1], b = polyline[i];
            var length = Distance(a, b);
            if (!(length > 0)) continue;
            nx = -(b.Y - a.Y) / length;
            ny = (b.X - a.X) / length;
            for (; next <= travelled + length; next += SampleSpacing)
            {
                var u = (next - travelled) / length;
                var o = Offset(next);
                result.Add(new(a.X + (b.X - a.X) * u + nx * o, a.Y + (b.Y - a.Y) * u + ny * o));
            }
            travelled += length;
        }
        if (result.Count == 0) return polyline.ToArray();
        if (next - SampleSpacing < travelled)
        {
            var end = polyline[^1];
            var o = Offset(travelled);
            result.Add(new(end.X + nx * o, end.Y + ny * o));
        }
        return result;
    }

    /// <summary>Pen width at <paramref name="t"/> (0..1 along the stroke): tapers to 40 % over the first and last 12 %.</summary>
    public static float PenWidth(float t, float baseWidth = 3.2f)
    {
        var along = Math.Clamp(float.IsFinite(t) ? t : 0.5f, 0, 1);
        var edge = Math.Min(along, 1 - along);
        return edge >= TaperFraction ? baseWidth : baseWidth * (TaperMinimum + (1 - TaperMinimum) * edge / TaperFraction);
    }

    /// <summary>A stable seed for a part id (string.GetHashCode changes every run): FNV-1a.</summary>
    public static int Seed(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var hash = 2166136261;
        foreach (var c in id) hash = (hash ^ c) * 16777619;
        return unchecked((int)hash);
    }

    /// <summary>
    /// The first <paramref name="progress"/> (0..1) of the strokes' total length, drawn in order, and where the pen is
    /// (null when nothing is drawn yet).
    /// </summary>
    public static (IReadOnlyList<IReadOnlyList<Pt>> Visible, Pt? Tip) Reveal(IReadOnlyList<IReadOnlyList<Pt>> strokes, float progress)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        Pt? last = strokes.LastOrDefault(s => s.Count > 0) is { } end ? end[^1] : null;
        if (progress >= 1) return (strokes, last);
        if (!(progress > 0)) return ([], null);
        var total = strokes.Sum(Length);
        var remaining = total * progress;
        var visible = new List<IReadOnlyList<Pt>>();
        Pt? tip = null;
        foreach (var stroke in strokes)
        {
            if (stroke.Count == 0 || remaining <= 0) break;
            var shown = new List<Pt> { stroke[0] };
            tip = stroke[0];
            for (var i = 1; i < stroke.Count && remaining > 0; i++)
            {
                Pt a = stroke[i - 1], b = stroke[i];
                var length = Distance(a, b);
                var portion = length <= 0 ? 1 : Math.Min(1, remaining / length);
                tip = new Pt(a.X + (b.X - a.X) * portion, a.Y + (b.Y - a.Y) * portion);
                shown.Add(tip.Value);
                remaining -= length;
            }
            visible.Add(shown);
        }
        return (visible, tip);
    }

    public static float Length(IReadOnlyList<Pt> stroke)
    {
        var length = 0f;
        for (var i = 1; i < stroke.Count; i++) length += Distance(stroke[i - 1], stroke[i]);
        return length;
    }

    private static float Distance(Pt a, Pt b) => MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
