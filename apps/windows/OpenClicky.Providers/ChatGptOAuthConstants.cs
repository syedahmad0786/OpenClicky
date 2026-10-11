namespace OpenClicky.Providers;

/// <summary>
/// Official Sign in with ChatGPT (OSS token-sharing) endpoints and scopes.
/// See https://developers.openai.com/siwc/token-sharing-open-source/sign-in
/// </summary>
internal static class ChatGptOAuthConstants
{
    public const string AuthorizeUrl = "https://auth.openai.com/api/accounts/authorize";
    public const string TokenUrl = "https://auth.openai.com/api/accounts/oauth/token";
    public const string RevocationUrl = "https://auth.openai.com/api/accounts/oauth/revoke";
    public const string JwksUrl = "https://auth.openai.com/.well-known/jwks.json";
    public const string Issuer = "https://auth.openai.com";
    public const string Resource = "https://api.openai.com/v1";
    public const string DynamicClientId = "dynamic_agent_client";
    public const string AgentNameHint = "OpenClicky";
    public const string CallbackPath = "/auth/callback";

    public const string Scopes =
        "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";

    public const string PlanUsageScope = "chatgpt.tokens.use.direct";
}
