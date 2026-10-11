namespace OpenClicky.Core;

/// <summary>
/// Local ChatGPT OAuth credential record (Sign in with ChatGPT OSS flow).
/// Tokens live in the OS secret store (Credential Manager or DPAPI file); never write them to settings.json.
/// </summary>
public sealed class ChatGptCredential
{
    public string? Email { get; set; }
    public string Issuer { get; set; } = "https://auth.openai.com";
    public string? Subject { get; set; }
    public string? ClientId { get; set; }
    public string? ExtAgentHostId { get; set; }
    public string? IdToken { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public List<string> Scopes { get; set; } = new();
    public DateTimeOffset? SavedAt { get; set; }

    public bool HasUsableSession =>
        !string.IsNullOrWhiteSpace(AccessToken)
        && !string.IsNullOrWhiteSpace(ClientId)
        && Scopes.Any(s => s.Equals("chatgpt.tokens.use.direct", StringComparison.OrdinalIgnoreCase));

    public bool AccessTokenNearExpiry(TimeSpan skew) =>
        AccessTokenExpiresAt is { } exp && DateTimeOffset.UtcNow >= exp - skew;
}
