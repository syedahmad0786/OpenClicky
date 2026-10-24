using System.Drawing.Drawing2D;
using System.Globalization;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Overlay;

/// <summary>
/// Paints drawing parts in the hand-drawn ink style: wobbling, tapering pen strokes revealed by progress, highlighter bands,
/// washes inside closed shapes, handwritten text with a white halo, label pills, and the click ring for point and target.
/// Coordinates are virtual-screen pixels; sizes are DIP times the scale of the monitor under each part. Public and static so
/// replay and print can reuse it. UI thread only (fonts and the measuring surface are shared).
/// </summary>
public static class DrawingRenderer
{
    private const float MarkerDip = 6, DotDip = 5, TextDip = 14, TextMaxWidthDip = 360, HaloDip = 3, PillGapDip = 6;
    private const int HaloAlpha = 179; // 70 %
    private static readonly string HandFamily = Installed("Segoe Print") ? "Segoe Print" : "Segoe UI";
    private static readonly Graphics Measure = Graphics.FromImage(new Bitmap(1, 1));
    private static readonly StringFormat TextFormat = new(StringFormat.GenericTypographic) { Trimming = StringTrimming.None };

    /// <summary>One part laid out for painting: wobbled strokes, text box, label pill or ring, and everything it can paint.</summary>
    public sealed class PartLayout
    {
        internal PartLayout(DrawPart part, float dip) { Part = part; Dip = dip; }
        public DrawPart Part { get; }
        public float Dip { get; }
        public IReadOnlyList<IReadOnlyList<Pt>> Strokes { get; internal set; } = [];
        public RectangleF? TextBox { get; internal set; }
        public float FontPx { get; internal set; }
        // ponytail: not disposed, the GDI+ finalizer frees it; layouts are replaced on every ShowDrawing, so few are alive.
        internal GraphicsPath? TextPath { get; set; }
        public string? PillText { get; internal set; }
        public RectangleF? Pill { get; internal set; }
        internal (Box Ring, float Corner)? Ring { get; set; }
        public RectangleF Bounds { get; internal set; }
    }

    /// <summary>
    /// Lays out the parts that can be drawn. A part with a non-finite coordinate, or any point or box corner outside
    /// <paramref name="screen"/> (default: the virtual screen), is skipped: edits can move or scale a part off-screen. So is a
    /// shape whose strokes leave the monitor under its anchor.
    /// Label pills that would overlap move apart (<see cref="LabelLayout"/>).
    /// </summary>
    public static IReadOnlyList<PartLayout> Layout(IReadOnlyList<DrawPart> parts, Rectangle? screen = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var bounds = screen ?? SystemInformation.VirtualScreen;
        var layouts = new List<PartLayout>();
        var pills = new List<(PartLayout Layout, Box Pill, Rectangle WorkArea)>();
        foreach (var part in parts)
        {
            var op = part.Op;
            if (!Inside(op, bounds)) continue;
            var anchor = op.Points.Count > 0 ? op.Points[0] : op.Box.Centre;
            var at = new Point((int)anchor.X, (int)anchor.Y);
            var dip = DpiScale.ForPoint(at);
            var work = Screen.FromPoint(at).WorkingArea;
            var layout = new PartLayout(part, dip);
            var paint = RectangleF.Empty;
            switch (op.Kind)
            {
                case DrawOpKind.Text or DrawOpKind.Label:
                {
                    if (string.IsNullOrEmpty(op.Text)) continue;
                    var px = op.Kind == DrawOpKind.Text && op.Args.TryGetValue("size", out var size)
                        && float.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s * part.Scale : TextDip * dip;
                    using var font = new Font(HandFamily, px, GraphicsUnit.Pixel);
                    var measured = Measure.MeasureString(op.Text, font, (int)(TextMaxWidthDip * dip), TextFormat);
                    var box = new RectangleF(anchor.X, anchor.Y, measured.Width + 2, measured.Height + 2);
                    layout.TextBox = box;
                    layout.FontPx = px;
                    layout.TextPath = new GraphicsPath();
                    layout.TextPath.AddString(op.Text, font.FontFamily, (int)FontStyle.Regular, px,
                        new RectangleF(box.X, box.Y, TextMaxWidthDip * dip, box.Height + px), TextFormat);
                    paint = Inflate(box, HaloDip * dip + 2);
                    break;
                }
                case DrawOpKind.Dot:
                    paint = new RectangleF(anchor.X - DotDip * dip - 1, anchor.Y - DotDip * dip - 1, 2 * DotDip * dip + 2, 2 * DotDip * dip + 2);
                    break;
                case DrawOpKind.Point or DrawOpKind.Target:
                {
                    var frame = op.Kind == DrawOpKind.Target ? op.Box : (Box?)null;
                    var ring = ClickRingLayout.From(frame, op.Kind == DrawOpKind.Target ? op.Box.Centre : anchor, dip, ToBox(work));
                    layout.Ring = (ring.Ring, ring.CornerRadius);
                    paint = Inflate(ToRect(ring.Ring), CursorRenderer.RingPaintMarginDip * dip);
                    if (op.Text is { Length: > 0 } text)
                    {
                        var size = CursorRenderer.LabelSize(text, dip);
                        var y = ring.LabelBelow ? ring.LabelAnchor.Y : ring.LabelAnchor.Y - size.Height;
                        layout.PillText = text;
                        pills.Add((layout, new Box(ring.LabelAnchor.X - size.Width / 2, y, size.Width, size.Height), work));
                    }
                    break;
                }
                default:
                {
                    var outline = ShapeGeometry.Outline(op, dip, part.Scale);
                    if (outline.Count == 0) continue; // drawn by a later helper (graph, card, …) or nothing to draw
                    // The strokes must stay on the part's monitor (only their band may overhang its edge, as for a highlight
                    // on the taskbar). Checked before the wobble, so an oversized shape costs nothing.
                    var monitor = RectangleF.Inflate(Screen.FromPoint(at).Bounds, 1, 1);
                    if (!monitor.Contains(Extent(outline))) continue;
                    var seed = InkStyle.Seed(part.Id);
                    layout.Strokes = outline.Select((stroke, i) =>
                    {
                        var wobbled = InkStyle.Wobble(stroke, seed + i, 1.2f * dip, 28 * dip);
                        // A closed outline stays closed: both ends take the first sample.
                        return stroke.Count > 2 && stroke[0] == stroke[^1] && wobbled.Count > 1
                            ? wobbled.Take(wobbled.Count - 1).Append(wobbled[0]).ToArray() : wobbled;
                    }).ToArray();
                    paint = Inflate(Extent(layout.Strokes), BandWidth(layout) / 2 + 2 * dip + 2);
                    if (op.Args.GetValueOrDefault("label") is { Length: > 0 } label)
                    {
                        var size = CursorRenderer.LabelSize(label, dip);
                        var gap = PillGapDip * dip;
                        var above = op.Box.Y - gap - size.Height;
                        var y = above >= work.Top ? above : op.Box.Bottom + gap;
                        layout.PillText = label;
                        pills.Add((layout, new Box(op.Box.X, y, size.Width, size.Height), work));
                    }
                    break;
                }
            }
            layout.Bounds = paint;
            layouts.Add(layout);
        }

        foreach (var group in pills.GroupBy(p => p.WorkArea))
        {
            var items = group.ToArray();
            var resolved = LabelLayout.Resolve(items.Select(p => p.Pill).ToArray(), ToBox(group.Key), items[0].Layout.Dip);
            for (var i = 0; i < items.Length; i++)
            {
                var pill = ToRect(resolved[i]);
                items[i].Layout.Pill = pill;
                items[i].Layout.Bounds = RectangleF.Union(items[i].Layout.Bounds, pill);
            }
        }
        return layouts;
    }

    /// <summary>
    /// Paints one part: <paramref name="progress"/> (0..1) is how much of it the pen has drawn, <paramref name="alpha"/> fades all
    /// of it (reduced-motion fade-in, erase). <paramref name="accent"/> is used unless the op has its own colour.
    /// </summary>
    public static void Draw(Graphics g, PartLayout layout, float progress, float alpha, Color accent)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(layout);
        progress = Math.Clamp(progress, 0, 1);
        alpha = Math.Clamp(alpha, 0, 1);
        if (progress <= 0 || alpha <= 0) return;
        var op = layout.Part.Op;
        var colour = ColourOf(op, accent);
        var dip = layout.Dip;
        Color Fade(Color c, float a = 1) => Color.FromArgb((int)Math.Round(c.A * a * alpha), c);

        switch (op.Kind)
        {
            case DrawOpKind.Text or DrawOpKind.Label when layout.TextBox is { } box && layout.TextPath is { } path:
            {
                var state = g.Save();
                g.SetClip(new RectangleF(box.X - HaloDip * dip - 2, box.Y - HaloDip * dip - 2, (box.Width + 2 * HaloDip * dip + 4) * progress, box.Height + 2 * HaloDip * dip + 4));
                using (var halo = new Pen(Fade(Color.White, HaloAlpha / 255f), 2 * HaloDip * dip) { LineJoin = LineJoin.Round })
                    g.DrawPath(halo, path);
                using (var fill = new SolidBrush(Fade(colour))) g.FillPath(fill, path);
                g.Restore(state);
                break;
            }
            case DrawOpKind.Dot:
            {
                var c = op.Points[0];
                var r = DotDip * dip * progress;
                using var fill = new SolidBrush(Fade(colour));
                g.FillEllipse(fill, c.X - r, c.Y - r, 2 * r, 2 * r);
                break;
            }
            case DrawOpKind.Point or DrawOpKind.Target when layout.Ring is { } ring:
                CursorRenderer.DrawClickRing(g, ToRect(ring.Ring), ring.Corner, dip, colour, alpha * progress);
                break;
            default:
                DrawStrokes(g, layout, progress, colour, alpha);
                break;
        }

        if (progress >= 1 && layout.Pill is { } pill && layout.PillText is { } text)
            CursorRenderer.DrawLabel(g, pill, text, dip, colour, alpha);
    }

    /// <summary>Where the pen is at <paramref name="progress"/>: the end of the drawn stroke, the edge of the text being written.</summary>
    public static Pt? PenTip(PartLayout layout, float progress)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Strokes.Count > 0) return InkStyle.Reveal(layout.Strokes, Math.Clamp(progress, 0, 1)).Tip;
        if (layout.TextBox is { } box) return new Pt(box.X + box.Width * Math.Clamp(progress, 0, 1), box.Y + box.Height * 0.7f);
        var op = layout.Part.Op;
        return op.Points.Count > 0 ? op.Points[0] : op.Box.Centre;
    }

    private static void DrawStrokes(Graphics g, PartLayout layout, float progress, Color colour, float alpha)
    {
        var op = layout.Part.Op;
        var dip = layout.Dip;
        var (visible, _) = InkStyle.Reveal(layout.Strokes, progress);
        var style = op.Kind == DrawOpKind.Highlight ? "highlighter" : op.Args.GetValueOrDefault("style") ?? "pen";

        if (ShapeGeometry.IsClosed(op))
        {
            // Every stroke that ends where it began: a path washes only its subpaths closed with Z.
            using var wash = new SolidBrush(Color.FromArgb((int)Math.Round(255 * InkStyle.WashAlpha * progress * alpha), colour));
            foreach (var stroke in layout.Strokes.Where(s => s.Count > 2 && s[0] == s[^1]))
                g.FillPolygon(wash, stroke.Select(p => new PointF(p.X, p.Y)).ToArray());
        }

        if (style is "highlighter" or "marker")
        {
            var a = style == "highlighter" ? InkStyle.HighlighterAlpha : 1;
            using var band = new Pen(Color.FromArgb((int)Math.Round(255 * a * alpha), colour), BandWidth(layout))
            {
                LineJoin = LineJoin.Round,
                StartCap = style == "highlighter" ? LineCap.Square : LineCap.Round,
                EndCap = style == "highlighter" ? LineCap.Square : LineCap.Round,
            };
            foreach (var stroke in visible.Where(s => s.Count >= 2))
                g.DrawLines(band, stroke.Select(p => new PointF(p.X, p.Y)).ToArray());
            return;
        }

        // The pen: each segment at the taper width for where it sits along its whole stroke (not just the drawn part).
        using var pen = new Pen(Color.FromArgb((int)Math.Round(255 * alpha), colour), 1) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var s = 0; s < visible.Count; s++)
        {
            var stroke = visible[s];
            var total = InkStyle.Length(layout.Strokes[s]);
            var along = 0f;
            for (var i = 1; i < stroke.Count; i++)
            {
                Pt a = stroke[i - 1], b = stroke[i];
                var length = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                pen.Width = InkStyle.PenWidth(total > 0 ? (along + length / 2) / total : 0.5f) * dip;
                g.DrawLine(pen, a.X, a.Y, b.X, b.Y);
                along += length;
            }
        }
    }

    private static float BandWidth(PartLayout layout)
    {
        var op = layout.Part.Op;
        if (op.Kind == DrawOpKind.Highlight) return Math.Max(op.Box.H, InkStyle.HighlighterWidth * layout.Dip);
        return op.Args.GetValueOrDefault("style") switch
        {
            "highlighter" => InkStyle.HighlighterWidth * layout.Dip,
            "marker" => MarkerDip * layout.Dip,
            _ => 3.2f * layout.Dip,
        };
    }

    private static Color ColourOf(DrawOp op, Color accent)
    {
        if (op.Color is not { Length: 7 } hex) return accent;
        return int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb) ? Color.FromArgb(255, Color.FromArgb(rgb)) : accent;
    }

    /// <summary>Finite and inside the screen: every point and both box corners.</summary>
    private static bool Inside(DrawOp op, Rectangle screen)
    {
        bool In(float x, float y) => float.IsFinite(x) && float.IsFinite(y) && x >= screen.Left && y >= screen.Top && x <= screen.Right && y <= screen.Bottom;
        return In(op.Box.X, op.Box.Y) && In(op.Box.Right, op.Box.Bottom) && op.Points.All(p => In(p.X, p.Y));
    }

    private static RectangleF Extent(IReadOnlyList<IReadOnlyList<Pt>> strokes)
    {
        var points = strokes.SelectMany(s => s).ToArray();
        return RectangleF.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    private static RectangleF Inflate(RectangleF r, float by)
    {
        r.Inflate(by, by);
        return r;
    }

    private static RectangleF ToRect(Box b) => new(b.X, b.Y, b.W, b.H);

    private static bool Installed(string family)
    {
        try { using var _ = new FontFamily(family); return true; }
        catch (ArgumentException) { return false; }
    }

    private static Box ToBox(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
