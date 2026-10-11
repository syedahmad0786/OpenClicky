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

    public static ITalkClient? CreateTalkClient(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            return null;
        }

        return new OpenRouterTalkClient(settings.OpenRouterApiKey!, ResolveBaseUrl(settings));
    }

    public static ISttClient? CreateSttClient(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            return null;
        }

        return new OpenRouterSttClient(
            settings.OpenRouterApiKey!,
            settings.TranscriptionModel,
            ResolveBaseUrl(settings));
    }
}
