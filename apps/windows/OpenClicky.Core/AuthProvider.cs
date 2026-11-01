namespace OpenClicky.Core;

/// <summary>Which cloud auth Talk/Agent use. OpenRouter, Anthropic and xAI use the user's own API key.</summary>
public enum AuthProvider
{
    OpenRouter = 0,
    ChatGPT = 1,
    /// <summary>Claude, with the user's own Anthropic API key (console.anthropic.com).</summary>
    Anthropic = 2,
    /// <summary>Grok, with the user's own xAI API key (console.x.ai).</summary>
    Xai = 3,
}
