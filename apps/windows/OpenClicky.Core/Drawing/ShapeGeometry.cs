using System.Globalization;

namespace OpenClicky.Core.Drawing;

/// <summary>
/// The strokes each drawing op is made of, in the op's own coordinates (virtual pixels for a mapped part). <paramref name="dip"/>
/// is the monitor scale for fixed DIP sizes (arrow heads, bracket caps, corners); <paramref name="scale"/> converts the op's
/// screenshot-pixel size keys (<c>size</c>, <c>r</c>) to the op's coordinates. Ops drawn another way (text, label, dot, point,
/// target and the C7/C8 helpers) have no strokes here.
/// </summary>
public static class ShapeGeometry
{
    private const float ArrowHeadDip = 12, ArrowHeadDegrees = 28, BracketCapDip = 24, BracketFraction = 0.25f, CornerDip = 6, UnderlineDropDip = 2;
    private const int EllipsePoints = 64, CurvePoints = 32, PathSegmentPoints = 16, SplinePoints = 16, ArcPoints = 6;

    public static IReadOnlyList<IReadOnlyList<Pt>> Outline(DrawOp op, float dip = 1, float scale = 1)
    {
        ArgumentNullException.ThrowIfNull(op);
        var p = op.Points;
        var b = op.Box;
        var size = SizeKey(op, "size", 24) * scale;
        switch (op.Kind)
        {
            case DrawOpKind.Line when p.Count >= 2: return [p];
            case DrawOpKind.Arrow when p.Count >= 2:
                return ArrowHead(p, ArrowHeadDip * dip) is { } head ? [p, head] : [p];
            case DrawOpKind.Curve when p.Count == 4: return [Cubic(p[0], p[1], p[2], p[3], CurvePoints, includeStart: true)];
            case DrawOpKind.Poly when p.Count >= 3:
                return [op.Args.GetValueOrDefault("closed") == "1" ? [.. p, p[0]] : p];
            case DrawOpKind.Path: return FlattenPath(op.Args.GetValueOrDefault("d") ?? "", p);
            case DrawOpKind.Circle or DrawOpKind.Ring: return [Ellipse(b)];
            case DrawOpKind.Rect:
                var radius = op.Args.ContainsKey("r") ? SizeKey(op, "r", 0) * scale : CornerDip * dip;
                return [RoundedRect(b, Math.Min(radius, Math.Min(b.W, b.H) / 2))];
            case DrawOpKind.Highlight: return [[new(b.X, b.Y + b.H / 2), new(b.Right, b.Y + b.H / 2)]];
            case DrawOpKind.Trace when p.Count >= 3: return [ClosedSpline(p)];
            case DrawOpKind.Frame: return Brackets(b, dip);
            // y is the text baseline for an underline (the ink sits just below it) and the text's mid-line for a strike.
            case DrawOpKind.Underline when p.Count == 2:
                return [[new(p[0].X, p[0].Y + UnderlineDropDip * dip), new(p[1].X, p[1].Y + UnderlineDropDip * dip)]];
            case DrawOpKind.Strike when p.Count == 2: return [[p[0], p[1]]];
            case DrawOpKind.Star when p.Count > 0: return [Star(p[0], size / 2)];
            case DrawOpKind.Check when p.Count > 0:
                var c = p[0];
                return [[new(c.X - size * 0.35f, c.Y), new(c.X - size * 0.1f, c.Y + size * 0.3f), new(c.X + size * 0.4f, c.Y - size * 0.35f)]];
            case DrawOpKind.Cross when p.Count > 0:
                var x = p[0];
                var h = size * 0.4f;
                return [[new(x.X - h, x.Y - h), new(x.X + h, x.Y + h)], [new(x.X + h, x.Y - h), new(x.X - h, x.Y + h)]];
            default: return [];
        }
    }

    /// <summary>Shapes whose outline encloses an area (and so gets the wash fill).</summary>
    public static bool IsClosed(DrawOp op) => op.Kind switch
    {
        DrawOpKind.Circle or DrawOpKind.Rect or DrawOpKind.Trace or DrawOpKind.Star => true,
        DrawOpKind.Poly => op.Args.GetValueOrDefault("closed") == "1",
        DrawOpKind.Path => op.Args.GetValueOrDefault("d") is { } d && d.Contains('Z'),
        _ => false,
    };

    private static float SizeKey(DrawOp op, string key, float fallback) =>
        op.Args.TryGetValue(key, out var raw) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>Two barbs at ±28° off the last non-zero segment, meeting at the tip: [left, tip, right].</summary>
    private static Pt[]? ArrowHead(IReadOnlyList<Pt> p, float length)
    {
        var tip = p[^1];
        for (var i = p.Count - 2; i >= 0; i--)
        {
            float dx = p[i].X - tip.X, dy = p[i].Y - tip.Y;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            if (!(d > 0)) continue;
            var back = MathF.Atan2(dy, dx);
            var spread = ArrowHeadDegrees * MathF.PI / 180;
            Pt Barb(float angle) => new(tip.X + MathF.Cos(angle) * length, tip.Y + MathF.Sin(angle) * length);
            return [Barb(back + spread), tip, Barb(back - spread)];
        }
        return null;
    }

    private static Pt[] Ellipse(Box b)
    {
        var points = new Pt[EllipsePoints];
        float cx = b.X + b.W / 2, cy = b.Y + b.H / 2;
        for (var i = 0; i < EllipsePoints - 1; i++)
        {
            var a = 2 * Math.PI * i / (EllipsePoints - 1);
            points[i] = new(cx + (float)(b.W / 2 * Math.Cos(a)), cy + (float)(b.H / 2 * Math.Sin(a)));
        }
        points[^1] = points[0];
        return points;
    }

    private static Pt[] RoundedRect(Box b, float r)
    {
        if (!(r > 0)) return [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom), new(b.X, b.Y)];
        var points = new List<Pt>();
        void Arc(float cx, float cy, double from)
        {
            for (var i = 0; i <= ArcPoints; i++)
            {
                var a = from + Math.PI / 2 * i / ArcPoints;
                points.Add(new(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
            }
        }
        Arc(b.Right - r, b.Y + r, -Math.PI / 2);
        Arc(b.Right - r, b.Bottom - r, 0);
        Arc(b.X + r, b.Bottom - r, Math.PI / 2);
        Arc(b.X + r, b.Y + r, Math.PI);
        points.Add(points[0]);
        return [.. points];
    }

    private static List<Pt> Cubic(Pt p0, Pt c1, Pt c2, Pt p1, int count, bool includeStart)
    {
        var points = new List<Pt>(count + 1);
        if (includeStart) points.Add(p0);
        var steps = includeStart ? count - 1 : count;
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1 - t;
            points.Add(new(u * u * u * p0.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p1.X,
                u * u * u * p0.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p1.Y));
        }
        return points;
    }

    private static IEnumerable<Pt> Quadratic(Pt p0, Pt c, Pt p1)
    {
        for (var i = 1; i <= PathSegmentPoints; i++)
        {
            var t = i / (float)PathSegmentPoints;
            var u = 1 - t;
            yield return new(u * u * p0.X + 2 * u * t * c.X + t * t * p1.X, u * u * p0.Y + 2 * u * t * c.Y + t * t * p1.Y);
        }
    }

    /// <summary>The parser's path: <paramref name="letters"/> (M/L/Q/C/Z) consume 1/1/2/3/0 of <paramref name="points"/> each.</summary>
    private static IReadOnlyList<IReadOnlyList<Pt>> FlattenPath(string letters, IReadOnlyList<Pt> points)
    {
        var strokes = new List<IReadOnlyList<Pt>>();
        List<Pt>? current = null;
        var next = 0;
        void Finish() { if (current is { Count: >= 2 }) strokes.Add(current); current = null; }
        foreach (var letter in letters)
        {
            var need = letter switch { 'M' or 'L' => 1, 'Q' => 2, 'C' => 3, _ => 0 };
            if (next + need > points.Count) break;
            if (letter == 'M') { Finish(); current = [points[next]]; }
            else if (current is null) break; // the parser guarantees a leading M
            else if (letter == 'L') current.Add(points[next]);
            else if (letter == 'Q') current.AddRange(Quadratic(current[^1], points[next], points[next + 1]));
            else if (letter == 'C') current.AddRange(Cubic(current[^1], points[next], points[next + 1], points[next + 2], PathSegmentPoints, includeStart: false));
            else if (letter == 'Z') current.Add(current[0]);
            next += need;
        }
        Finish();
        return strokes;
    }

    /// <summary>A closed Catmull-Rom spline through every point, 16 points per segment, ending on the first point.</summary>
    private static Pt[] ClosedSpline(IReadOnlyList<Pt> p)
    {
        var n = p.Count;
        var points = new List<Pt>(n * SplinePoints + 1);
        for (var i = 0; i < n; i++)
        {
            Pt p0 = p[(i - 1 + n) % n], p1 = p[i], p2 = p[(i + 1) % n], p3 = p[(i + 2) % n];
            for (var j = 0; j < SplinePoints; j++)
            {
                var t = j / (float)SplinePoints;
                float t2 = t * t, t3 = t2 * t;
                float At(float a, float b, float c, float d) =>
                    0.5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
                points.Add(new(At(p0.X, p1.X, p2.X, p3.X), At(p0.Y, p1.Y, p2.Y, p3.Y)));
            }
        }
        points.Add(points[0]);
        return [.. points];
    }

    /// <summary>Four corner brackets; each arm is 25 % of its side, at most 24 DIP.</summary>
    private static IReadOnlyList<IReadOnlyList<Pt>> Brackets(Box b, float dip)
    {
        var ax = Math.Min(b.W * BracketFraction, BracketCapDip * dip);
        var ay = Math.Min(b.H * BracketFraction, BracketCapDip * dip);
        return
        [
            [new(b.X, b.Y + ay), new(b.X, b.Y), new(b.X + ax, b.Y)],
            [new(b.Right - ax, b.Y), new(b.Right, b.Y), new(b.Right, b.Y + ay)],
            [new(b.Right, b.Bottom - ay), new(b.Right, b.Bottom), new(b.Right - ax, b.Bottom)],
            [new(b.X + ax, b.Bottom), new(b.X, b.Bottom), new(b.X, b.Bottom - ay)],
        ];
    }

    /// <summary>Five outer and five inner points, starting at the top, closed.</summary>
    private static Pt[] Star(Pt c, float outer)
    {
        var points = new Pt[11];
        for (var i = 0; i < 10; i++)
        {
            var a = -Math.PI / 2 + Math.PI * i / 5;
            var r = i % 2 == 0 ? outer : outer * 0.45f;
            points[i] = new(c.X + (float)(r * Math.Cos(a)), c.Y + (float)(r * Math.Sin(a)));
        }
        points[10] = points[0];
        return points;
    }
}
