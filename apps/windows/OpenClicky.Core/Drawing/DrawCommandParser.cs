using System.Globalization;
using System.Text;

namespace OpenClicky.Core.Drawing;

/// <summary>
/// Extracts <c>[DRAW:&lt;op&gt; key=value …]</c> tags from a reply. Every tag is removed from the returned text; only valid
/// ones (the first 24 tags, at most 3 buttons) become ops. A tag ends at the first unescaped ']' outside double quotes and
/// never crosses a line break, so an unclosed tag or quote cannot swallow the rest of the reply. Model output is untrusted: anything malformed,
/// unknown or out of range drops the whole tag.
/// </summary>
public static class DrawCommandParser
{
    public const int MaxTags = 24;
    public const int MaxPoints = 32;
    public const int MaxPathCommands = 64;
    public const int MaxLabel = 60;
    public const int MaxText = 280;
    public const int MaxButtons = 3;
    public const int MaxPrompt = 300;

    private const int MaxValue = 100_000;
    private const string Prefix = "[DRAW:";

    private static readonly Dictionary<string, DrawOpKind> OpNames =
        Enum.GetValues<DrawOpKind>().Where(k => k != DrawOpKind.Figure).ToDictionary(k => k.ToString().ToLowerInvariant());

    /// <param name="partial">Streaming text: a trailing start of "[DRAW:" is cut too, so it is never spoken.</param>
    public static (string Text, IReadOnlyList<DrawOp> Ops) Parse(string reply, bool partial = false)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var text = new StringBuilder(reply.Length);
        var ops = new List<DrawOp>();
        int tags = 0, buttons = 0;
        var removed = false;
        for (var i = 0; i < reply.Length;)
        {
            if (string.CompareOrdinal(reply, i, Prefix, 0, Prefix.Length) != 0)
            {
                text.Append(reply[i++]);
                continue;
            }
            removed = true;
            var end = FindClose(reply, i + Prefix.Length);
            var closed = end < reply.Length && reply[end] == ']';
            if (closed && ++tags <= MaxTags && Build(reply[(i + Prefix.Length)..end], i) is { } op
                && (op.Kind != DrawOpKind.Button || ++buttons <= MaxButtons))
                ops.Add(op);
            i = closed ? end + 1 : end;
            // Drop the gap the tag leaves behind so "a [DRAW:…] b" reads "a b".
            if (i < reply.Length && reply[i] is '\r' or '\n')
                while (text.Length > 0 && text[^1] is ' ' or '\t') text.Length--;
            else if (text.Length == 0 || char.IsWhiteSpace(text[^1]))
                while (i < reply.Length && reply[i] is ' ' or '\t') i++;
        }
        if (partial)
            for (var k = Math.Min(Prefix.Length - 1, text.Length); k > 0; k--)
                if (text.ToString(text.Length - k, k) == Prefix[..k]) { text.Length -= k; removed = true; break; }
        return (removed ? text.ToString().Trim() : reply, ops);
    }

    /// <summary>Converts parsed MARK annotations (screenshot pixels) into the equivalent draw ops.</summary>
    public static IReadOnlyList<DrawOp> FromMarks(IReadOnlyList<ScreenAnnotation> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        var ops = new List<DrawOp>();
        foreach (var m in marks)
        {
            var args = new Dictionary<string, string> { ["label"] = m.Label };
            var points = m.Points?.Select(p => new Pt(p.X, p.Y)).ToArray() ?? [];
            DrawOp Op(DrawOpKind kind, Pt[] pts, Box box) => new(kind, null, pts, box, m.Label, null, m.ScreenNumber, args, 0);
            switch (m.Shape)
            {
                case "rect": ops.Add(Op(DrawOpKind.Rect, [], new(m.X, m.Y, m.A, m.B))); break;
                case "circle": ops.Add(Op(DrawOpKind.Circle, [], new(m.X, m.Y, m.A, m.B))); break;
                case "arrow": Pt[] arrow = [new(m.X, m.Y), new(m.A, m.B)]; ops.Add(Op(DrawOpKind.Arrow, arrow, Bounds(arrow))); break;
                case "polygon": args["closed"] = "1"; ops.Add(Op(DrawOpKind.Poly, points, Bounds(points))); break;
                case "freehand": ops.Add(Op(DrawOpKind.Trace, points, Bounds(points))); break;
                case "curve": ops.Add(Op(DrawOpKind.Curve, points, Bounds(points))); break;
                case "text": ops.Add(Op(DrawOpKind.Text, [new(m.X, m.Y)], new(m.X, m.Y, 0, 0))); break;
            }
        }
        return ops;
    }

    /// <summary>
    /// Index of the closing ']' (outside double quotes), or of the line break / end of text when the tag is unclosed.
    /// A backslash escapes the next character; a line break always ends the tag, even inside an unterminated quote.
    /// </summary>
    private static int FindClose(string s, int start)
    {
        var quoted = false;
        for (var j = start; j < s.Length; j++)
        {
            var c = s[j];
            if (c is '\r' or '\n') return j;
            if (c == '\\' && j + 1 < s.Length && s[j + 1] is not ('\r' or '\n')) j++;
            else if (c == '"') quoted = !quoted;
            else if (c == ']' && !quoted) return j;
        }
        return s.Length;
    }

    private static DrawOp? Build(string inner, int offset)
    {
        var k = 0;
        while (k < inner.Length && inner[k] is >= 'a' and <= 'z') k++;
        if (!OpNames.TryGetValue(inner[..k], out var kind) || Tokenize(inner, k) is not { } raw) return null;
        var r = new Keys(raw);

        var id = r.Str("id", kind >= DrawOpKind.Recolor && kind < DrawOpKind.Undo, 24);
        if (id is not null && !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')) return null;
        var color = r.Str("color", kind == DrawOpKind.Recolor, 7);
        if (color is not null && !(color.Length == 7 && color[0] == '#' && color.Skip(1).All(char.IsAsciiHexDigit))) return null;
        var screen = r.Int("screen", false, min: 1);
        var label = r.Str("label", kind is DrawOpKind.Point or DrawOpKind.Target, MaxLabel);

        Pt[] points = [];
        Box box = default;
        void Anchor() { var x = r.Int("x", true); var y = r.Int("y", true); points = [new(x ?? 0, y ?? 0)]; box = new(x ?? 0, y ?? 0, 0, 0); }
        void BoxKeys() { var x = r.Int("x", true); var y = r.Int("y", true); box = new(x ?? 0, y ?? 0, r.Int("w", true, 1) ?? 0, r.Int("h", true, 1) ?? 0); }
        void Centred() { var x = r.Int("x", true) ?? 0; var y = r.Int("y", true) ?? 0; var rad = r.Int("r", true, 1) ?? 0; points = [new(x, y)]; box = new(x - rad, y - rad, 2 * rad, 2 * rad); }
        void Across() { var x = r.Int("x", true) ?? 0; var y = r.Int("y", true) ?? 0; var w = r.Int("w", true, 1) ?? 0; points = [new(x, y), new(x + w, y)]; box = new(x, y, w, 0); }
        void Pts(int min, int max) { points = r.Points(min, max); box = Bounds(points); }

        switch (kind)
        {
            case DrawOpKind.Line or DrawOpKind.Arrow: Pts(2, MaxPoints); r.OneOf("style", false, "pen", "marker", "highlighter"); break;
            case DrawOpKind.Curve: Pts(4, 4); break;
            case DrawOpKind.Poly: Pts(3, MaxPoints); r.Int("closed", false, 0, 1); break;
            case DrawOpKind.Trace: Pts(3, MaxPoints); break;
            case DrawOpKind.Tie: Pts(2, 2); break;
            case DrawOpKind.Path:
                if (r.Str("d", true, int.MaxValue) is { } d && ParsePath(d) is { } path) { points = path.Points; box = Bounds(points); raw["d"] = path.Letters; }
                else return null;
                break;
            case DrawOpKind.Circle: if (raw.ContainsKey("r")) Centred(); else BoxKeys(); break;
            case DrawOpKind.Ring: Centred(); break;
            case DrawOpKind.Rect: BoxKeys(); r.Int("r", false); break;
            case DrawOpKind.Highlight or DrawOpKind.Frame or DrawOpKind.Spotlight: BoxKeys(); break;
            case DrawOpKind.Target: BoxKeys(); r.Int("hover", false, 0, 1); break;
            case DrawOpKind.Graph:
                BoxKeys(); r.Str("xlabel", false, MaxLabel); r.Str("ylabel", false, MaxLabel);
                if (!ValidSeries(r.Str("series", true, MaxText))) return null;
                break;
            case DrawOpKind.Timeline:
                Across();
                if (r.Str("items", true, MaxText) is not { } items || items.Split(';') is not { Length: <= 8 } list || list.Any(string.IsNullOrWhiteSpace)) return null;
                break;
            case DrawOpKind.Underline or DrawOpKind.Strike: Across(); break;
            case DrawOpKind.Dot or DrawOpKind.Point: Anchor(); break;
            case DrawOpKind.Text: Anchor(); r.Str("text", true, MaxText); r.Int("size", false, 10, 48); break;
            case DrawOpKind.Label: Anchor(); r.Str("text", true, MaxText); break;
            case DrawOpKind.Cross or DrawOpKind.Check or DrawOpKind.Star: Anchor(); raw.TryAdd("size", "24"); r.Int("size", false, 1); break;
            case DrawOpKind.Scroll: Anchor(); r.OneOf("dir", true, "up", "down"); break;
            case DrawOpKind.Badge: Anchor(); r.Int("n", true, 1, 15); break;
            case DrawOpKind.Button: Anchor(); r.Str("text", true, MaxText); r.Str("prompt", true, MaxPrompt); break;
            case DrawOpKind.Card:
                Anchor(); r.Str("title", true, MaxLabel); r.Str("text", true, MaxText); r.Str("stock", false, MaxLabel);
                if (r.Int("w", false, 200, 360) is int w) box = box with { W = w };
                break;
            case DrawOpKind.Sticky: Anchor(); r.Str("text", true, MaxText); r.Str("stock", false, MaxLabel); break;
            case DrawOpKind.Move: r.Int("dx", true, signed: true); r.Int("dy", true, signed: true); break;
            case DrawOpKind.MoveTo: r.Int("x", true); r.Int("y", true); break;
            case DrawOpKind.Scale:
                if (!(double.TryParse(r.Str("factor", true, 12), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var f) && f is >= 0.25 and <= 4)) return null;
                break;
        }
        if (r.Bad) return null;
        var args = raw.Where(p => r.Known.Contains(p.Key) && p.Key is not ("id" or "color" or "screen")).ToDictionary(p => p.Key, p => p.Value);
        return new(kind, id, points, box, args.GetValueOrDefault("text") ?? label, color, screen, args, offset);
    }

    /// <summary>Reads <c>key=value</c> pairs after the op name: lowercase keys, bare values, or double-quoted values with \" \] \\ escapes.</summary>
    private static Dictionary<string, string>? Tokenize(string s, int k)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            var gap = k;
            while (k < s.Length && s[k] is ' ' or '\t') k++;
            if (k == s.Length) return args;
            if (k == gap) return null;
            var keyStart = k;
            while (k < s.Length && s[k] is >= 'a' and <= 'z') k++;
            if (k == keyStart || k == s.Length || s[k] != '=') return null;
            var key = s[keyStart..k++];
            if (k < s.Length && s[k] == '"')
            {
                var value = new StringBuilder();
                for (k++; ; )
                {
                    if (k == s.Length) return null;
                    var c = s[k++];
                    if (c == '\\' && k < s.Length && s[k] is '"' or ']' or '\\') value.Append(s[k++]);
                    else if (c == '"') break;
                    else value.Append(c);
                }
                args[key] = value.ToString();
            }
            else
            {
                var valueStart = k;
                while (k < s.Length && s[k] is not (' ' or '\t')) k++;
                var value = s[valueStart..k];
                if (value.Length == 0 || value.Contains('"') || value.Contains('\\')) return null;
                args[key] = value;
            }
        }
    }

    private static (Pt[] Points, string Letters)? ParsePath(string d)
    {
        var letters = new StringBuilder();
        var numbers = new List<int>();
        var points = new List<Pt>();
        int Arity(char c) => c switch { 'M' or 'L' => 2, 'Q' => 4, 'C' => 6, _ => 0 };
        bool Flush()
        {
            if (letters.Length == 0) return numbers.Count == 0;
            if (numbers.Count != Arity(letters[^1])) return false;
            for (var n = 0; n < numbers.Count; n += 2) points.Add(new(numbers[n], numbers[n + 1]));
            numbers.Clear();
            return true;
        }
        for (var i = 0; i < d.Length;)
        {
            var c = d[i];
            if (c is ' ' or ',' or '\t') { i++; continue; }
            if (c is 'M' or 'L' or 'C' or 'Q' or 'Z')
            {
                if (!Flush() || letters.Length == 0 && c != 'M' || letters.Length == MaxPathCommands) return null;
                letters.Append(c);
                i++;
                continue;
            }
            var start = i;
            while (i < d.Length && char.IsAsciiDigit(d[i])) i++;
            if (i == start || i - start > 5) return null;
            var value = int.Parse(d.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture);
            if (value >= MaxValue) return null;
            numbers.Add(value);
        }
        return Flush() && letters.Length > 0 ? (points.ToArray(), letters.ToString()) : null;
    }

    private static bool ValidSeries(string? series)
    {
        if (series?.Split(';') is not { Length: <= 4 } lines) return false;
        foreach (var line in lines)
        {
            var values = line.Split(',');
            if (values.Length > 24 || values.Any(v => !int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || Math.Abs((long)n) >= MaxValue))
                return false;
        }
        return true;
    }

    /// <summary>Bidi embedding/override/isolate controls and directional marks, which could reorder model text on screen.
    /// Other format characters (such as the emoji joiner U+200D) stay allowed.</summary>
    private static bool IsBidiControl(char c) =>
        c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏' or '؜';

    private static Box Bounds(IReadOnlyList<Pt> points)
    {
        if (points.Count == 0) return default;
        float minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
        return new(minX, minY, points.Max(p => p.X) - minX, points.Max(p => p.Y) - minY);
    }

    /// <summary>Typed reads over one tag's raw keys. Any invalid or missing required value marks the tag bad; Known lists the keys the op uses.</summary>
    private sealed class Keys(Dictionary<string, string> raw)
    {
        public bool Bad;
        public readonly HashSet<string> Known = [];

        public string? Str(string key, bool required, int max)
        {
            Known.Add(key);
            if (!raw.TryGetValue(key, out var value)) { Bad |= required; return null; }
            if (value.Length == 0 || value.Length > max || value.Any(c => char.IsControl(c) || IsBidiControl(c))) { Bad = true; return null; }
            return value;
        }

        public int? Int(string key, bool required, int min = 0, int max = MaxValue - 1, bool signed = false)
        {
            if (Str(key, required, 12) is not { } value) return null;
            if (int.TryParse(value, signed ? NumberStyles.AllowLeadingSign : NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                && n >= (signed ? -max : min) && n <= max) return n;
            Bad = true;
            return null;
        }

        public void OneOf(string key, bool required, params string[] allowed)
        {
            if (Str(key, required, MaxLabel) is { } value && !allowed.Contains(value)) Bad = true;
        }

        public Pt[] Points(int min, int max)
        {
            if (Str("pts", true, int.MaxValue) is not { } value) return [];
            if (value.Count(c => c == ',') + 1 > 2 * max) { Bad = true; return []; }
            var parts = value.Split(',');
            var points = new Pt[parts.Length / 2];
            for (var i = 0; i < parts.Length && !Bad; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n >= MaxValue) Bad = true;
                else if (i % 2 == 1) points[i / 2] = new(int.Parse(parts[i - 1], CultureInfo.InvariantCulture), n);
            }
            if (parts.Length % 2 != 0 || points.Length < min || points.Length > max) Bad = true;
            return Bad ? [] : points;
        }
    }
}
