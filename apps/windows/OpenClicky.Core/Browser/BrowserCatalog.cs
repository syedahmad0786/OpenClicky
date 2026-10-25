using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace OpenClicky.Core.Browser;

public sealed record BrowserProfile(string Directory, string Name);

/// <summary>InstallDir/UserDataDir are relative to Program Files (x86) and LocalAppData; VendorKey is under HKCU.</summary>
public sealed record BrowserKind(string Id, string Name, string ExeName, string InstallDir, string UserDataDir, string VendorKey);

public sealed record DetectedBrowser(BrowserKind Kind, string ExePath, string UserDataPath, IReadOnlyList<BrowserProfile> Profiles);

public interface IBrowserProbe
{
    string LocalAppData { get; }
    string ProgramFiles { get; }
    string ProgramFilesX86 { get; }
    bool FileExists(string path);
    string? ReadText(string path);
    string? AppPath(string exeName);
}

/// <summary>Real probe: file system plus the App Paths registry entry (HKCU first, then HKLM).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrowserProbe : IBrowserProbe
{
    public string LocalAppData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string ProgramFiles { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public string ProgramFilesX86 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    public bool FileExists(string path) => File.Exists(path);

    public string? ReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public string? AppPath(string exeName)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeName);
                if (key?.GetValue("") is string s && !string.IsNullOrWhiteSpace(s)) return s.Trim('"');
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return null;
    }
}

public static class BrowserCatalog
{
    public static IReadOnlyList<BrowserKind> Known { get; } =
    [
        new("chrome", "Google Chrome", "chrome.exe", @"Google\Chrome\Application", @"Google\Chrome\User Data", @"Software\Google\Chrome"),
        new("chrome-beta", "Chrome Beta", "chrome.exe", @"Google\Chrome Beta\Application", @"Google\Chrome Beta\User Data", @"Software\Google\Chrome"),
        new("chrome-dev", "Chrome Dev", "chrome.exe", @"Google\Chrome Dev\Application", @"Google\Chrome Dev\User Data", @"Software\Google\Chrome"),
        new("chrome-canary", "Chrome Canary", "chrome.exe", @"Google\Chrome SxS\Application", @"Google\Chrome SxS\User Data", @"Software\Google\Chrome"),
        new("chromium", "Chromium", "chrome.exe", @"Chromium\Application", @"Chromium\User Data", @"Software\Chromium"),
        new("edge", "Microsoft Edge", "msedge.exe", @"Microsoft\Edge\Application", @"Microsoft\Edge\User Data", @"Software\Microsoft\Edge"),
        new("brave", "Brave", "brave.exe", @"BraveSoftware\Brave-Browser\Application", @"BraveSoftware\Brave-Browser\User Data", @"Software\BraveSoftware\Brave-Browser"),
        new("vivaldi", "Vivaldi", "vivaldi.exe", @"Vivaldi\Application", @"Vivaldi\User Data", @"Software\Vivaldi"),
    ];

    public static IReadOnlyList<DetectedBrowser> Detect(IBrowserProbe probe)
    {
        var found = new List<DetectedBrowser>();
        foreach (var kind in Known)
        {
            var exe = FindExe(probe, kind);
            if (exe is null) continue;
            var userData = Path.Combine(probe.LocalAppData, kind.UserDataDir);
            found.Add(new DetectedBrowser(kind, exe, userData, ParseProfiles(probe.ReadText(Path.Combine(userData, "Local State")))));
        }
        return found;
    }

    private static string? FindExe(IBrowserProbe probe, BrowserKind kind)
    {
        foreach (var root in new[] { probe.ProgramFiles, probe.ProgramFilesX86, probe.LocalAppData })
        {
            var candidate = Path.Combine(root, kind.InstallDir, kind.ExeName);
            if (probe.FileExists(candidate)) return candidate;
        }
        // App Paths covers installs outside the usual roots; trust it only when its folder is this browser's install dir
        // (chrome.exe is shared by Chrome, Beta, Dev, Canary and Chromium).
        var app = probe.AppPath(kind.ExeName);
        var dir = app is null ? null : Path.GetDirectoryName(app);
        if (dir is not null && dir.EndsWith(kind.InstallDir, StringComparison.OrdinalIgnoreCase) && probe.FileExists(app!))
            return app;
        return null;
    }

    public static IReadOnlyList<BrowserProfile> ParseProfiles(string? localStateJson)
    {
        BrowserProfile[] fallback = [new("Default", "Default")];
        if (string.IsNullOrWhiteSpace(localStateJson)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(localStateJson);
            if (!doc.RootElement.TryGetProperty("profile", out var profile) ||
                !profile.TryGetProperty("info_cache", out var cache) ||
                cache.ValueKind != JsonValueKind.Object)
                return fallback;
            var list = new List<BrowserProfile>();
            foreach (var p in cache.EnumerateObject())
            {
                if (p.Name is "System Profile" or "Guest Profile") continue;
                var name = p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("name", out var n) &&
                           n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())
                    ? n.GetString()! : p.Name;
                list.Add(new BrowserProfile(p.Name, name));
            }
            if (list.Count == 0) return fallback;
            return list.OrderBy(p => p.Directory == "Default" ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Directory, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException) { return fallback; }
    }
}
