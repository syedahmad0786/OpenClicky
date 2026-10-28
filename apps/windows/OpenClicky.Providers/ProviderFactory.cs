using OpenClicky.Core;

namespace OpenClicky.Providers;

public static class ProviderFactory
{
    public static string ResolveBaseUrl(AppSettings settings)
    {
        if (settings.UseLocalProxy)
        {
            var address = settings.LocalProxyBaseUrl?.Trim();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || !uri.IsLoopback || uri.Scheme is not ("http" or "https")
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("The local proxy must use a localhost or loopback HTTP address, without credentials, query, or fragment.");
            var proxy = address!.TrimEnd('/');
            // Local proxy exposes OpenAI-compatible paths at root; append /v1 if missing.
            return proxy.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? proxy : $"{proxy}/v1";
        }

        return "https://openrouter.ai/api/v1";
    }

    public const string XaiBaseUrl = "https://api.x.ai/v1";

    /// <summary>The saved Anthropic or xAI key for the selected provider; null for other providers or when none is saved.</summary>
    public static string? ApiKeyFor(AuthProvider provider, ISecretStore? secrets)
    {
        var name = provider switch
        {
            AuthProvider.Anthropic => SecretNames.AnthropicApiKey,
            AuthProvider.Xai => SecretNames.XaiApiKey,
            _ => null,
        };
        return name is null || secrets?.Get(name) is not { } key || string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public static bool IsChatGptSignedIn(ISecretStore secrets)
    {
        var cred = ChatGptCredentialStore.Load(secrets);
        return cred?.HasUsableSession == true;
    }

    public static ITalkClient? CreateTalkClient(AppSettings settings, ISecretStore? secrets = null, Action<string>? log = null)
    {
        // Opt-in offline smoke: canned [POINT:…] reply, no network / no API keys.
        if (OfflineTalkClient.IsEnvEnabled())
        {
            return new OfflineTalkClient();
        }

        if (settings.AuthProvider == AuthProvider.ChatGPT)
        {
            if (secrets is null || !IsChatGptSignedIn(secrets))
            {
                return null;
            }

            var oauth = new ChatGptOAuthService();
            return new ChatGptTalkClient(ct => oauth.GetAccessTokenAsync(settings, secrets, ct), log: log);
        }

        if (settings.AuthProvider is AuthProvider.Anthropic or AuthProvider.Xai)
        {
            return ApiKeyFor(settings.AuthProvider, secrets) is not { } key ? null
                : settings.AuthProvider == AuthProvider.Anthropic ? new AnthropicTalkClient(key)
                : new OpenRouterTalkClient(key, XaiBaseUrl, provider: "xAI");
        }

        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            return null;
        }

        return new OpenRouterTalkClient(settings.OpenRouterApiKey!, ResolveBaseUrl(settings));
    }

    public static ISttClient? CreateSttClient(AppSettings settings, ISecretStore? secrets = null)
    {
        // STT stays on OpenRouter (Whisper-compatible) when a BYO key is present.
        // ChatGPT plan usage excludes audio; local Windows recognition is the app default.
        _ = secrets;
        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            return null;
        }

        return new OpenRouterSttClient(
            settings.OpenRouterApiKey!,
            settings.TranscriptionModel,
            ResolveBaseUrl(settings));
    }

    public static IAgentPlanner? CreateAgentPlanner(AppSettings settings, ISecretStore? secrets = null, Action<string>? log = null)
    {
        // Opt-in offline smoke: fixed Notepad + type plan, no network / no API keys.
        // Confirmations still use WinFormsAgentConfirm in the tray path.
        if (OfflineAgentPlanner.IsEnvEnabled())
        {
            return new OfflineAgentPlanner();
        }

        if (settings.AuthProvider == AuthProvider.ChatGPT)
        {
            if (secrets is null || !IsChatGptSignedIn(secrets))
            {
                return null;
            }

            var oauth = new ChatGptOAuthService();
            return new ChatGptAgentPlanner(ct => oauth.GetAccessTokenAsync(settings, secrets, ct), log: log);
        }

        if (settings.AuthProvider is AuthProvider.Anthropic or AuthProvider.Xai)
        {
            return ApiKeyFor(settings.AuthProvider, secrets) is not { } key ? null
                : settings.AuthProvider == AuthProvider.Anthropic ? new AnthropicAgentPlanner(key)
                : new OpenRouterAgentPlanner(key, XaiBaseUrl, provider: "xAI");
        }

        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            return null;
        }

        return new OpenRouterAgentPlanner(settings.OpenRouterApiKey!, ResolveBaseUrl(settings));
    }
}
