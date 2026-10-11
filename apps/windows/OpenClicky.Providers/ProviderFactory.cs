using OpenClicky.Core;

namespace OpenClicky.Providers;

public static class ProviderFactory
{
    public static string ResolveBaseUrl(AppSettings settings)
    {
        if (settings.UseLocalProxy && !string.IsNullOrWhiteSpace(settings.LocalProxyBaseUrl))
        {
            var proxy = settings.LocalProxyBaseUrl.Trim().TrimEnd('/');
            // Local proxy exposes OpenAI-compatible paths at root; append /v1 if missing.
            return proxy.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? proxy : $"{proxy}/v1";
        }

        return "https://openrouter.ai/api/v1";
    }

    public static bool IsChatGptSignedIn(ISecretStore secrets)
    {
        var cred = ChatGptCredentialStore.Load(secrets);
        return cred?.HasUsableSession == true;
    }

    public static ITalkClient? CreateTalkClient(AppSettings settings, ISecretStore? secrets = null)
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
            return new ChatGptTalkClient(ct => oauth.GetAccessTokenAsync(settings, secrets, ct));
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
        // ChatGPT plan usage is Responses API only — mic STT needs OpenRouter or a debug transcript.
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

    public static IAgentPlanner? CreateAgentPlanner(AppSettings settings, ISecretStore? secrets = null)
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
            return new ChatGptAgentPlanner(ct => oauth.GetAccessTokenAsync(settings, secrets, ct));
        }

        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            return null;
        }

        return new OpenRouterAgentPlanner(settings.OpenRouterApiKey!, ResolveBaseUrl(settings));
    }
}
