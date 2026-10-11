using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClicky.Core;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenClicky");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Persists non-secret settings. API keys must not be written here —
    /// use <see cref="ISecretStore"/> (Credential Manager) instead.
    /// </summary>
    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        var disk = CloneWithoutSecrets(settings);
        var json = JsonSerializer.Serialize(disk, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static AppSettings CloneWithoutSecrets(AppSettings settings) =>
        new()
        {
            PushToTalk = settings.PushToTalk,
            LocalProxyBaseUrl = settings.LocalProxyBaseUrl,
            UseLocalProxy = settings.UseLocalProxy,
            OpenRouterApiKey = null,
            OpenRouterModel = settings.OpenRouterModel,
            TranscriptionModel = settings.TranscriptionModel,
            DebugTranscriptOverride = settings.DebugTranscriptOverride,
            PersistConversationHistory = settings.PersistConversationHistory,
            ForceGdiCapture = settings.ForceGdiCapture,
            AgentDenyListExtra = settings.AgentDenyListExtra,
            FirstRunCompleted = settings.FirstRunCompleted,
        };
}
