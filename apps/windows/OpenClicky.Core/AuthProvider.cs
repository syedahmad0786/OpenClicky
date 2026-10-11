namespace OpenClicky.Core;

/// <summary>Which cloud auth Talk/Agent use. OpenRouter BYO key remains available either way.</summary>
public enum AuthProvider
{
    OpenRouter = 0,
    ChatGPT = 1,
}
