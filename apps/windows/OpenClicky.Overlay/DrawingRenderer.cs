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
    private const float SpotCornerDip = 8, SpotDim = 0.55f, BadgeDip = 22, BadgeTextDip = 12;
    private const float CardWidthDip = 280, StickyDip = 160, PaperPadDip = 14, TitleDip = 14, BodyDip = 12, PaperGapDip = 6, PaperCornerDip = 3;
    private const float TapeWidthDip = 40, TapeHeightDip = 12, ButtonTextDip = 13, ButtonHeightDip = 32, ButtonPadDip = 14, ButtonMaxWidthDip = 300;
    private const float ChartTextDip = 11, ChartGapDip = 6;
    private const int HaloAlpha = 179; // 70 %
    private const int MaxBodyLines = 8;
    /// <summary>At most three buttons are drawn (and clickable); a fourth is dropped.</summary>
    public const int MaxButtons = 3;
    /// <summary>The largest ink bitmap in pixels (two 4K monitors, 64 MB).</summary>
    public const long MaxInkPixels = 2L * 3840 * 2160;
    /// <summary>A spotlight dims the screen for this long, then fades out (<see cref="SpotlightFadeSeconds"/>, or the short reduced-motion fade).</summary>
    public const double SpotlightSeconds = 30, SpotlightFadeSeconds = 0.6, SpotlightReducedFadeSeconds = 0.12;

    /// <summary>The spotlight's opacity factor <paramref name="age"/> seconds after it appeared: 1 until 30 s, then down to 0 (gone).</summary>
    internal static float SpotlightAlpha(double age, bool reducedMotion) =>
        age <= SpotlightSeconds ? 1 : (float)Math.Max(0, 1 - (age - SpotlightSeconds) / (reducedMotion ? SpotlightReducedFadeSeconds : SpotlightFadeSeconds));
    private static readonly string HandFamily = Installed("Segoe Print") ? "Segoe Print" : "Segoe UI";
    // Semibold is a named instance of the variable font; fall back to its bold, then to the static Segoe UI faces.
    private static readonly (string Family, FontStyle Style) Semibold =
        Installed("Segoe UI Variable Display Semibold") ? ("Segoe UI Variable Display Semibold", FontStyle.Regular)
        : Installed("Segoe UI Variable Display") ? ("Segoe UI Variable Display", FontStyle.Bold)
        : Installed("Segoe UI Semibold") ? ("Segoe UI Semibold", FontStyle.Regular) : ("Segoe UI", FontStyle.Bold);
    private static readonly Color InkColour = ColorTranslator.FromHtml(HelperGeometry.Ink);
    /// <summary>Series colours after the accent for a graph with more than one line.</summary>
    private static readonly Color[] SeriesColours = [Color.Empty, Color.FromArgb(0xE4, 0x57, 0x2E), Color.FromArgb(0x17, 0xA3, 0x98), Color.FromArgb(0x8E, 0x5B, 0xD9)];
    private static readonly Graphics Measure = Graphics.FromImage(new Bitmap(1, 1));
    private static readonly StringFormat TextFormat = new(StringFormat.GenericTypographic) { Trimming = StringTrimming.None };
    private static readonly StringFormat LineFormat = new(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
    private static readonly StringFormat BodyFormat = new(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisWord };
    private static readonly StringFormat CentreFormat = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap,
    };
    private static readonly Dictionary<(string, FontStyle, float), Font> Fonts = [];

    // ponytail: fonts are cached for the life of the process; there are a handful of sizes (DIP times the monitor scales in use).
    private static Font FontOf(string family, FontStyle style, float px)
    {
        if (!Fonts.TryGetValue((family, style, px), out var font)) Fonts[(family, style, px)] = font = new Font(family, px, style, GraphicsUnit.Pixel);
        return font;
    }

    /// <summary>A card or sticky: the paper (before tilt), its text boxes and the stock.</summary>
    internal sealed class PaperLayout
    {
        public RectangleF Rect, TitleRect, BodyRect;
        public string? Title;
        public string Body = "";
        public Color Fill;
        public float Tilt;
        public PointF Centre => new(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2);
    }

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
        /// <summary>Spotlight: the area to dim (the screen) and the box that stays clear.</summary>
        internal (RectangleF Screen, RectangleF Hole, float Corner)? Spot { get; set; }
        internal (PointF Centre, float Radius, string Text)? Badge { get; set; }
        internal PaperLayout? Paper { get; set; }
        /// <summary>A button's pill in virtual pixels (a click catcher sits over it) and its label.</summary>
        public RectangleF? ButtonRect { get; internal set; }
        // ponytail: not disposed, as TextPath.
        internal GraphicsPath? LabelPath { get; set; }
        internal RectangleF? LabelBox { get; set; }
        /// <summary>Per-stroke colour (null: the part's own), aligned with <see cref="Strokes"/>: a graph's axes are ink, its lines differ.</summary>
        internal IReadOnlyList<Color?> StrokeColours { get; set; } = [];
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
        var buttons = 0;
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
            // Fills (badge, button, paper) must sit whole on the monitor under their anchor, like strokes: dropped, never clamped.
            bool OnMonitor(RectangleF r) => RectangleF.Inflate(Screen.FromPoint(at).Bounds, 1, 1).Contains(r);
            switch (op.Kind)
            {
                case DrawOpKind.Spotlight:
                {
                    var screenArea = (long)bounds.Width * bounds.Height <= MaxInkPixels ? bounds : Screen.FromPoint(at).Bounds;
                    var hole = RectangleF.Intersect(ToRect(op.Box), screenArea);
                    if (hole.Width <= 0 || hole.Height <= 0) continue;
                    layout.Spot = (screenArea, hole, SpotCornerDip * dip);
                    paint = screenArea;
                    break;
                }
                case DrawOpKind.Badge:
                {
                    var r = BadgeDip * dip / 2;
                    var disc = new RectangleF(anchor.X - r, anchor.Y - r, 2 * r, 2 * r);
                    if (!op.Args.TryGetValue("n", out var number) || !OnMonitor(disc)) continue;
                    layout.Badge = (new PointF(anchor.X, anchor.Y), r, number);
                    paint = Inflate(disc, 3 * dip);
                    break;
                }
                case DrawOpKind.Button:
                {
                    if (op.Text is not { Length: > 0 } label || !op.Args.ContainsKey("prompt") || buttons >= MaxButtons) continue;
                    var width = Math.Min(ButtonMaxWidthDip * dip, Measure.MeasureString(label, FontOf(Semibold.Family, Semibold.Style, ButtonTextDip * dip), 10_000, TextFormat).Width + 2 * ButtonPadDip * dip);
                    var pill = new RectangleF(anchor.X, anchor.Y, width, ButtonHeightDip * dip);
                    if (!OnMonitor(pill)) continue;
                    buttons++;
                    layout.ButtonRect = pill;
                    paint = Inflate(pill, 5 * dip);
                    break;
                }
                case DrawOpKind.Card or DrawOpKind.Sticky:
                {
                    var paper = LayoutPaper(op, part.Id, dip, anchor);
                    if (paper is null) continue;
                    var corners = Turned(paper.Rect, paper.Tilt, paper.Centre);
                    var cover = RectangleF.FromLTRB(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y));
                    if (!OnMonitor(cover)) continue;
                    layout.Paper = paper;
                    paint = Inflate(cover, (TapeHeightDip / 2 + 8) * dip);
                    break;
                }
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
                    if (op.Kind == DrawOpKind.Graph)
                    {
                        // Axes and tick marks in ink; the first line takes the part's colour, further lines their own.
                        var lines = layout.Strokes.Count - 1 - 2 * HelperGeometry.TickCount;
                        Color? StrokeColour(int i)
                        {
                            if (i == 0 || i > lines) return InkColour;
                            var own = SeriesColours[(i - 1) % SeriesColours.Length];
                            return own.IsEmpty ? null : own;
                        }
                        layout.StrokeColours = Enumerable.Range(0, layout.Strokes.Count).Select(StrokeColour).ToArray();
                    }
                    if (op.Kind is DrawOpKind.Graph or DrawOpKind.Timeline && ChartLabels(op, dip) is { } chart)
                    {
                        layout.LabelPath = chart.Path;
                        layout.LabelBox = chart.Box;
                        paint = RectangleF.Union(paint, Inflate(chart.Box, HaloDip * dip + 2));
                    }
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
            // Spotlights would stack their dimming (four of them black the screen): the newest (highest Seq) wins. A part
            // replaced by id keeps its place in the document, so the last in order is not always the newest.
            if (op.Kind == DrawOpKind.Spotlight)
            {
                if (layouts.Any(l => l.Part.Op.Kind == DrawOpKind.Spotlight && l.Part.Seq > part.Seq)) continue;
                layouts.RemoveAll(l => l.Part.Op.Kind == DrawOpKind.Spotlight);
            }
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
                DrawTextPath(g, path, box, progress, dip, colour, alpha);
                break;
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
            case DrawOpKind.Spotlight when layout.Spot is { } spot:
            {
                using var dimmed = new GraphicsPath(FillMode.Alternate);
                dimmed.AddRectangle(spot.Screen);
                using (var clear = CursorRenderer.RoundedRect(spot.Hole, spot.Corner)) dimmed.AddPath(clear, false);
                using var dim = new SolidBrush(Color.FromArgb((int)Math.Round(255 * SpotDim * progress * alpha), Color.Black));
                g.FillPath(dim, dimmed);
                break;
            }
            case DrawOpKind.Badge when layout.Badge is { } badge:
                DrawBadge(g, badge, colour, dip, alpha * progress);
                break;
            case DrawOpKind.Button when layout.ButtonRect is { } buttonRect:
                DrawButton(g, buttonRect, op.Text!, colour, dip, alpha * progress);
                break;
            case DrawOpKind.Card or DrawOpKind.Sticky when layout.Paper is { } paper:
                DrawPaper(g, paper, dip, alpha * progress);
                break;
            default:
                DrawStrokes(g, layout, progress, colour, alpha);
                // Tick numbers, axis titles and timeline items are written once the lines are mostly in.
                if (layout.LabelPath is { } labels && layout.LabelBox is { } labelBox && progress > 0.5f)
                    DrawTextPath(g, labels, labelBox, 1, dip, InkColour, alpha * Math.Min(1, (progress - 0.5f) * 2));
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
            pen.Color = Color.FromArgb((int)Math.Round(255 * alpha), s < layout.StrokeColours.Count ? layout.StrokeColours[s] ?? colour : colour);
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

    /// <summary>Text drawn as a path: a white halo, then the fill; <paramref name="progress"/> uncovers it from the left.</summary>
    private static void DrawTextPath(Graphics g, GraphicsPath path, RectangleF box, float progress, float dip, Color colour, float alpha)
    {
        Color Fade(Color c, float a = 1) => Color.FromArgb((int)Math.Round(c.A * a * alpha), c);
        var state = g.Save();
        g.SetClip(new RectangleF(box.X - HaloDip * dip - 2, box.Y - HaloDip * dip - 2, (box.Width + 2 * HaloDip * dip + 4) * progress, box.Height + 2 * HaloDip * dip + 4));
        using (var halo = new Pen(Fade(Color.White, HaloAlpha / 255f), 2 * HaloDip * dip) { LineJoin = LineJoin.Round })
            g.DrawPath(halo, path);
        using (var fill = new SolidBrush(Fade(colour))) g.FillPath(fill, path);
        g.Restore(state);
    }

    /// <summary>White on a dark fill, ink on a light one (the yellow accent).</summary>
    private static Color OnColour(Color fill) => (0.299f * fill.R + 0.587f * fill.G + 0.114f * fill.B) > 160 ? InkColour : Color.White;

    private static void DrawBadge(Graphics g, (PointF Centre, float Radius, string Text) badge, Color colour, float dip, float alpha)
    {
        Color Fade(Color c, float a = 1) => Color.FromArgb((int)Math.Round(c.A * a * alpha), c);
        var disc = new RectangleF(badge.Centre.X - badge.Radius, badge.Centre.Y - badge.Radius, 2 * badge.Radius, 2 * badge.Radius);
        using (var shadow = new SolidBrush(Fade(Color.Black, 0.18f)))
            g.FillEllipse(shadow, disc.X, disc.Y + dip, disc.Width, disc.Height);
        using (var fill = new SolidBrush(Fade(colour))) g.FillEllipse(fill, disc);
        using (var rim = new Pen(Fade(Color.White, 0.9f), 1.5f * dip)) g.DrawEllipse(rim, disc);
        using var text = new SolidBrush(Fade(OnColour(colour)));
        g.DrawString(badge.Text, FontOf(Semibold.Family, Semibold.Style, BadgeTextDip * dip), text, disc, CentreFormat);
    }

    private static void DrawButton(Graphics g, RectangleF pill, string label, Color colour, float dip, float alpha)
    {
        Color Fade(Color c, float a = 1) => Color.FromArgb((int)Math.Round(c.A * a * alpha), c);
        using (var shadow = new SolidBrush(Fade(Color.Black, 0.2f)))
            CursorRenderer.FillRounded(g, shadow, new RectangleF(pill.X, pill.Y + 2 * dip, pill.Width, pill.Height), pill.Height / 2);
        using (var fill = new SolidBrush(Fade(colour))) CursorRenderer.FillRounded(g, fill, pill, pill.Height / 2);
        using var text = new SolidBrush(Fade(OnColour(colour)));
        var inner = RectangleF.Inflate(pill, -ButtonPadDip * dip / 2, 0);
        g.DrawString(label, FontOf(Semibold.Family, Semibold.Style, ButtonTextDip * dip), text, inner, CentreFormat);
    }

    /// <summary>
    /// A card or sticky: a card is <c>w</c> wide (default 280 DIP) with a title and up to eight lines; a sticky is 160 DIP square.
    /// The paper's top left is the anchor. Null when it has no text to show.
    /// </summary>
    private static PaperLayout? LayoutPaper(DrawOp op, string id, float dip, Pt anchor)
    {
        var sticky = op.Kind == DrawOpKind.Sticky;
        var body = op.Text;
        var title = sticky ? null : op.Args.GetValueOrDefault("title");
        if (string.IsNullOrEmpty(body) && string.IsNullOrEmpty(title)) return null;
        float pad = PaperPadDip * dip, width = sticky ? StickyDip * dip : op.Box.W > 0 ? op.Box.W : CardWidthDip * dip;
        var inner = width - 2 * pad;
        var titleFont = FontOf(Semibold.Family, Semibold.Style, TitleDip * dip);
        var bodyFont = FontOf(HandFamily, FontStyle.Regular, BodyDip * dip);
        var titleHeight = title is null ? 0 : titleFont.Height;
        var lineLimit = MaxBodyLines * bodyFont.Height;
        var bodyLimit = sticky ? Math.Min(lineLimit, inner) : lineLimit; // a sticky's text stays on the square
        var bodyHeight = string.IsNullOrEmpty(body) ? 0 : Math.Min(bodyLimit, MathF.Ceiling(Measure.MeasureString(body, bodyFont, new SizeF(inner, bodyLimit), BodyFormat).Height));
        var gap = title is null || bodyHeight == 0 ? 0 : PaperGapDip * dip;
        var height = sticky ? width : 2 * pad + titleHeight + gap + bodyHeight;
        var x = anchor.X;
        var y = anchor.Y;
        return new PaperLayout
        {
            Rect = new RectangleF(x, y, width, height),
            TitleRect = new RectangleF(x + pad, y + pad, inner, titleHeight),
            BodyRect = new RectangleF(x + pad, y + pad + titleHeight + gap, inner, sticky ? height - 2 * pad - titleHeight - gap : bodyHeight),
            Title = title,
            Body = body ?? "",
            Fill = ColorTranslator.FromHtml(HelperGeometry.StockColour(op.Args.GetValueOrDefault("stock"), sticky)),
            Tilt = HelperGeometry.TiltDegrees(id),
        };
    }

    /// <summary>The four corners of <paramref name="rect"/> turned by <paramref name="degrees"/> about <paramref name="about"/>.</summary>
    private static PointF[] Turned(RectangleF rect, float degrees, PointF about)
    {
        PointF[] corners = [new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom)];
        using var turn = new Matrix();
        turn.RotateAt(degrees, about);
        turn.TransformPoints(corners);
        return corners;
    }

    /// <summary>Paper on the screen: a soft shadow of three offset fills, the stock, ink text, and a strip of tape across the top edge, all tilted together.</summary>
    private static void DrawPaper(Graphics g, PaperLayout paper, float dip, float alpha)
    {
        Color Fade(Color c, float a = 1) => Color.FromArgb((int)Math.Round(c.A * a * alpha), c);
        var state = g.Save();
        var centre = paper.Centre;
        g.TranslateTransform(centre.X, centre.Y);
        g.RotateTransform(paper.Tilt);
        g.TranslateTransform(-centre.X, -centre.Y);

        var rect = paper.Rect;
        float[] shadowAlpha = [0.10f, 0.08f, 0.06f];
        for (var i = 0; i < shadowAlpha.Length; i++)
        {
            var step = (i + 1) * dip;
            using var shadow = new SolidBrush(Fade(Color.Black, shadowAlpha[i]));
            CursorRenderer.FillRounded(g, shadow, new RectangleF(rect.X + step * 0.6f, rect.Y + step * 1.4f, rect.Width, rect.Height), PaperCornerDip * dip + step / 2);
        }
        using (var fill = new SolidBrush(Fade(paper.Fill))) CursorRenderer.FillRounded(g, fill, rect, PaperCornerDip * dip);
        using (var edge = new Pen(Fade(InkColour, 0.12f), dip)) g.DrawRectangle(edge, rect.X, rect.Y, rect.Width, rect.Height);

        using var ink = new SolidBrush(Fade(InkColour));
        if (paper.Title is { } title)
            g.DrawString(title, FontOf(Semibold.Family, Semibold.Style, TitleDip * dip), ink, paper.TitleRect, LineFormat);
        if (paper.Body.Length > 0)
            g.DrawString(paper.Body, FontOf(HandFamily, FontStyle.Regular, BodyDip * dip), ink, paper.BodyRect, BodyFormat);

        var tape = new RectangleF(centre.X - TapeWidthDip * dip / 2, rect.Top - TapeHeightDip * dip / 2, TapeWidthDip * dip, TapeHeightDip * dip);
        using (var strip = new SolidBrush(Fade(Color.White, 0.6f))) g.FillRectangle(strip, tape);
        using (var stripEdge = new Pen(Fade(InkColour, 0.10f), dip)) g.DrawRectangle(stripEdge, tape.X, tape.Y, tape.Width, tape.Height);
        g.Restore(state);
    }

    /// <summary>
    /// Tick numbers and axis titles of a graph, or the items of a timeline (alternately above and below the line), as one text
    /// path in the hand font. Null when there is nothing to write.
    /// </summary>
    private static (GraphicsPath Path, RectangleF Box)? ChartLabels(DrawOp op, float dip)
    {
        var px = ChartTextDip * dip;
        var font = FontOf(HandFamily, FontStyle.Regular, px);
        var path = new GraphicsPath();
        RectangleF? box = null;
        // anchor: 0 puts x at the text's left edge, 0.5 at its centre, 1 at its right edge.
        void Put(string text, float x, float y, float anchor)
        {
            var size = Measure.MeasureString(text, font, 10_000, TextFormat);
            var at = new PointF(x - size.Width * anchor, y);
            path.AddString(text, font.FontFamily, (int)font.Style, px, at, TextFormat);
            var written = new RectangleF(at, size);
            box = box is { } b ? RectangleF.Union(b, written) : written;
        }
        var gap = ChartGapDip * dip;
        if (op.Kind == DrawOpKind.Graph)
        {
            var (axes, _, ticks) = HelperGeometry.Graph(op);
            for (var i = 0; i < ticks.Count; i++)
            {
                if (i < HelperGeometry.TickCount) Put(ticks[i].Text, ticks[i].At.X - gap, ticks[i].At.Y - px / 2, 1);
                else Put(ticks[i].Text, ticks[i].At.X, ticks[i].At.Y + gap, 0.5f);
            }
            if (op.Args.GetValueOrDefault("ylabel") is { Length: > 0 } ylabel) Put(ylabel, axes[0].X, axes[0].Y - px - gap, 0);
            if (op.Args.GetValueOrDefault("xlabel") is { Length: > 0 } xlabel) Put(xlabel, (axes[1].X + axes[2].X) / 2, axes[1].Y + 2 * gap + px, 0.5f);
        }
        else
        {
            var (_, items) = HelperGeometry.Timeline(op);
            for (var i = 0; i < items.Count; i++)
            {
                var label = items[i].Label.Length > 24 ? items[i].Label[..23] + "…" : items[i].Label;
                Put(label, items[i].Tick.X, i % 2 == 0 ? items[i].Tick.Y - 2 * gap - px : items[i].Tick.Y + 2 * gap, 0.5f);
            }
        }
        if (box is not { } all) { path.Dispose(); return null; }
        return (path, all);
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
