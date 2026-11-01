namespace OpenClicky.Core.Work;

/// <summary>Carries a file to a new home once: copied only when the new file is missing, the old one is never moved, changed or deleted.</summary>
public static class FileCarryOver
{
    /// <summary>Copies <paramref name="previous"/> to <paramref name="next"/> when it exists and <paramref name="next"/> does not.
    /// Returns true when a copy was made; a missing source or an unreadable or unwritable path returns false and never throws.</summary>
    public static bool CopyIfMissing(string? previous, string next)
    {
        if (string.IsNullOrWhiteSpace(previous)) return false;
        try
        {
            if (string.Equals(Path.GetFullPath(previous), Path.GetFullPath(next), StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(previous) || File.Exists(next)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(next))!);
            File.Copy(previous, next, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
