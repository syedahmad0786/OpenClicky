namespace OpenClicky.Core.Browser;

/// <summary>App-side checks on files an agent wants to upload to a website. A name heuristic, not a guarantee.</summary>
public static class BrowserUploadPolicy
{
    public const long MaxBytes = 100L * 1024 * 1024;
    private static readonly string[] SecretNames = [".env", ".netrc", ".git-credentials", ".npmrc", ".pypirc", ".pgpass", "id_rsa", "id_ed25519"];
    private static readonly string[] SecretExtensions = [".pem", ".key", ".p12", ".pfx", ".kdbx"];

    /// <summary>Returns why the upload is refused, in one sentence, or null when every file may be sent.</summary>
    public static string? Validate(IReadOnlyList<string> paths)
    {
        if (paths.Count is < 1 or > 10) return "Upload 1 to 10 files at a time.";
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return $"Upload paths must be absolute: {path}";
            var file = new FileInfo(Path.GetFullPath(path));
            var target = file.Exists && file.LinkTarget is not null ? file.ResolveLinkTarget(true) as FileInfo ?? file : file;
            if (LooksSecret(file.FullName) || LooksSecret(target.FullName)) return $"{file.Name} looks like a secret or browser data, so it was not uploaded.";
            if (!target.Exists || (target.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0) return $"{path} is not a file.";
            if (target.Length > MaxBytes) return $"{file.Name} is over 100 MB.";
            try { using (target.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { } }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"{file.Name} can't be read."; }
        }
        return null;
    }

    private static bool LooksSecret(string fullPath)
    {
        var name = Path.GetFileName(fullPath).ToLowerInvariant();
        if (SecretNames.Contains(name) || name.StartsWith(".env.", StringComparison.Ordinal)) return true;
        if (SecretExtensions.Contains(Path.GetExtension(name))) return true;
        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s.Equals("User Data", StringComparison.OrdinalIgnoreCase))) return true;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky") + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(appData, StringComparison.OrdinalIgnoreCase);
    }
}
