using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace OpenClicky.Providers;

/// <summary>
/// Per-user (HKCU) native-messaging registration for the browser bridge, plus the unpacked-extension copy.
/// Nothing here touches Windows security settings; it only writes our own host key and files under %LOCALAPPDATA%\OpenClicky.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BrowserNativeHostRegistrar
{
    public const string HostName = "com.openclicky.chrome_bridge";
    public const string ExtensionId = "demdidhcdmmfbpidanflhknhplklkcja";
    public const string ExtensionOrigin = "chrome-extension://" + ExtensionId + "/";

    private static string OpenClickyData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky");
    public static string DefaultBridgeFolder => Path.Combine(OpenClickyData, "BrowserBridge");
    public static string DefaultExtensionFolder => Path.Combine(OpenClickyData, "BrowserExtension");

    private static string HostKey(string vendorKey) => vendorKey + @"\NativeMessagingHosts\" + HostName;

    public static string WriteManifest(string folder, string hostExePath)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, HostName + ".json");
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = HostName,
            ["description"] = "OpenClicky browser bridge",
            ["path"] = hostExePath,
            ["type"] = "stdio",
            ["allowed_origins"] = new[] { ExtensionOrigin },
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    public static void Register(RegistryKey root, string vendorKey, string manifestPath)
    {
        using var key = root.CreateSubKey(HostKey(vendorKey));
        key.SetValue("", manifestPath);
    }

    /// <summary>Deletes only our host key; other native hosts under the same vendor key stay.</summary>
    public static void Unregister(RegistryKey root, string vendorKey) => root.DeleteSubKey(HostKey(vendorKey), false);

    public static bool IsRegistered(RegistryKey root, string vendorKey, string manifestPath)
    {
        using var key = root.OpenSubKey(HostKey(vendorKey));
        return key?.GetValue("") is string s && string.Equals(s, manifestPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when our host manifest is missing, unreadable or names another exe (an update or a move).</summary>
    public static bool ManifestDiffers(string folder, string hostExePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, HostName + ".json")));
            return !(doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("path", out var path)
                && path.ValueKind == JsonValueKind.String && string.Equals(path.GetString(), hostExePath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return true; }
    }

    /// <summary>Copies manifest.json, *.js, *.mjs, *.html and icons\* (never test\ or README); removes stale files of those kinds.</summary>
    public static string InstallExtension(string sourceFolder, string targetFolder)
    {
        // Fail before touching the target: stale-file removal below would otherwise wipe a loaded extension folder.
        var manifest = Path.Combine(sourceFolder, "manifest.json");
        if (!File.Exists(manifest)) throw new FileNotFoundException("Extension source has no manifest.json.", manifest);
        var plan = Plan(sourceFolder, targetFolder);
        foreach (var (from, to) in plan)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, true);
        }
        foreach (var f in Stale(targetFolder, plan)) File.Delete(f);
        return targetFolder;
    }

    /// <summary>True when <see cref="InstallExtension"/> would change the target: a file to copy is missing or different,
    /// or a stale file would be removed. False when the source has no manifest (nothing to install).</summary>
    public static bool ExtensionDiffers(string sourceFolder, string targetFolder)
    {
        if (!File.Exists(Path.Combine(sourceFolder, "manifest.json"))) return false;
        var plan = Plan(sourceFolder, targetFolder);
        return plan.Any(p => !File.Exists(p.To) || !File.ReadAllBytes(p.From).AsSpan().SequenceEqual(File.ReadAllBytes(p.To)))
            || Stale(targetFolder, plan).Any();
    }

    private static readonly string[] CodeExtensions = [".js", ".mjs", ".html"];

    // Filter by exact extension: Windows wildcard matching lets "*.mjs" also match ".mjsx".
    private static string[] CodeFiles(string dir) => Directory.Exists(dir)
        ? Directory.GetFiles(dir).Where(f => CodeExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToArray()
        : [];

    private static string[] AllFiles(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir) : [];

    private static List<(string From, string To)> Plan(string source, string target)
    {
        var plan = new List<(string From, string To)> { (Path.Combine(source, "manifest.json"), Path.Combine(target, "manifest.json")) };
        plan.AddRange(CodeFiles(source).Select(f => (f, Path.Combine(target, Path.GetFileName(f)))));
        plan.AddRange(AllFiles(Path.Combine(source, "icons")).Select(f => (f, Path.Combine(target, "icons", Path.GetFileName(f)))));
        return plan;
    }

    private static List<string> Stale(string target, List<(string From, string To)> plan)
    {
        var wanted = new HashSet<string>(plan.Select(p => Path.GetFullPath(p.To)), StringComparer.OrdinalIgnoreCase);
        return CodeFiles(target).Concat(AllFiles(Path.Combine(target, "icons"))).Where(f => !wanted.Contains(Path.GetFullPath(f))).ToList();
    }
}
