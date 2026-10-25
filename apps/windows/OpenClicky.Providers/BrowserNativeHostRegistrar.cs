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

    /// <summary>Copies manifest.json, *.js, *.mjs, *.html and icons\* (never test\ or README); removes stale files of those kinds.</summary>
    public static string InstallExtension(string sourceFolder, string targetFolder)
    {
        // Fail before touching the target: stale-file removal below would otherwise wipe a loaded extension folder.
        var manifest = Path.Combine(sourceFolder, "manifest.json");
        if (!File.Exists(manifest)) throw new FileNotFoundException("Extension source has no manifest.json.", manifest);
        Directory.CreateDirectory(targetFolder);
        var code = new[] { ".js", ".mjs", ".html" };
        // Filter by exact extension: Windows wildcard matching lets "*.mjs" also match ".mjsx".
        string[] Files(string dir) => Directory.GetFiles(dir).Where(f => code.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToArray();
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Copy(string src, string destDir)
        {
            Directory.CreateDirectory(destDir);
            var name = Path.GetFileName(src);
            wanted.Add(Path.GetFullPath(Path.Combine(destDir, name)));
            File.Copy(src, Path.Combine(destDir, name), true);
        }

        Copy(manifest, targetFolder);
        foreach (var f in Files(sourceFolder)) Copy(f, targetFolder);
        var icons = Path.Combine(sourceFolder, "icons");
        if (Directory.Exists(icons))
            foreach (var f in Directory.GetFiles(icons)) Copy(f, Path.Combine(targetFolder, "icons"));

        foreach (var f in Files(targetFolder))
            if (!wanted.Contains(Path.GetFullPath(f))) File.Delete(f);
        var targetIcons = Path.Combine(targetFolder, "icons");
        if (Directory.Exists(targetIcons))
            foreach (var f in Directory.GetFiles(targetIcons))
                if (!wanted.Contains(Path.GetFullPath(f))) File.Delete(f);
        return targetFolder;
    }
}
