using System.Text;
using System.Text.Json;

namespace OpenClicky.Core.Browser;

public sealed record BrowserConnection(string? BrowserId = null, string? ProfileDirectory = null, string? SelectedInstance = null, bool Registered = false);

/// <summary>The user's chosen browser/profile, kept at %LOCALAPPDATA%\OpenClicky\BrowserBridge\connection.json.</summary>
public static class BrowserConnectionStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "BrowserBridge", "connection.json");

    public static BrowserConnection Load(string path)
    {
        try { return JsonSerializer.Deserialize<BrowserConnection>(File.ReadAllText(path), Options) ?? new BrowserConnection(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new BrowserConnection(); }
    }

    public static void Save(string path, BrowserConnection value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
