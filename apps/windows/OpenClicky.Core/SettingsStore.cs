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
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
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
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.PushToTalk = HotkeyChord.NormalizeOrDefault(settings.PushToTalk);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Persists non-secret settings. API keys / OAuth tokens must not be written here —
    /// use <see cref="ISecretStore"/> (Credential Manager or DPAPI) instead.
    /// </summary>
    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        settings.PushToTalk = HotkeyChord.NormalizeOrDefault(settings.PushToTalk);
        var disk = CloneWithoutSecrets(settings);
        var json = JsonSerializer.Serialize(disk, JsonOptions);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static AppSettings CloneWithoutSecrets(AppSettings settings) =>
        new()
        {
            PushToTalk = settings.PushToTalk,
            LocalProxyBaseUrl = settings.LocalProxyBaseUrl,
            UseLocalProxy = settings.UseLocalProxy,
            AuthProvider = settings.AuthProvider,
            OpenRouterApiKey = null,
            OpenRouterModel = settings.OpenRouterModel,
            TranscriptionModel = settings.TranscriptionModel,
            UseWindowsSpeech = settings.UseWindowsSpeech,
            SpeakReplies = settings.SpeakReplies,
            ChatGptHostId = settings.ChatGptHostId,
            ChatGptIssuedClientId = settings.ChatGptIssuedClientId,
            ChatGptAccountEmail = settings.ChatGptAccountEmail,
            ChatGptModel = settings.ChatGptModel,
            ChatGptPlanWelcomeShown = settings.ChatGptPlanWelcomeShown,
            DebugTranscriptOverride = settings.DebugTranscriptOverride,
            PersistConversationHistory = settings.PersistConversationHistory,
            ForceGdiCapture = settings.ForceGdiCapture,
            AgentDenyListExtra = settings.AgentDenyListExtra,
            FirstRunCompleted = settings.FirstRunCompleted,
        };
}
