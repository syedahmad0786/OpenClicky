using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenClicky.Core.Drawing;

/// <summary>
/// The shapes behind the screen helpers (graph, timeline, scroll, tie) and the small rules of the paper ones (stocks, tilt).
/// Coordinates are the op's own (virtual pixels for a mapped part); sizes given in DIP take the monitor's scale as <c>dip</c>.
/// </summary>
public static class HelperGeometry
{
    public const int TickCount = 5;
    public const float ScrollChevronDip = 16, ScrollGapDip = 10, TieBulge = 0.2f;
    public const string Ink = "#1F2430";

    private const float GraphMargin = 0.1f, ChevronHeightFraction = 0.5f, TickMarkDip = 4;
    private const int TieSamples = 24;

    private static readonly Dictionary<string, string> Stocks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cream"] = "#FBF6EA", ["butter"] = "#FFF1B8", ["sky"] = "#DCEBFF", ["mint"] = "#DCF4E6", ["blush"] = "#FBE0E3", ["kraft"] = "#E6D2AE",
    };

    /// <summary>
    /// The axes (an L: top of the y axis, the origin, the end of the x axis), one polyline per series and the ticks. The plot is the
    /// box less a 10 % margin on every side; every series shares one scale, spaced evenly by index, and a flat set of values
    /// is a line at mid-height. The ticks are five on the y axis (smallest value first) then five on the x axis (left to right).
    /// </summary>
    public static (IReadOnlyList<Pt> Axes, IReadOnlyList<IReadOnlyList<Pt>> Series, IReadOnlyList<(Pt At, string Text)> Ticks) Graph(DrawOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        var b = op.Box;
        float left = b.X + b.W * GraphMargin, right = b.Right - b.W * GraphMargin;
        float top = b.Y + b.H * GraphMargin, bottom = b.Bottom - b.H * GraphMargin;
        Pt[] axes = [new(left, top), new(left, bottom), new(right, bottom)];

        var values = (op.Args.GetValueOrDefault("series") ?? "").Split(';')
            .Select(line => line.Split(',').Select(v => int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
                .Where(n => n is not null).Select(n => (float)n!.Value).ToArray())
            .Where(line => line.Length > 0).ToArray();
        var all = values.SelectMany(v => v).ToArray();
        float min = all.Length > 0 ? all.Min() : 0, max = all.Length > 0 ? all.Max() : 0;
        var flat = max <= min;
        if (flat) { min -= 1; max += 1; } // the line sits mid-height and the ticks read around the value
        var count = values.Length > 0 ? values.Max(v => v.Length) : 0;

        float X(int i) => count > 1 ? left + (right - left) * i / (count - 1) : (left + right) / 2;
        float Y(float v) => bottom - (bottom - top) * (v - min) / (max - min);
        var series = values.Select(line => (IReadOnlyList<Pt>)line.Select((v, i) => new Pt(X(i), Y(v))).ToArray()).ToArray();

        var ticks = new List<(Pt, string)>(2 * TickCount);
        for (var k = 0; k < TickCount; k++)
        {
            var value = min + (max - min) * k / (TickCount - 1);
            ticks.Add((new Pt(left, Y(value)), value.ToString("0.##", CultureInfo.InvariantCulture)));
        }
        for (var k = 0; k < TickCount; k++)
        {
            var index = count > 1 ? (int)MathF.Round((count - 1) * k / (float)(TickCount - 1), MidpointRounding.AwayFromZero) + 1 : 1;
            ticks.Add((new Pt(left + (right - left) * k / (TickCount - 1), bottom), index.ToString(CultureInfo.InvariantCulture)));
        }
        return (axes, series, ticks);
    }

    /// <summary>The axis along the op's width and one tick per item, evenly spaced with a half step of room at each end.</summary>
    public static (IReadOnlyList<Pt> Axis, IReadOnlyList<(Pt Tick, string Label)> Items) Timeline(DrawOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        var b = op.Box;
        var labels = (op.Args.GetValueOrDefault("items") ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var items = labels.Select((label, i) => (new Pt(b.X + b.W * (i + 0.5f) / labels.Length, b.Y), label)).ToArray();
        return ([new Pt(b.X, b.Y), new Pt(b.Right, b.Y)], items);
    }

    /// <summary>Three chevrons 16 DIP wide and 10 DIP apart, centred on <paramref name="at"/>, pointing down or up.</summary>
    public static IReadOnlyList<IReadOnlyList<Pt>> Scroll(Pt at, bool down, float dip = 1)
    {
        float half = ScrollChevronDip * dip / 2, rise = ScrollChevronDip * dip * ChevronHeightFraction * (down ? 1 : -1);
        return Enumerable.Range(-1, 3).Select(i =>
        {
            var y = at.Y + i * ScrollGapDip * dip;
            return (IReadOnlyList<Pt>)[new Pt(at.X - half, y - rise / 2), new Pt(at.X, y + rise / 2), new Pt(at.X + half, y - rise / 2)];
        }).ToArray();
    }

    /// <summary>A quadratic arc from <paramref name="from"/> to <paramref name="to"/> that bulges 20 % of its length to the left of the way it runs (up, for left to right).</summary>
    public static IReadOnlyList<Pt> Tie(Pt from, Pt to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 0)) return [from, to];
        // A quadratic's middle sits half way to its control point, so the control is pushed out twice as far as the bulge.
        var push = 2 * TieBulge * length;
        var control = new Pt((from.X + to.X) / 2 + dy / length * push, (from.Y + to.Y) / 2 - dx / length * push);
        var arc = new Pt[TieSamples + 1];
        for (var i = 0; i <= TieSamples; i++)
        {
            float t = i / (float)TieSamples, u = 1 - t;
            arc[i] = new(u * u * from.X + 2 * u * t * control.X + t * t * to.X, u * u * from.Y + 2 * u * t * control.Y + t * t * to.Y);
        }
        arc[0] = from;
        arc[^1] = to;
        return arc;
    }

    /// <summary>The strokes the ink style draws for these ops (<see cref="ShapeGeometry.Outline"/> routes here); empty for any other op.</summary>
    public static IReadOnlyList<IReadOnlyList<Pt>> Outline(DrawOp op, float dip = 1)
    {
        var mark = TickMarkDip * dip;
        switch (op.Kind)
        {
            case DrawOpKind.Graph:
            {
                var (axes, series, ticks) = Graph(op);
                var strokes = new List<IReadOnlyList<Pt>> { axes };
                strokes.AddRange(series.Where(s => s.Count >= 2));
                strokes.AddRange(ticks.Select((t, i) => (IReadOnlyList<Pt>)(i < TickCount
                    ? [new Pt(t.At.X - mark, t.At.Y), t.At]
                    : [t.At, new Pt(t.At.X, t.At.Y + mark)])));
                return strokes;
            }
            case DrawOpKind.Timeline:
            {
                var (axis, items) = Timeline(op);
                if (items.Count == 0) return [];
                var strokes = new List<IReadOnlyList<Pt>> { axis };
                strokes.AddRange(items.Select(i => (IReadOnlyList<Pt>)[new Pt(i.Tick.X, i.Tick.Y - mark), new Pt(i.Tick.X, i.Tick.Y + mark)]));
                return strokes;
            }
            case DrawOpKind.Scroll when op.Points.Count > 0:
                return Scroll(op.Points[0], op.Args.GetValueOrDefault("dir") != "up", dip);
            case DrawOpKind.Tie when op.Points.Count == 2:
                return [Tie(op.Points[0], op.Points[1])];
            default:
                return [];
        }
    }

    /// <summary>A paper stock's colour (<c>#RRGGBB</c>): cream for cards and butter for stickies unless named; unknown names take the default.</summary>
    public static string StockColour(string? stock, bool sticky) =>
        stock is not null && Stocks.TryGetValue(stock, out var colour) ? colour : Stocks[sticky ? "butter" : "cream"];

    /// <summary>The same small tilt (-2 to 2 degrees) for the same part id, so a re-layout never makes a note wobble.</summary>
    public static float TiltDegrees(string id) => (uint)InkStyle.Seed(id) % 4001 / 1000f - 2f;
}

/// <summary>What a click on a drawn button asks the talk pipeline.</summary>
public static class DrawingButtons
{
    private static readonly JsonSerializerOptions Quote = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The request for a click: the button's label and the model's own prompt text, both quoted as data. The prompt can ask for
    /// whatever any talk request can, and nothing more: it goes through the normal pipeline and its access rules. The wording
    /// never starts with a phrase that would route the turn to agent mode or a walkthrough.
    /// </summary>
    public static string Request(string label, string prompt) =>
        $"I clicked the button {JsonSerializer.Serialize(label, Quote)} on your drawing, which asks: {JsonSerializer.Serialize(prompt, Quote)}";
}
