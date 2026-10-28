using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>Validates OpenAI ID tokens against published JWKS (Sign in with ChatGPT).</summary>
public static class ChatGptIdTokenValidator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<ValidatedIdToken> ValidateAsync(
        string idToken,
        string expectedAudienceClientId,
        string expectedNonce,
        CancellationToken cancellationToken = default,
        HttpClient? httpClient = null)
    {
        var http = httpClient ?? Http;
        var jwksJson = await http.GetStringAsync(ChatGptOAuthConstants.JwksUrl, cancellationToken).ConfigureAwait(false);
        var jwks = new JsonWebKeySet(jwksJson);
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = ChatGptOAuthConstants.Issuer,
            ValidAudience = expectedAudienceClientId,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(5),
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(idToken, parameters, out _);
        var jwt = handler.ReadJwtToken(idToken);
        var nonce = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ID token nonce mismatch.");
        }

        var sub = jwt.Subject;
        if (string.IsNullOrWhiteSpace(sub)) throw new InvalidOperationException("ID token missing sub.");
        var email = principal.FindFirst("email")?.Value
                    ?? jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

        return new ValidatedIdToken(sub, email);
    }

}

public sealed record ValidatedIdToken(string Subject, string? Email);
