namespace OpenClicky.Core;

public static class DictationInsertionProof
{
    public static bool IsExactContiguousChange(string? before, string? after, string inserted)
    {
        ArgumentNullException.ThrowIfNull(inserted);
        if (before is null || after is null || inserted.Length == 0 || before == after) return false;

        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;

        return after.AsSpan(prefix, after.Length - prefix - suffix).SequenceEqual(inserted);
    }
}
