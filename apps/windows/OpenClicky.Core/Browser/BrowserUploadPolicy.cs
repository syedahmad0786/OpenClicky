using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace OpenClicky.Core.Browser;

/// <summary>App-side checks on files an agent wants to upload to a website. A name heuristic, not a guarantee.</summary>
public static class BrowserUploadPolicy
{
    public const long MaxBytes = 100L * 1024 * 1024;
    private static readonly string[] SecretNames = [".env", ".netrc", ".git-credentials", ".npmrc", ".pypirc", ".pgpass", "id_rsa", "id_ed25519"];
    private static readonly string[] SecretExtensions = [".pem", ".key", ".p12", ".pfx", ".kdbx"];
    // A drive-rooted local path only: no UNC, \\?\ or \\.\ forms, and no ':' after the drive (alternate data streams).
    private static readonly Regex LocalPath = new(@"^[A-Za-z]:[\\/][^:]*\z", RegexOptions.CultureInvariant);

    /// <summary>Folders whose files are never uploaded: OpenClicky's own app data.</summary>
    public static IReadOnlyList<string> DefaultProtectedRoots { get; } =
        [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky")];

    /// <summary>Returns why the upload is refused, in one sentence, or null when every file may be sent.</summary>
    public static string? Validate(IReadOnlyList<string> paths, IReadOnlyList<string>? protectedRoots = null)
    {
        var roots = (protectedRoots ?? DefaultProtectedRoots).Where(r => r.Length > 0)
            .Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)) + Path.DirectorySeparatorChar).ToArray();
        if (paths.Count is < 1 or > 10) return "Upload 1 to 10 files at a time.";
        foreach (var path in paths)
        {
            if (path is null || !LocalPath.IsMatch(path) || !Path.IsPathFullyQualified(path))
                return $"Upload paths must be absolute local paths like C:\\folder\\file.pdf: {path}";
            var file = new FileInfo(Path.GetFullPath(path));
            var secret = $"{file.Name} looks like a secret or browser data, so it was not uploaded.";
            if (LooksSecret(file.FullName, roots)) return secret;
            if (!file.Exists || (file.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0) return $"{path} is not a file.";
            try
            {
                using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                // The handle's final path resolves symlinks and junctions anywhere in the path, and long names for 8.3 ones.
                var final = OperatingSystem.IsWindows() ? FinalPath(stream.SafeFileHandle) : file.ResolveLinkTarget(true)?.FullName;
                if (final is not null && LooksSecret(final, roots)) return secret;
                if (stream.Length > MaxBytes) return $"{file.Name} is over 100 MB.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"{file.Name} can't be read."; }
        }
        return null;
    }

    private static bool LooksSecret(string fullPath, IReadOnlyList<string> roots)
    {
        var name = Path.GetFileName(fullPath).ToLowerInvariant();
        if (SecretNames.Contains(name) || name.StartsWith(".env.", StringComparison.Ordinal)) return true;
        if (SecretExtensions.Contains(Path.GetExtension(name))) return true;
        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s.Equals("User Data", StringComparison.OrdinalIgnoreCase))) return true;
        return roots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows")]
    private static string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[1024];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length >= buffer.Length)
        {
            buffer = new char[length + 1];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        }
        if (length == 0 || length >= buffer.Length) throw new IOException("The file's final path could not be read.");
        var final = new string(buffer, 0, (int)length);
        return final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + final[8..]
            : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint size, uint flags);
}
