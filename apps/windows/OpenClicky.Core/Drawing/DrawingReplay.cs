namespace OpenClicky.Core.Drawing;

/// <summary>
/// Collects one talk turn's lines as they are spoken and drawn, and builds the <see cref="SavedDrawing"/> when the turn ends.
/// Spoken text is cleaned here (it comes from the model), so what is stored always passes the store's checks. Any thread; the
/// owner serialises calls.
/// </summary>
public sealed class DrawingTurnRecorder
{
    private readonly List<SavedLine> _lines = [];
    private IReadOnlyList<Box> _monitors = [];
    private int _total;

    /// <summary>The screens the turn captured, in screenshot order.</summary>
    public void SetMonitors(IReadOnlyList<ScreenCaptureResult> screens) =>
        _monitors = screens.Take(DrawingStore.MaxMonitors).Select(s => new Box(s.MonitorLeft, s.MonitorTop, s.MonitorWidth, s.MonitorHeight)).ToArray();

    /// <summary>A line that was spoken (or read for its duration), with the mapped ops that showed with it (possibly none).</summary>
    public void AddLine(string spoken, TimeSpan duration, IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> mapped)
    {
        if (_lines.Count >= DrawingStore.MaxLines) return;
        var kept = Keep(mapped, DrawingStore.MaxOpsPerLine);
        _lines.Add(new(DrawingStore.CleanText(spoken, DrawingStore.MaxSpoken), Math.Round(duration.TotalSeconds, 2),
            kept.Select(m => m.Virtual).ToArray(), kept.Select(m => m.Scale).ToArray()));
    }

    /// <summary>
    /// The ops the store would accept, at most <paramref name="room"/> and within the drawing's total: one it would refuse (a bidi
    /// character in a MARK label, say) is left out of the saved copy rather than making the whole save fail.
    /// </summary>
    private (DrawOp Virtual, float Scale)[] Keep(IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> mapped, int room)
    {
        var kept = mapped.Where(m => m.Scale is >= 0.01f and <= 100f && DrawingStore.IsStorable(m.Virtual))
            .Take(Math.Max(0, Math.Min(room, DrawingStore.MaxOps - _total))).Select(m => (m.Virtual, m.Scale)).ToArray();
        _total += kept.Length;
        return kept;
    }

    /// <summary>Ops that showed with the last line after it was queued (what is left of the reply). A turn with no line yet gets a silent one.</summary>
    public void AppendToLast(TimeSpan duration, IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> mapped)
    {
        if (mapped.Count == 0) return;
        if (_lines.Count == 0) { AddLine("", duration, mapped); return; }
        var last = _lines[^1];
        var more = Keep(mapped, DrawingStore.MaxOpsPerLine - last.Ops.Count);
        _lines[^1] = last with
        {
            Ops = [.. last.Ops, .. more.Select(m => m.Virtual)],
            Scales = [.. last.Scales ?? Enumerable.Repeat(1f, last.Ops.Count), .. more.Select(m => m.Scale)],
        };
    }

    /// <summary>
    /// The saved drawing, or null when the turn drew no part (only edits, undo or clear, or nothing). The title is the first
    /// spoken line cut to <see cref="DrawingStore.MaxTitle"/> characters, else "Drawing".
    /// </summary>
    public SavedDrawing? Build(DateTimeOffset now)
    {
        if (!_lines.Any(l => l.Ops.Any(o => !o.IsEdit))) return null;
        var first = _lines.Select(l => l.Spoken).FirstOrDefault(s => s.Length > 0);
        var title = DrawingStore.CleanText(first, DrawingStore.MaxTitle);
        return new(Guid.NewGuid().ToString("N"), title.Length > 0 ? title : "Drawing", now, _lines.ToArray(), _monitors);
    }
}

/// <summary>One line of a replay: what to say, how long to take, and the ops to apply, ready for <see cref="DrawingDocument.Apply"/>.</summary>
public sealed record ReplayLine(string Spoken, TimeSpan Duration, IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> Ops);

/// <summary>Turns a saved drawing into replay steps, at its original coordinates or scaled to fit a target rectangle.</summary>
public static class DrawingReplayPlan
{
    /// <summary>The box around every part the drawing adds (edits excluded), or null when it adds none.</summary>
    public static Box? ContentBounds(SavedDrawing d)
    {
        Box? bounds = null;
        foreach (var op in d.Lines.SelectMany(l => l.Ops).Where(o => !o.IsEdit))
        {
            var box = op.Box;
            foreach (var p in op.Points) box = box.Union(new Box(p.X, p.Y, 0, 0));
            bounds = bounds is { } b ? b.Union(box) : box;
        }
        return bounds;
    }

    /// <summary>
    /// The uniform scale (at most <paramref name="maxScale"/>, 1 by default: a small drawing keeps its size) and the offset that centre <paramref name="content"/> in
    /// <paramref name="target"/>: a point p maps to (p - content origin) * scale + offset.
    /// </summary>
    public static (float Scale, Pt Offset) Fit(Box content, Box target, float maxScale = 1f)
    {
        var scale = Math.Min(maxScale, Math.Min(content.W > 0 ? target.W / content.W : maxScale, content.H > 0 ? target.H / content.H : maxScale));
        if (!float.IsFinite(scale) || scale <= 0) scale = 1;
        var centre = target.Centre;
        return (scale, new(centre.X - content.W * scale / 2, centre.Y - content.H * scale / 2));
    }

    /// <summary>
    /// The drawing as replay lines. With <paramref name="target"/> null the parts keep their original virtual coordinates;
    /// otherwise they are scaled to fit it (strokes and text keep their size in DIP, only the geometry shrinks). Edits pass
    /// through; each part's source op is rebuilt from its monitor and scale so an edit that targets it moves it right.
    /// </summary>
    public static IReadOnlyList<ReplayLine> Build(SavedDrawing d, Box? target, float maxScale = 1f)
    {
        (float Scale, Pt Offset, Pt Origin)? fit = null;
        if (target is { } t && ContentBounds(d) is { } content)
        {
            var (scale, offset) = Fit(content, t, maxScale);
            fit = (scale, offset, new Pt(content.X, content.Y));
        }
        var lines = new List<ReplayLine>();
        foreach (var line in d.Lines)
        {
            var ops = new List<(DrawOp, DrawOp, float)>();
            for (var i = 0; i < line.Ops.Count; i++)
            {
                var op = line.Ops[i];
                if (op.IsEdit) { ops.Add((op, op, 1f)); continue; }
                var scale = line.Scales is { } s && i < s.Count && s[i] > 0 ? s[i] : 1f;
                var source = AsSource(op, d.Monitors, scale);
                ops.Add((fit is { } f ? Fitted(op, f) : op, source, fit is { } g ? scale * g.Scale : scale));
            }
            lines.Add(new(line.Spoken, TimeSpan.FromSeconds(line.DurationSeconds), ops));
        }
        return lines;
    }

    private static DrawOp Fitted(DrawOp op, (float Scale, Pt Offset, Pt Origin) f)
    {
        Pt P(Pt p) => new((p.X - f.Origin.X) * f.Scale + f.Offset.X, (p.Y - f.Origin.Y) * f.Scale + f.Offset.Y);
        var b = op.Box;
        var at = P(new Pt(b.X, b.Y));
        return op with { Points = op.Points.Select(P).ToArray(), Box = new(at.X, at.Y, b.W * f.Scale, b.H * f.Scale) };
    }

    /// <summary>The op as it was before mapping: screenshot pixels relative to its monitor (what the model saw), for edits that target it.</summary>
    private static DrawOp AsSource(DrawOp op, IReadOnlyList<Box> monitors, float scale)
    {
        var m = op.Screen is int n && n >= 1 && n <= monitors.Count ? monitors[n - 1] : default;
        Pt P(Pt p) => new((p.X - m.X) / scale, (p.Y - m.Y) / scale);
        var at = P(new Pt(op.Box.X, op.Box.Y));
        return op with { Points = op.Points.Select(P).ToArray(), Box = new(at.X, at.Y, op.Box.W / scale, op.Box.H / scale) };
    }
}
