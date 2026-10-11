using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
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
        CancellationToken cancellationToken = default)
    {
        var jwksUri = await ResolveJwksUriAsync(cancellationToken).ConfigureAwait(false);
        var jwksJson = await Http.GetStringAsync(jwksUri, cancellationToken).ConfigureAwait(false);
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
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(idToken, parameters, out _);
        var jwt = handler.ReadJwtToken(idToken);
        var nonce = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ID token nonce mismatch.");
        }

        var sub = principal.FindFirst("sub")?.Value
                  ?? jwt.Subject
                  ?? throw new InvalidOperationException("ID token missing sub.");
        var email = principal.FindFirst("email")?.Value
                    ?? jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

        return new ValidatedIdToken(sub, email);
    }

    /// <summary>Fallback claim peek when JWKS is unreachable (still requires nonce/iss/aud checks).</summary>
    public static ValidatedIdToken ValidateClaimsOnly(
        string idToken,
        string expectedAudienceClientId,
        string expectedNonce)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("Malformed ID token.");
        }

        var json = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var iss = root.GetProperty("iss").GetString();
        var aud = root.TryGetProperty("aud", out var audEl)
            ? audEl.ValueKind == JsonValueKind.Array
                ? audEl.EnumerateArray().Select(e => e.GetString()).FirstOrDefault()
                : audEl.GetString()
            : null;
        var nonce = root.TryGetProperty("nonce", out var n) ? n.GetString() : null;
        var exp = root.TryGetProperty("exp", out var expEl) ? expEl.GetInt64() : 0L;
        var sub = root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
        var email = root.TryGetProperty("email", out var em) ? em.GetString() : null;

        if (!string.Equals(iss, ChatGptOAuthConstants.Issuer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected ID token issuer '{iss}'.");
        }

        if (!string.Equals(aud, expectedAudienceClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ID token audience mismatch.");
        }

        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ID token nonce mismatch.");
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp + 120)
        {
            throw new InvalidOperationException("ID token expired.");
        }

        if (string.IsNullOrWhiteSpace(sub))
        {
            throw new InvalidOperationException("ID token missing sub.");
        }

        return new ValidatedIdToken(sub!, email);
    }

    private static async Task<string> ResolveJwksUriAsync(CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await Http.GetStringAsync(
                "https://auth.openai.com/.well-known/openid-configuration",
                cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(discovery);
            if (doc.RootElement.TryGetProperty("jwks_uri", out var uri)
                && uri.GetString() is { Length: > 0 } jwks)
            {
                return jwks;
            }
        }
        catch
        {
            // fall through
        }

        return ChatGptOAuthConstants.JwksUrl;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}

public sealed record ValidatedIdToken(string Subject, string? Email);
