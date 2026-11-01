namespace OpenClicky.Core;

/// <summary>
/// OS secret store abstraction. Windows host: Credential Manager for small secrets,
/// DPAPI file under %AppData%\OpenClicky\secrets\ when CredWrite cannot hold the blob
/// (ChatGPT OAuth JSON exceeds the 2560-byte CredMan limit).
/// </summary>
public interface ISecretStore
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

public static class SecretNames
{
    public const string OpenRouterApiKey = "openrouter-api-key";

    /// <summary>The user's OpenAI API key, used only for OpenAI voices (text to speech).</summary>
    public const string OpenAiApiKey = "openai-api-key";

    /// <summary>The user's Anthropic API key, for Claude as the model provider.</summary>
    public const string AnthropicApiKey = "anthropic-api-key";

    /// <summary>The user's xAI API key, for Grok as the model provider.</summary>
    public const string XaiApiKey = "xai-api-key";

    /// <summary>JSON <see cref="ChatGptCredential"/> blob (access/refresh/id tokens).</summary>
    public const string ChatGptOAuth = "chatgpt-oauth";
}
