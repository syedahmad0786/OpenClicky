namespace OpenClicky.Core;

public static class DictationInsertionProof
{
    public static bool IsExactContiguousChange(string? before, string? after, string inserted)
    {
        ArgumentNullException.ThrowIfNull(inserted);
        if (before is null || after is null || inserted.Length == 0 || before == after) return false;

        var (prefix, suffix) = SharedEdges(before, after);
        return after.AsSpan(prefix, after.Length - prefix - suffix).SequenceEqual(inserted);
    }

    /// <summary>The lengths of the prefix and suffix that <paramref name="before"/> and <paramref name="after"/> share (never overlapping in either).</summary>
    public static (int Prefix, int Suffix) SharedEdges(string before, string after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        return (prefix, suffix);
    }
}
