using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenClicky.Core.Drawing;

/// <summary>One drawn part: <see cref="Op"/> in virtual pixels, <see cref="Source"/> in screenshot pixels.</summary>
public sealed record DrawPart(string Id, DrawOp Op, DrawOp Source, float Scale, int Seq);

public sealed record PartAnimation(DrawPart Part, TimeSpan Start, TimeSpan Duration, bool Erase);

public static class DrawingPacing
{
    /// <summary>How long a spoken line takes, so the parts cued to it can reveal across it.</summary>
    public static TimeSpan LineDuration(string text, double speed) =>
        TimeSpan.FromSeconds(Math.Max(0.6, text.Length * 0.065 / (speed > 0 ? speed : 1)));
}

/// <summary>
/// The parts drawn so far and their edits. Not thread-safe: the owner applies ops on one thread.
/// Every change (add, replace, edit, erase, clear) pushes an undo snapshot; <c>undo</c> pops one.
/// </summary>
public sealed class DrawingDocument
{
    public const int MaxParts = 64;
    public const int MaxUndo = 20;
    public const int MaxDescription = 2000;

    private static readonly TimeSpan MinReveal = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan MaxReveal = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan EraseDuration = TimeSpan.FromSeconds(0.4);
    private static readonly JsonSerializerOptions LabelJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private List<DrawPart> _parts = [];
    private readonly List<DrawPart[]> _undo = [];
    private int _seq;

    public IReadOnlyList<DrawPart> Parts => _parts.AsReadOnly();
    public bool IsEmpty => _parts.Count == 0;

    /// <summary>Applies mapped ops in order. New parts reveal one after another across <paramref name="lineDuration"/>.</summary>
    public IReadOnlyList<PartAnimation> Apply(IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> ops, TimeSpan lineDuration)
    {
        ArgumentNullException.ThrowIfNull(ops);
        var firstNewSeq = _seq + 1;
        var animations = new List<PartAnimation>();
        foreach (var (op, source, scale) in ops)
        {
            switch (op.Kind)
            {
                case DrawOpKind.Undo:
                    if (_undo.Count == 0) break;
                    _parts = [.. _undo[^1]];
                    _undo.RemoveAt(_undo.Count - 1);
                    break;
                case DrawOpKind.Clear:
                    if (IsEmpty) break;
                    Push();
                    _parts.Clear();
                    break;
                case DrawOpKind.Erase:
                {
                    var index = IndexOf(op.Id);
                    if (index < 0) break;
                    Push();
                    var part = _parts[index];
                    _parts.RemoveAt(index);
                    // A part added earlier in this same batch was never shown, so it has nothing to fade.
                    if (part.Seq < firstNewSeq) animations.Add(new(part, TimeSpan.Zero, EraseDuration, true));
                    break;
                }
                case DrawOpKind.Recolor or DrawOpKind.Move or DrawOpKind.MoveTo or DrawOpKind.Scale:
                {
                    var index = IndexOf(op.Id);
                    if (index < 0) break;
                    var edited = Edit(_parts[index], op);
                    // An edit that would blow geometry up (repeated scale=4, huge moves) is refused; the part stays as it was.
                    if (!Sane(edited.Op) || !Sane(edited.Source)) break;
                    Push();
                    _parts[index] = edited;
                    break;
                }
                default:
                {
                    // Auto ids skip ids the model chose, so an unnamed part never replaces a named one.
                    if (op.Id is null) while (IndexOf($"p{_seq + 1}") >= 0) _seq++;
                    var id = op.Id ?? $"p{_seq + 1}";
                    var index = IndexOf(id);
                    if (index < 0 && _parts.Count >= MaxParts) break;
                    Push();
                    var part = new DrawPart(id, op, source, scale, ++_seq);
                    if (index < 0) _parts.Add(part); else _parts[index] = part;
                    break;
                }
            }
        }
        var revealed = _parts.Where(p => p.Seq >= firstNewSeq).OrderBy(p => p.Seq).ToArray();
        if (revealed.Length > 0)
        {
            var each = lineDuration / revealed.Length;
            each = each < MinReveal ? MinReveal : each > MaxReveal ? MaxReveal : each;
            for (var i = 0; i < revealed.Length; i++) animations.Add(new(revealed[i], each * i, each, false));
        }
        return animations;
    }

    /// <summary>One line per part, in screenshot coordinates, for the next prompt: <c>id kind "label" at x,y w×h screenN</c>.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        foreach (var part in _parts)
        {
            var s = part.Source;
            var label = s.Args.GetValueOrDefault("label") ?? s.Text;
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{part.Id} {s.Kind.ToString().ToLowerInvariant()}{(label is null ? "" : " " + JsonSerializer.Serialize(label, LabelJson))} at {Round(s.Box.X)},{Round(s.Box.Y)} {Round(s.Box.W)}×{Round(s.Box.H)}{(s.Screen is int n ? $" screen{n}" : "")}");
            if (text.Length + (text.Length > 0 ? 1 : 0) + line.Length > MaxDescription) break;
            if (text.Length > 0) text.Append('\n');
            text.Append(line);
        }
        return text.ToString();
    }

    /// <summary>Empties the document and forgets undo history (Esc).</summary>
    public void Clear()
    {
        _parts.Clear();
        _undo.Clear();
    }

    private static int Round(float value) => (int)MathF.Round(value);

    private const float MaxCoordinate = 99_999;

    internal static bool Sane(DrawOp op)
    {
        static bool Ok(float v) => float.IsFinite(v) && Math.Abs(v) <= MaxCoordinate;
        return Ok(op.Box.X) && Ok(op.Box.Y) && Ok(op.Box.W) && Ok(op.Box.H) && Ok(op.Box.Right) && Ok(op.Box.Bottom)
            && op.Points.All(p => Ok(p.X) && Ok(p.Y));
    }

    private int IndexOf(string? id) => id is null ? -1 : _parts.FindIndex(p => p.Id == id);

    private void Push()
    {
        _undo.Add([.. _parts]);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }

    private static DrawPart Edit(DrawPart part, DrawOp edit)
    {
        int Arg(string key) => int.Parse(edit.Args[key], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        switch (edit.Kind)
        {
            case DrawOpKind.Recolor:
                return part with { Op = part.Op with { Color = edit.Color }, Source = part.Source with { Color = edit.Color } };
            case DrawOpKind.Scale:
                var factor = float.Parse(edit.Args["factor"], CultureInfo.InvariantCulture);
                return part with { Op = ScaleAbout(part.Op, factor), Source = ScaleAbout(part.Source, factor) };
            default:
                // moveto x,y places the part's anchor (its first point, else its box corner) there in screenshot pixels.
                var anchor = part.Source.Points.Count > 0 ? part.Source.Points[0] : new Pt(part.Source.Box.X, part.Source.Box.Y);
                var (dx, dy) = edit.Kind == DrawOpKind.Move ? (Arg("dx"), Arg("dy")) : (Arg("x") - anchor.X, Arg("y") - anchor.Y);
                return part with { Op = Shift(part.Op, dx * part.Scale, dy * part.Scale), Source = Shift(part.Source, dx, dy) };
        }
    }

    private static DrawOp Shift(DrawOp op, float dx, float dy) => op with
    {
        Points = op.Points.Select(p => new Pt(p.X + dx, p.Y + dy)).ToArray(),
        Box = op.Box with { X = op.Box.X + dx, Y = op.Box.Y + dy },
    };

    private static DrawOp ScaleAbout(DrawOp op, float factor)
    {
        var c = op.Box.Centre;
        return op with
        {
            Points = op.Points.Select(p => new Pt(c.X + (p.X - c.X) * factor, c.Y + (p.Y - c.Y) * factor)).ToArray(),
            Box = new(c.X - op.Box.W * factor / 2, c.Y - op.Box.H * factor / 2, op.Box.W * factor, op.Box.H * factor),
        };
    }
}
