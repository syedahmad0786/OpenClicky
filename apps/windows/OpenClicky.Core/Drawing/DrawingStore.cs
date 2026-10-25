using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenClicky.Core.Drawing;

/// <summary>
/// One spoken line of a drawing turn and the ops cued to it, in virtual-screen pixels. <see cref="Scales"/> (parallel to
/// <see cref="Ops"/>, null meaning 1) keeps each op's screenshot-to-virtual scale, so lengths such as a text <c>size</c> replay at
/// the size they were drawn.
/// </summary>
public sealed record SavedLine(string Spoken, double DurationSeconds, IReadOnlyList<DrawOp> Ops, IReadOnlyList<float>? Scales = null);

/// <summary>
/// A drawing that was kept: what was said and drawn, line by line. <see cref="Monitors"/> are the captured screens' virtual
/// rectangles in screenshot order (an op's <c>Screen</c> is 1 + its index). No screenshot is ever stored.
/// </summary>
public sealed record SavedDrawing(string Id, string Title, DateTimeOffset CreatedAt, IReadOnlyList<SavedLine> Lines, IReadOnlyList<Box> Monitors);

/// <summary>
/// Saved drawings as one JSON file each under <see cref="DefaultFolder"/>. The files are untrusted on the way back in: every
/// drawing is validated again on <see cref="Load"/> (ids, text, numbers, figures, counts), and one that fails is skipped.
/// Not thread-safe: the owner calls it from one thread.
/// </summary>
public sealed partial class DrawingStore
{
    public const int MaxBytes = 512 * 1024, MaxFiles = 200;
    public const int MaxTitle = 80, MaxSpoken = 2_000, MaxLines = 64, MaxOpsPerLine = 64, MaxOps = 512, MaxMonitors = 16;
    private const double MaxDuration = 120;
    private const int MaxArgs = 16, MaxArgValue = 2_000, MaxOpText = DrawCommandParser.MaxText;
    private const string Extension = ".json";

    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true, // System.Numerics vectors keep their coordinates in fields
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _folder;

    /// <param name="folder">Where the files live; <see cref="DefaultFolder"/> when null. Created on the first save.</param>
    public DrawingStore(string? folder = null) => _folder = folder ?? DefaultFolder;

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky", "drawings");

    /// <summary>An id the store will use as a file name: letters, digits, '-' and '_' only.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>
    /// Writes <paramref name="d"/> atomically (temp file, then a replacing move) and prunes the oldest files beyond
    /// <see cref="MaxFiles"/>. Returns the drawing's id.
    /// </summary>
    /// <exception cref="InvalidDataException">The drawing is over <see cref="MaxBytes"/>.</exception>
    /// <exception cref="ArgumentException">The drawing breaks a limit the store enforces (bad ids, text, numbers or arguments).</exception>
    public string Save(SavedDrawing d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!Validate(d)) throw new ArgumentException("The drawing holds content the store will not keep.", nameof(d));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(d, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The drawing is too large to keep.");
        Directory.CreateDirectory(_folder);
        var path = PathOf(d.Id);
        var temp = Path.Combine(_folder, $".{d.Id}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            // The file's time is the drawing's own, so "oldest" means the earliest drawn, and ties cannot reorder a prune.
            File.SetLastWriteTimeUtc(temp, d.CreatedAt.UtcDateTime);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Prune();
        return d.Id;
    }

    /// <summary>The drawing, or null when it is missing, unreadable, over the size limit or fails validation.</summary>
    public SavedDrawing? Load(string id)
    {
        if (!IsValidId(id)) return null;
        try
        {
            // Read through a stream capped at one byte over the limit: the file cannot grow between a size check and the read.
            using var stream = new FileStream(PathOf(id), FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(MaxBytes + 1);
            try
            {
                var length = stream.ReadAtLeast(buffer.AsSpan(0, MaxBytes + 1), MaxBytes + 1, throwOnEndOfStream: false);
                if (length > MaxBytes) return null;
                var d = JsonSerializer.Deserialize<SavedDrawing>(buffer.AsSpan(0, length), Json);
                return d is not null && d.Id == id && Validate(d) ? d : null;
            }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
        }
        // A hostile file can fail in many ways (bad JSON, nulls where the model has none): any non-fatal one just skips the file.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Every readable drawing, newest first. Corrupt, oversized or invalid files are skipped.</summary>
    public IReadOnlyList<(string Id, string Title, DateTimeOffset CreatedAt)> List()
    {
        if (!Directory.Exists(_folder)) return [];
        var found = new List<(string Id, string Title, DateTimeOffset CreatedAt)>();
        // A bad file is skipped by Load; a folder that cannot be listed throws, so the caller can tell it from an empty one.
        foreach (var file in Directory.EnumerateFiles(_folder, "*" + Extension))
            if (Load(Path.GetFileNameWithoutExtension(file)) is { } d) found.Add((d.Id, d.Title, d.CreatedAt));
        return found.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Deletes the file; false when there was none.</summary>
    public bool Delete(string id)
    {
        if (!IsValidId(id)) return false;
        var path = PathOf(id);
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private string PathOf(string id) => Path.Combine(_folder, id + Extension);

    private void Prune()
    {
        try
        {
            var files = new DirectoryInfo(_folder).EnumerateFiles("*" + Extension)
                .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
            foreach (var old in files.Take(Math.Max(0, files.Count - MaxFiles)))
            {
                try { old.Delete(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* kept; the next save prunes again */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* nothing to prune */ }
    }

    // ---- validation: used on save and, because files are untrusted, again on load ----

    /// <summary>Text meant to be shown or spoken: no control or bidi characters, no lone surrogates.</summary>
    public static bool IsSafeText(string? s, int max, bool allowEmpty = false) =>
        s is not null && s.Length <= max && (allowEmpty || s.Length > 0) && IsWellFormed(s)
        && !s.Any(c => char.IsControl(c) || DrawCommandParser.IsBidiControl(c));

    /// <summary>Makes model-authored text safe to store: control and bidi characters become spaces (runs collapse), a cut never splits a surrogate pair.</summary>
    public static string CleanText(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = new char[s.Length];
        var n = 0;
        var space = true; // also trims the start
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { chars[n++] = c; chars[n++] = s[++i]; space = false; continue; }
            if (char.IsSurrogate(c)) continue;
            if (char.IsControl(c) || char.IsWhiteSpace(c) || DrawCommandParser.IsBidiControl(c))
            {
                if (!space) chars[n++] = ' ';
                space = true;
            }
            else { chars[n++] = c; space = false; }
        }
        var text = new string(chars, 0, n).TrimEnd();
        if (text.Length <= max) return text;
        var cut = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..cut].TrimEnd();
    }

    private static bool IsWellFormed(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            else if (char.IsSurrogate(s[i])) return false;
        }
        return true;
    }

    private static bool Finite(float v) => float.IsFinite(v);

    internal static bool Validate(SavedDrawing d)
    {
        if (!IsValidId(d.Id) || !IsSafeText(d.Title, MaxTitle) || d.Lines is null || d.Monitors is null) return false;
        if (d.CreatedAt.Year is < 2000 or > 2200) return false;
        if (d.Lines.Count is 0 or > MaxLines || d.Monitors.Count > MaxMonitors) return false;
        foreach (var m in d.Monitors)
            if (!(Finite(m.X) && Finite(m.Y) && Finite(m.W) && Finite(m.H) && Math.Abs(m.X) <= 99_999 && Math.Abs(m.Y) <= 99_999 && m.W > 0 && m.H > 0 && m.W <= 99_999 && m.H <= 99_999)) return false;
        var ops = 0;
        foreach (var line in d.Lines)
        {
            if (line is null || !IsSafeText(line.Spoken, MaxSpoken, allowEmpty: true) || !double.IsFinite(line.DurationSeconds) || line.DurationSeconds is < 0 or > MaxDuration) return false;
            if (line.Ops is null || line.Ops.Count > MaxOpsPerLine || (ops += line.Ops.Count) > MaxOps) return false;
            if (line.Scales is { } scales && (scales.Count != line.Ops.Count || scales.Any(s => !Finite(s) || s is < 0.01f or > 100f))) return false;
            if (!line.Ops.All(ValidOp)) return false;
        }
        return true;
    }

    private static bool ValidOp(DrawOp op)
    {
        if (op is null || !Enum.IsDefined(op.Kind) || op.Points is null || op.Args is null || op.Points.Count > DrawCommandParser.MaxPoints) return false;
        if (op.Id is not null && !OpIdPattern().IsMatch(op.Id)) return false;
        if (op.Text is not null && !IsSafeText(op.Text, MaxOpText)) return false;
        if (op.Color is not null && !ColorPattern().IsMatch(op.Color)) return false;
        if (op.Screen is < 1 or > MaxMonitors) return false;
        if (!DrawingDocument.Sane(op) || op.Box.W < 0 || op.Box.H < 0) return false;
        if (op.Args.Count > MaxArgs || op.Args.Any(a => !ArgKeyPattern().IsMatch(a.Key) || !IsSafeText(a.Value, MaxArgValue, allowEmpty: true))) return false;
        if (!ValidShape(op)) return false;
        return op.Kind == DrawOpKind.Figure ? op.Figure is { } f && FigureBlockParser.IsValid(f) : op.Figure is null;
    }

    /// <summary>Whether a stored op could have come out of <see cref="DrawCommandParser"/> (or the recorder): the stored points are never indexed blind.</summary>
    internal static bool IsStorable(DrawOp op) => ValidOp(op);

    /// <summary>Points and arguments per kind, as the parser requires them; anything the document or renderer would index or parse must be there.</summary>
    private static bool ValidShape(DrawOp op)
    {
        var points = op.Points.Count;
        var (min, max) = op.Kind switch
        {
            DrawOpKind.Line or DrawOpKind.Arrow or DrawOpKind.Trace => (2, DrawCommandParser.MaxPoints), // a freehand MARK may have just two
            DrawOpKind.Poly => (3, DrawCommandParser.MaxPoints),
            DrawOpKind.Curve => (4, 4),
            DrawOpKind.Tie or DrawOpKind.Timeline or DrawOpKind.Underline or DrawOpKind.Strike => (2, 2),
            DrawOpKind.Path => (1, DrawCommandParser.MaxPoints),
            DrawOpKind.Circle => (0, 1),
            DrawOpKind.Ring or DrawOpKind.Dot or DrawOpKind.Point or DrawOpKind.Text or DrawOpKind.Label or DrawOpKind.Cross or DrawOpKind.Check
                or DrawOpKind.Star or DrawOpKind.Scroll or DrawOpKind.Badge or DrawOpKind.Button or DrawOpKind.Card or DrawOpKind.Sticky => (1, 1),
            _ => (0, 0), // boxes, graph, figure and every edit carry none
        };
        if (points < min || points > max) return false;
        string? Arg(string key) => op.Args.GetValueOrDefault(key);
        bool Int(string key, bool signed, int lo, int hi) =>
            int.TryParse(Arg(key), signed ? NumberStyles.AllowLeadingSign : NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= lo && n <= hi;
        bool OptionalInt(string key, int lo, int hi) => !op.Args.ContainsKey(key) || Int(key, false, lo, hi);
        const int Limit = 99_999;
        switch (op.Kind)
        {
            case DrawOpKind.Move: return op.Id is not null && Int("dx", true, -Limit, Limit) && Int("dy", true, -Limit, Limit);
            case DrawOpKind.MoveTo: return op.Id is not null && Int("x", false, 0, Limit) && Int("y", false, 0, Limit);
            case DrawOpKind.Scale:
                return op.Id is not null && double.TryParse(Arg("factor"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var factor) && factor is >= 0.25 and <= 4;
            case DrawOpKind.Recolor: return op.Id is not null && op.Color is not null;
            case DrawOpKind.Erase: return op.Id is not null;
            case DrawOpKind.Text: return OptionalInt("size", 10, 48);
            case DrawOpKind.Cross or DrawOpKind.Check or DrawOpKind.Star: return OptionalInt("size", 1, DrawCommandParser.MaxSize);
            case DrawOpKind.Badge: return Int("n", false, 1, 15);
            case DrawOpKind.Graph: return DrawCommandParser.ValidSeries(Arg("series"));
            case DrawOpKind.Timeline: return Arg("items")?.Split(';') is { Length: <= 8 } items && items.All(i => !string.IsNullOrWhiteSpace(i));
            case DrawOpKind.Path:
            {
                // d is reduced to command letters; M and L take one point, Q two, C three.
                if (Arg("d") is not { Length: > 0 and <= DrawCommandParser.MaxPathCommands } letters || letters[0] != 'M') return false;
                var needed = 0;
                foreach (var c in letters)
                {
                    var arity = c switch { 'M' or 'L' => 1, 'Q' => 2, 'C' => 3, 'Z' => 0, _ => -1 };
                    if (arity < 0) return false;
                    needed += arity;
                }
                return needed == points;
            }
            default: return true;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}\\z")] private static partial Regex IdPattern();
    [GeneratedRegex("^[a-z0-9_-]{1,24}\\z")] private static partial Regex OpIdPattern();
    [GeneratedRegex("^#[0-9A-Fa-f]{6}\\z")] private static partial Regex ColorPattern();
    [GeneratedRegex("^[a-z]{1,12}\\z")] private static partial Regex ArgKeyPattern();
}
