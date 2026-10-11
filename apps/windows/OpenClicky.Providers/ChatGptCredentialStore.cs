using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

public static class ChatGptCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static ChatGptCredential? Load(ISecretStore secrets)
    {
        var raw = secrets.Get(SecretNames.ChatGptOAuth);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChatGptCredential>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(ISecretStore secrets, ChatGptCredential credential)
    {
        credential.SavedAt = DateTimeOffset.UtcNow;
        secrets.Set(SecretNames.ChatGptOAuth, JsonSerializer.Serialize(credential, JsonOptions));
    }

    public static void Delete(ISecretStore secrets)
    {
        secrets.Delete(SecretNames.ChatGptOAuth);
    }
}
