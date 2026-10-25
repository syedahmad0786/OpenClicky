using OpenClicky.Core.Settings;
using OpenClicky.Core.Text;

namespace OpenClicky.Core.Dictation;

/// <summary>
/// Spots the user fixing one misheard word in text dictation just inserted. Fed the field's value now and then, it returns
/// the corrected word once, when exactly one word of the inserted span changed into a similar new one and the same value
/// was seen on two consecutive calls. Pure: it reads nothing and logs nothing.
/// </summary>
public sealed class DictationCorrectionDetector
{
    private const int MinWordLength = 3;
    private const double MinSimilarity = 0.5;

    private readonly string _prefix, _suffix;
    private readonly string[] _oldWords;
    private readonly IReadOnlyCollection<string> _dictionary;
    private string? _lastValue;
    private bool _done;

    public DictationCorrectionDetector(string valueBefore, string valueAfter, string inserted, IReadOnlyCollection<string> dictionary)
    {
        ArgumentNullException.ThrowIfNull(valueBefore);
        ArgumentNullException.ThrowIfNull(valueAfter);
        ArgumentNullException.ThrowIfNull(inserted);
        _dictionary = dictionary ?? [];
        var (prefix, suffix) = DictationInsertionProof.SharedEdges(valueBefore, valueAfter);
        _prefix = valueAfter[..prefix];
        _suffix = valueAfter[^suffix..];
        _oldWords = DictationInsertionProof.IsExactContiguousChange(valueBefore, valueAfter, inserted)
            ? Words(valueAfter[prefix..^suffix]) : []; // not a clean insert: nothing to compare against
        _lastValue = valueAfter;
        _done = _oldWords.Length == 0;
    }

    /// <summary>The learned word the first time it qualifies; null for every other call.</summary>
    public string? Observe(string currentValue)
    {
        if (_done || currentValue is null) return null;
        var candidate = Candidate(currentValue);
        var stable = candidate is not null && currentValue == _lastValue;
        _lastValue = currentValue;
        if (!stable) return null;
        _done = true;
        return candidate;
    }

    private string? Candidate(string value)
    {
        if (value.Length < _prefix.Length + _suffix.Length
            || !value.StartsWith(_prefix, StringComparison.Ordinal) || !value.EndsWith(_suffix, StringComparison.Ordinal)) return null;
        var now = Words(value[_prefix.Length..^_suffix.Length]);
        if (now.Length != _oldWords.Length) return null;

        string? oldWord = null, newWord = null;
        for (var i = 0; i < now.Length; i++)
        {
            if (string.Equals(now[i], _oldWords[i], StringComparison.OrdinalIgnoreCase)) continue;
            if (newWord is not null) return null; // a second changed word
            (oldWord, newWord) = (_oldWords[i], now[i]);
        }
        if (newWord is null || oldWord is null) return null;
        if (newWord.Length < MinWordLength || newWord.Length > SettingsCatalog.MaxWordLength) return null;
        if (_dictionary.Contains(newWord, StringComparer.OrdinalIgnoreCase)) return null;

        var distance = Levenshtein.Distance(oldWord.ToLowerInvariant(), newWord.ToLowerInvariant());
        return 1.0 - (double)distance / Math.Max(oldWord.Length, newWord.Length) >= MinSimilarity ? newWord : null;
    }

    /// <summary>Runs of letters, digits and apostrophes.</summary>
    private static string[] Words(string text)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '\'' or '’');
            if (inWord && start < 0) start = i;
            else if (!inWord && start >= 0) { words.Add(text[start..i]); start = -1; }
        }
        return [.. words];
    }
}
