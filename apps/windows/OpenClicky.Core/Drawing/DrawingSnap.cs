using System.Globalization;

namespace OpenClicky.Core.Drawing;

/// <summary>A line or word the OCR read on screenshot <see cref="Screen"/> (1-based), in that screenshot's pixels.</summary>
public sealed record ScreenText(int Screen, string Text, Box Bounds);

/// <summary>
/// Fits marks round the words they name. The model guesses pixel positions from a downscaled screenshot and lands tens of
/// pixels off; when a circle, ring, box, highlight, frame, target, trace, underline or strike has a label (or a POINT has
/// one) that matches text read near the guess, the shape is rebuilt round that text. Everything else passes unchanged.
/// </summary>
public static class DrawingSnap
{
    /// <summary>How far (as a share of the screenshot diagonal) from the model's guess the matching text may be.</summary>
    public const float MaxReach = 0.35f;
    private const double MinSimilarity = 0.72;

    /// <summary>Labelled marks fitted to <paramref name="text"/>; 3D figures fitted onto their screen (<see cref="FigureStage.Fit"/>).</summary>
    public static IReadOnlyList<DrawOp> Apply(IReadOnlyList<DrawOp> ops, IReadOnlyList<ScreenText> text, IReadOnlyList<ScreenCaptureResult> screens)
    {
        if (screens.Count == 0) return ops;
        return ops.Select(op => op.Kind == DrawOpKind.Figure ? FigureStage.Fit(op, screens) : text.Count == 0 ? op : Snap(op, text, screens)).ToArray();
    }

    /// <summary>The centre of the text a POINT label names near (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
    public static Pt? PointAt(string? label, float x, float y, int screen, IReadOnlyList<ScreenText> text, ScreenCaptureResult shot) =>
        Find(label, new Pt(x, y), screen, text, shot) is { } box ? box.Centre : null;

    private static DrawOp Snap(DrawOp op, IReadOnlyList<ScreenText> text, IReadOnlyList<ScreenCaptureResult> screens)
    {
        if (op.IsEdit || op.Kind is not (DrawOpKind.Circle or DrawOpKind.Ring or DrawOpKind.Rect or DrawOpKind.Highlight
            or DrawOpKind.Frame or DrawOpKind.Target or DrawOpKind.Trace or DrawOpKind.Underline or DrawOpKind.Strike)) return op;
        var screen = op.Screen ?? CursorScreen(screens);
        if (screen < 1 || screen > screens.Count) return op;
        if (Find(op.Text, op.Box.Centre, screen, text, screens[screen - 1]) is not { } t) return op;

        var pad = Math.Max(3f, t.H * 0.3f);
        Box box;
        Pt[] points;
        switch (op.Kind)
        {
            case DrawOpKind.Circle or DrawOpKind.Ring:
                // An ellipse through the corners of the padded text box would be √2 too big; this one hugs the words.
                box = new(t.X - pad - t.W * 0.08f, t.Y - pad - t.H * 0.25f, t.W + 2 * pad + t.W * 0.16f, t.H + 2 * pad + t.H * 0.5f);
                points = op.Points.Count == 1 ? [box.Centre] : [];
                break;
            case DrawOpKind.Trace:
                box = t.Inflate(pad);
                points = Loop(box);
                break;
            case DrawOpKind.Underline:
                points = [new(t.X, t.Bottom + 2), new(t.Right, t.Bottom + 2)];
                box = new(t.X, t.Bottom + 2, t.W, 0);
                break;
            case DrawOpKind.Strike:
                points = [new(t.X, t.Centre.Y), new(t.Right, t.Centre.Y)];
                box = new(t.X, t.Centre.Y, t.W, 0);
                break;
            default:
                box = t.Inflate(pad);
                points = [];
                break;
        }
        var shot = screens[screen - 1];
        box = Clip(box, shot);
        points = op.Kind is DrawOpKind.Circle or DrawOpKind.Ring && points.Length == 1 ? [box.Centre]
            : points.Select(p => new Pt(Math.Clamp(p.X, 0, shot.PixelWidth - 1), Math.Clamp(p.Y, 0, shot.PixelHeight - 1))).ToArray();
        return op with { Box = box, Points = points, Args = WithGeometry(op, box, points) };
    }

    /// <summary>The best text box for <paramref name="label"/>: most similar first, then nearest to <paramref name="guess"/>, within reach.</summary>
    private static Box? Find(string? label, Pt guess, int screen, IReadOnlyList<ScreenText> text, ScreenCaptureResult shot)
    {
        var wanted = Normalize(label);
        if (wanted.Length < 2) return null;
        var reach = MaxReach * MathF.Sqrt(shot.PixelWidth * (float)shot.PixelWidth + shot.PixelHeight * (float)shot.PixelHeight);
        Box? best = null;
        var bestScore = 0.0;
        var bestDistance = float.MaxValue;
        foreach (var span in Candidates(text.Where(t => t.Screen == screen).ToList(), wanted.Split(' ').Length))
        {
            var score = Similarity(wanted, Normalize(span.Text));
            if (score < MinSimilarity) continue;
            var c = span.Bounds.Centre;
            var distance = MathF.Sqrt((c.X - guess.X) * (c.X - guess.X) + (c.Y - guess.Y) * (c.Y - guess.Y));
            if (distance > reach) continue;
            if (score > bestScore + 0.05 || (Math.Abs(score - bestScore) <= 0.05 && distance < bestDistance))
            { best = span.Bounds; bestScore = Math.Max(score, bestScore); bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Each OCR line, plus runs of 1 to n+1 consecutive words on one line (a label often names part of a line).</summary>
    private static IEnumerable<ScreenText> Candidates(List<ScreenText> spans, int words)
    {
        foreach (var span in spans)
        {
            yield return span;
            var tokens = span.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;
            // Only the line box is known here: word boxes are estimated by character share, close enough to fit a mark.
            var total = (float)span.Text.Length;
            var starts = new List<int>();
            var at = 0;
            foreach (var token in tokens) { at = span.Text.IndexOf(token, at, StringComparison.Ordinal); starts.Add(at); at += token.Length; }
            for (var n = 1; n <= Math.Min(words + 1, tokens.Length - 1); n++)
                for (var i = 0; i + n <= tokens.Length; i++)
                {
                    var first = starts[i];
                    var last = starts[i + n - 1] + tokens[i + n - 1].Length;
                    var x = span.Bounds.X + span.Bounds.W * first / total;
                    var w = span.Bounds.W * (last - first) / total;
                    yield return new(span.Screen, string.Join(' ', tokens, i, n), new(x, span.Bounds.Y, w, span.Bounds.H));
                }
        }
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var chars = s.Normalize(System.Text.NormalizationForm.FormKD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.ToLowerInvariant(c) switch
            {
                '0' => 'o',
                '1' or 'l' or '|' => 'i', // OCR's usual look-alikes, folded the same way on both sides
                var x when char.IsLetterOrDigit(x) => x,
                _ => ' ',
            });
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Dice coefficient over character bigrams (1 for equal strings), so OCR slips like "0IL" for "OIL" still match.</summary>
    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        if (a.Length < 2 || b.Length < 2) return 0;
        var grams = new Dictionary<(char, char), int>();
        for (var i = 0; i + 1 < a.Length; i++) grams[(a[i], a[i + 1])] = grams.GetValueOrDefault((a[i], a[i + 1])) + 1;
        var shared = 0;
        for (var i = 0; i + 1 < b.Length; i++)
            if (grams.TryGetValue((b[i], b[i + 1]), out var n) && n > 0) { shared++; grams[(b[i], b[i + 1])] = n - 1; }
        return 2.0 * shared / (a.Length - 1 + b.Length - 1);
    }

    private static Pt[] Loop(Box b)
    {
        var c = b.Centre;
        return Enumerable.Range(0, 10).Select(i => i * Math.PI * 2 / 10)
            .Select(a => new Pt(c.X + b.W / 2 * (float)Math.Cos(a), c.Y + b.H / 2 * (float)Math.Sin(a))).ToArray();
    }

    private static Box Clip(Box b, ScreenCaptureResult s)
    {
        var x = Math.Clamp(b.X, 0, s.PixelWidth - 1);
        var y = Math.Clamp(b.Y, 0, s.PixelHeight - 1);
        return new(x, y, Math.Max(0, Math.Min(b.Right, s.PixelWidth - 1) - x), Math.Max(0, Math.Min(b.Bottom, s.PixelHeight - 1) - y));
    }

    /// <summary>The op's geometry keys rewritten to the snapped shape, so a saved drawing replays what was shown.</summary>
    private static IReadOnlyDictionary<string, string> WithGeometry(DrawOp op, Box box, Pt[] points)
    {
        var args = new Dictionary<string, string>(op.Args, StringComparer.Ordinal);
        static string I(float v) => ((int)MathF.Round(v)).ToString(CultureInfo.InvariantCulture);
        if (op.Kind == DrawOpKind.Trace) { args["pts"] = string.Join(',', points.SelectMany(p => new[] { I(p.X), I(p.Y) })); return args; }
        if (op.Kind is DrawOpKind.Underline or DrawOpKind.Strike) { args["x"] = I(points[0].X); args["y"] = I(points[0].Y); args["w"] = I(Math.Max(1, box.W)); return args; }
        if (args.ContainsKey("r") && op.Kind is DrawOpKind.Circle or DrawOpKind.Ring)
        {
            // A centred circle keeps its x y r form: r covers the wider half-axis.
            args["x"] = I(box.Centre.X); args["y"] = I(box.Centre.Y); args["r"] = I(Math.Max(1, Math.Max(box.W, box.H) / 2));
            return args;
        }
        args["x"] = I(box.X); args["y"] = I(box.Y); args["w"] = I(Math.Max(1, box.W)); args["h"] = I(Math.Max(1, box.H));
        return args;
    }

    private static int CursorScreen(IReadOnlyList<ScreenCaptureResult> screens)
    {
        for (var i = 0; i < screens.Count; i++) if (screens[i].IsCursorMonitor) return i + 1;
        return 1;
    }
}
