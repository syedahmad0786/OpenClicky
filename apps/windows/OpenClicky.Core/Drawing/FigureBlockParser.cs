using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClicky.Core.Drawing;

/// <summary>
/// Reads fenced <c>```figure</c> JSON blocks from a reply and removes them from its text (they are never spoken). Model output
/// is untrusted: the first two valid figures are kept, each with at most twelve parts and 20,000 triangles (a figure over budget
/// is dropped); an unknown kind, an out-of-range number or a malformed label drops that part; malformed JSON drops the figure.
/// The block is stripped in every case, as is an unterminated one.
/// </summary>
public static partial class FigureBlockParser
{
    public const int MaxFigures = 2, MaxParts = 12, MaxTriangles = 20_000;
    public const int MaxBlockChars = 16_000;
    private const string Fence = "```figure";
    private const double MaxBoxValue = 99_999, MinBoxSide = 16, MinSize = 0.01, MaxSize = 100, MaxAt = 100, MaxDegrees = 3600;
    private static readonly Dictionary<string, FigureKind> Kinds = Enum.GetValues<FigureKind>().ToDictionary(k => k.ToString().ToLowerInvariant());
    private static readonly string[] ParamKeys = ["teeth", "coils", "radius"];
    private static readonly IReadOnlyDictionary<string, string> NoArgs = new Dictionary<string, string>();

    public static (string Text, IReadOnlyList<FigureSpec> Figures) Parse(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var figures = new List<FigureSpec>();
        return (Scan(reply, figures), figures);
    }

    /// <summary>
    /// Whether a figure read back from a saved file keeps to the limits <see cref="Parse"/> enforces on a model's block: stored
    /// geometry is never trusted. Ranges, label text, the part count and the triangle budget are all re-checked.
    /// </summary>
    internal static bool IsValid(FigureSpec f)
    {
        static bool Within(Vector3 v, Func<float, bool> ok) => ok(v.X) && ok(v.Y) && ok(v.Z);
        static bool Good(string? s) => s is null || s.Length is > 0 and <= DrawCommandParser.MaxLabel && !s.Any(c => c == '`' || char.IsControl(c) || DrawCommandParser.IsBidiControl(c));
        var b = f.Box;
        if (f.Id is not null && !IdPattern().IsMatch(f.Id)) return false;
        if (f.Screen is < 1 or > 16 || !Good(f.Title)) return false;
        if (!(float.IsFinite(b.X) && float.IsFinite(b.Y) && b.X >= 0 && b.Y >= 0 && b.X <= MaxBoxValue && b.Y <= MaxBoxValue
            && b.W >= MinBoxSide && b.H >= MinBoxSide && b.W <= MaxBoxValue && b.H <= MaxBoxValue)) return false;
        if (f.Parts.Count is 0 or > MaxParts) return false;
        var triangles = 0;
        foreach (var p in f.Parts)
        {
            if (!Enum.IsDefined(p.Kind) || !Within(p.Size, v => v is >= (float)MinSize and <= (float)MaxSize)
                || !Within(p.At, v => Math.Abs(v) <= MaxAt) || !Within(p.RotateDegrees, v => Math.Abs(v) <= MaxDegrees)) return false;
            if (p.Color is null || p.Color.Length != 0 && !(p.Color.Length == 7 && p.Color[0] == '#' && p.Color.Skip(1).All(char.IsAsciiHexDigit))) return false;
            if (!Good(p.Label) || p.Params is null || p.Params.Count > ParamKeys.Length
                || p.Params.Any(kv => !ParamKeys.Contains(kv.Key) || !double.IsFinite(kv.Value))) return false;
            if (p.Profile is { } profile && (p.Kind is not (FigureKind.Tube or FigureKind.Lathe or FigureKind.Extrude)
                || profile.Count > FigureMesh.MaxProfilePoints
                || profile.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || Math.Abs(v.X) > MaxAt || Math.Abs(v.Y) > MaxAt))) return false;
            triangles += FigureMesh.Build(p).TriangleCount;
            if (triangles > MaxTriangles) return false;
        }
        return true;
    }

    /// <summary>Only the text, for streaming partial replies: a block still being written, and a trailing start of the fence, are cut too.</summary>
    internal static string Strip(string raw) => Scan(raw, null);

    /// <summary>A figure as a draw op, so it is mapped, kept, edited and described like any other part.</summary>
    public static DrawOp ToOp(FigureSpec figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        return new(DrawOpKind.Figure, figure.Id, [], figure.Box, figure.Title, null, figure.Screen, NoArgs, 0, figure);
    }

    private static string Scan(string raw, List<FigureSpec>? figures)
    {
        var start = raw.IndexOf(Fence, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return CutPartialFence(raw, out var cut) ? cut : raw;
        var kept = new List<string>();
        var at = 0;
        while (start >= 0)
        {
            kept.Add(raw[at..start]);
            // A block closes at a fence that starts a line, so a "```" inside its JSON does not end it. A one-line block
            // ("```figure {…}```") closes at the first fence on its own line instead of swallowing the rest of the reply.
            var body = start + Fence.Length;
            var lineEnd = raw.IndexOf('\n', body);
            var inline = raw.IndexOf("```", body, (lineEnd < 0 ? raw.Length : lineEnd) - body, StringComparison.Ordinal);
            var close = lineEnd < 0 ? -1 : raw.IndexOf("\n```", lineEnd, StringComparison.Ordinal);
            int content, end;
            if (inline >= 0) (content, end, at) = (body, inline, inline + 3);
            else if (close >= 0) (content, end, at) = (Math.Min(lineEnd + 1, close), close, close + 4); // an empty block is the same newline
            else { at = raw.Length; break; } // still being written, or never closed: the rest is the block
            if (figures is not null && figures.Count < MaxFigures && end - content <= MaxBlockChars
                && Read(raw[content..end]) is { } figure) figures.Add(figure);
            start = raw.IndexOf(Fence, at, StringComparison.OrdinalIgnoreCase);
        }
        var rest = raw[at..];
        kept.Add(CutPartialFence(rest, out var tail) ? tail : rest);
        return string.Join(" ", kept.Select(s => s.Trim()).Where(s => s.Length > 0));
    }

    /// <summary>
    /// A trailing "```f", "```fig" … is the start of a block being written. A bare "```" is left alone: it may close another
    /// block (ag-ui, artifact), and those parsers cut a dangling one themselves.
    /// </summary>
    private static bool CutPartialFence(string text, out string cut)
    {
        var tail = text.LastIndexOf("```", StringComparison.Ordinal);
        var partial = tail >= 0 && text.Length - tail > 3 && Fence.StartsWith(text[tail..], StringComparison.OrdinalIgnoreCase);
        cut = partial ? text[..tail].TrimEnd() : text;
        return partial;
    }

    private static FigureSpec? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // No id, or an invalid one: null, so the drawing assigns a fresh one and it never replaces an earlier figure.
            var id = root.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String && IdPattern().IsMatch(idValue.GetString()!)
                ? idValue.GetString()! : null;
            if (!root.TryGetProperty("box", out var boxValue) || Numbers(boxValue, 4) is not { } b
                || b.Any(v => v < 0 || v > MaxBoxValue) || b[2] < MinBoxSide || b[3] < MinBoxSide) return null;
            int? screen = null;
            if (root.TryGetProperty("screen", out var screenValue))
            {
                if (screenValue.ValueKind != JsonValueKind.Number || !screenValue.TryGetInt32(out var n) || n is < 1 or > 16) return null;
                screen = n;
            }
            var spin = root.TryGetProperty("spin", out var spinValue) && spinValue.ValueKind == JsonValueKind.True;
            string? title = null;
            if (root.TryGetProperty("title", out var titleValue) && !Text(titleValue, out title)) return null;
            if (!root.TryGetProperty("parts", out var partsValue) || partsValue.ValueKind != JsonValueKind.Array) return null;
            var parts = new List<FigurePart>();
            foreach (var element in partsValue.EnumerateArray())
            {
                if (parts.Count == MaxParts) break; // a thirteenth part and later are dropped
                if (ReadPart(element) is { } part) parts.Add(part);
            }
            if (parts.Count == 0 || parts.Sum(p => FigureMesh.Build(p).TriangleCount) > MaxTriangles) return null;
            return new(id, new Box((float)Math.Round(b[0]), (float)Math.Round(b[1]), (float)Math.Round(b[2]), (float)Math.Round(b[3])), screen, spin, title, parts);
        }
        catch (JsonException) { return null; }
    }

    private static FigurePart? ReadPart(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("kind", out var kindValue) || kindValue.ValueKind != JsonValueKind.String
            || !Kinds.TryGetValue(kindValue.GetString()!.Trim().ToLowerInvariant(), out var kind)) return null;
        if (Vector(e, "size", Vector3.One, v => v is >= MinSize and <= MaxSize) is not { } size
            || Vector(e, "at", Vector3.Zero, v => Math.Abs(v) <= MaxAt) is not { } at
            || Vector(e, "rotate", Vector3.Zero, v => Math.Abs(v) <= MaxDegrees) is not { } rotate) return null;
        var color = "";
        if (e.TryGetProperty("color", out var colorValue))
        {
            if (colorValue.ValueKind != JsonValueKind.String || colorValue.GetString() is not { Length: 7 } hex || hex[0] != '#' || !hex.Skip(1).All(char.IsAsciiHexDigit)) return null;
            color = hex.ToUpperInvariant();
        }
        string? label = null;
        if (e.TryGetProperty("label", out var labelValue) && !Text(labelValue, out label)) return null;
        var numbers = new Dictionary<string, double>();
        foreach (var key in ParamKeys)
        {
            if (!e.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n)) return null;
            numbers[key] = n; // FigureMesh clamps it to the kind's range
        }
        List<Vector2>? profile = null;
        if (kind is FigureKind.Tube or FigureKind.Lathe or FigureKind.Extrude && e.TryGetProperty("profile", out var profileValue))
        {
            if (profileValue.ValueKind != JsonValueKind.Array || profileValue.GetArrayLength() > FigureMesh.MaxProfilePoints) return null;
            profile = [];
            foreach (var point in profileValue.EnumerateArray())
            {
                if (Numbers(point, 2) is not { } xy || xy.Any(v => Math.Abs(v) > MaxAt)) return null;
                profile.Add(new((float)xy[0], (float)xy[1]));
            }
        }
        return new(kind, size, at, rotate, color, label, numbers, profile);
    }

    /// <summary>A missing key gives <paramref name="fallback"/>; a present one must be three numbers that all pass <paramref name="ok"/>.</summary>
    private static Vector3? Vector(JsonElement e, string key, Vector3 fallback, Func<double, bool> ok)
    {
        if (!e.TryGetProperty(key, out var value)) return fallback;
        return Numbers(value, 3) is { } v && v.All(ok) ? new((float)v[0], (float)v[1], (float)v[2]) : null;
    }

    private static double[]? Numbers(JsonElement e, int count)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != count) return null;
        var values = new double[count];
        var i = 0;
        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var v) || !double.IsFinite(v)) return null;
            values[i++] = v;
        }
        return values;
    }

    /// <summary>A label or title: 1–60 characters with no control, bidi or backtick characters (it is shown on screen).</summary>
    private static bool Text(JsonElement e, out string? text)
    {
        text = e.ValueKind == JsonValueKind.String ? e.GetString()!.Trim() : null;
        return text is { Length: > 0 and <= DrawCommandParser.MaxLabel } && !text.Any(c => c == '`' || char.IsControl(c) || DrawCommandParser.IsBidiControl(c));
    }

    [GeneratedRegex("^[a-z0-9_-]{1,24}$")]
    private static partial Regex IdPattern();
}
