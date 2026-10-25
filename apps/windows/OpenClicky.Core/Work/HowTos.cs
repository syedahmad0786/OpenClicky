using System.Text;
using System.Text.Json;

namespace OpenClicky.Core.Work;

/// <summary>One lesson a job run reported (<see cref="JobResult.Learned"/>). Model-authored, so untrusted: plain text only.</summary>
public sealed record HowTo(string Text, DateTimeOffset LearnedAt, string JobId);

/// <summary>
/// The kept lessons: at most <see cref="MaxKept"/> of <see cref="MaxChars"/> characters, newest win, duplicates ignoring case collapse.
/// A JSON file at the constructor's path (E12 moves it under the agent root). Safe for concurrent jobs finishing together.
/// A corrupt or unreadable file starts empty. Texts are never logged.
/// </summary>
public sealed class HowToStore
{
    public const int MaxKept = 60;
    public const int MaxChars = 240;

    private readonly string _path;
    private readonly object _gate = new();
    private List<HowTo> _items = [];

    public HowToStore(string path)
    {
        _path = path;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<HowTo>>(File.ReadAllText(_path));
            if (loaded is not null) foreach (var h in loaded) AddCore(h.Text, h.JobId, h.LearnedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _items = []; // missing, locked or corrupt: start empty
        }
    }

    public IReadOnlyList<HowTo> All { get { lock (_gate) return [.. _items]; } }

    public void Add(string text, string jobId, DateTimeOffset at) { lock (_gate) AddCore(text, jobId, at); }

    private void AddCore(string? text, string? jobId, DateTimeOffset at)
    {
        var clean = Clean(text);
        if (clean.Length == 0) return;
        _items.RemoveAll(h => string.Equals(h.Text, clean, StringComparison.OrdinalIgnoreCase)); // relearned: the newer one replaces it
        _items.Add(new HowTo(clean, at, jobId ?? ""));
        if (_items.Count > MaxKept) _items = [.. _items.OrderByDescending(h => h.LearnedAt).Take(MaxKept)];
    }

    /// <summary>Atomic write (temp file, then replace), so a crash or a second writer never leaves a half file.</summary>
    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items));
            File.Move(temp, _path, overwrite: true);
        }
    }

    /// <summary>One line of plain text: control characters and newlines become spaces, runs of spaces collapse, double quotes become single, capped.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(Math.Min(text.Length, MaxChars));
        foreach (var c in text)
        {
            var ch = char.IsControl(c) || (int)c is 0x2028 or 0x2029 ? ' ' : c == '"' ? '\'' : c;
            if (ch == ' ' && (sb.Length == 0 || sb[^1] == ' ')) continue;
            sb.Append(ch);
            if (sb.Length >= MaxChars) break;
        }
        if (sb.Length > 0 && char.IsHighSurrogate(sb[^1])) sb.Length--; // never end on half an emoji
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Picks the lessons a job prompt carries: the newest few, plus the ones sharing the most words with the job.</summary>
public static class HowToChooser
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "to", "of", "in", "on", "at", "for", "with", "from", "by", "is", "are", "was", "were", "be",
        "it", "this", "that", "these", "those", "i", "you", "my", "your", "me", "we", "our", "do", "does", "did", "how", "what", "when",
        "where", "which", "who", "can", "should", "will", "just", "then",
    };

    /// <summary><paramref name="query"/> is the job's title, request and follow-up. Related means the most shared words (stop words ignored),
    /// ties newer first; lessons already in the newest group are not repeated.</summary>
    public static IReadOnlyList<HowTo> Choose(IReadOnlyList<HowTo> all, string query, int newest = 10, int related = 5)
    {
        var recent = all.OrderByDescending(h => h.LearnedAt).ToList();
        var chosen = recent.Take(newest).ToList();
        var words = Tokens(query);
        if (words.Count == 0 || related <= 0) return chosen;
        chosen.AddRange(recent.Skip(newest)
            .Select(h => (h, score: Tokens(h.Text).Count(words.Contains)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score) // OrderBy is stable: equal scores keep newest first
            .Take(related)
            .Select(x => x.h));
        return chosen;
    }

    private static HashSet<string> Tokens(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length >= 2 && !StopWords.Contains(word.ToString())) set.Add(word.ToString());
            word.Clear();
        }
        foreach (var c in text ?? "")
            if (char.IsLetterOrDigit(c)) word.Append(char.ToLowerInvariant(c)); else Flush();
        Flush();
        return set;
    }
}
