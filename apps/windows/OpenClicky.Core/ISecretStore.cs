namespace OpenClicky.Core;

/// <summary>OS secret store abstraction (Windows Credential Manager in the app host).</summary>
public interface ISecretStore
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

public static class SecretNames
{
    public const string OpenRouterApiKey = "openrouter-api-key";

    /// <summary>JSON <see cref="ChatGptCredential"/> blob (access/refresh/id tokens).</summary>
    public const string ChatGptOAuth = "chatgpt-oauth";
}
