using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// Official Sign in with ChatGPT OSS flow: Authorization Code + PKCE via system browser
/// and a 127.0.0.1 loopback callback (no client secret, no scraped cookies).
/// Docs: https://developers.openai.com/siwc/token-sharing-open-source/sign-in
/// </summary>
public sealed class ChatGptOAuthService
{
    private readonly HttpClient _http;
    private readonly Action<string> _log;

    public ChatGptOAuthService(Action<string>? log = null, HttpClient? httpClient = null)
    {
        _log = log ?? (_ => { });
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    public async Task<ChatGptCredential> SignInAsync(
        AppSettings settings,
        ISecretStore secrets,
        CancellationToken cancellationToken = default)
    {
        settings.EnsureChatGptHostId();
        var existing = ChatGptCredentialStore.Load(secrets);
        var isNewRegistration = string.IsNullOrWhiteSpace(settings.ChatGptIssuedClientId);
        var clientIdForAuth = isNewRegistration
            ? ChatGptOAuthConstants.DynamicClientId
            : settings.ChatGptIssuedClientId!;

        var state = Pkce.RandomUrlToken();
        var nonce = Pkce.RandomUrlToken();
        var verifier = Pkce.CreateVerifier();
        var challenge = Pkce.ChallengeS256(verifier);

        using var listener = CreateLoopbackListener(out var redirectUri);
        _log($"chatgpt_oauth_listen={redirectUri}");

        var authUrl = BuildAuthorizeUrl(
            clientIdForAuth,
            redirectUri,
            state,
            nonce,
            challenge,
            settings.ChatGptHostId!,
            isNewRegistration,
            existing?.IdToken,
            settings.ChatGptAccountEmail);

        OpenBrowser(authUrl);
        _log("chatgpt_oauth_browser_opened");

        var callback = await WaitForCallbackAsync(listener, state, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(callback.Error))
        {
            throw new InvalidOperationException(
                $"Sign in with ChatGPT denied or failed: {callback.Error} {callback.ErrorDescription}".Trim());
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            throw new InvalidOperationException("Sign in with ChatGPT callback missing authorization code.");
        }

        var issuedClientId = callback.ClientId ?? settings.ChatGptIssuedClientId;
        if (isNewRegistration)
        {
            if (string.IsNullOrWhiteSpace(issuedClientId)
                || issuedClientId.Equals(ChatGptOAuthConstants.DynamicClientId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Dynamic registration incomplete — callback did not return an issued client_id.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(callback.ClientId)
                 && !string.Equals(callback.ClientId, settings.ChatGptIssuedClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Callback client_id does not match the saved ChatGPT registration.");
        }

        issuedClientId ??= settings.ChatGptIssuedClientId
                           ?? throw new InvalidOperationException("Missing issued ChatGPT client_id.");

        var token = await ExchangeCodeAsync(
            issuedClientId,
            callback.Code!,
            verifier,
            redirectUri,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token.IdToken))
        {
            throw new InvalidOperationException("Token response missing id_token.");
        }

        ValidatedIdToken identity;
        try
        {
            identity = await ChatGptIdTokenValidator.ValidateAsync(
                token.IdToken!,
                issuedClientId,
                nonce,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log($"chatgpt_id_token_jwks_fallback={ex.Message}");
            identity = ChatGptIdTokenValidator.ValidateClaimsOnly(token.IdToken!, issuedClientId, nonce);
        }

        var scopes = SplitScopes(token.Scope ?? callback.Scope);
        if (!scopes.Any(s => s.Equals(ChatGptOAuthConstants.PlanUsageScope, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "ChatGPT plan usage was not granted (missing chatgpt.tokens.use.direct). " +
                "Enable plan sharing for this app in ChatGPT settings and try again.");
        }

        var credential = new ChatGptCredential
        {
            Email = identity.Email ?? settings.ChatGptAccountEmail,
            Issuer = ChatGptOAuthConstants.Issuer,
            Subject = identity.Subject,
            ClientId = issuedClientId,
            ExtAgentHostId = settings.ChatGptHostId,
            IdToken = token.IdToken,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            TokenType = token.TokenType ?? "Bearer",
            ExpiresIn = token.ExpiresIn,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn)),
            Scopes = scopes,
            SavedAt = DateTimeOffset.UtcNow,
        };

        ChatGptCredentialStore.Save(secrets, credential);
        settings.ChatGptIssuedClientId = issuedClientId;
        settings.ChatGptAccountEmail = credential.Email;
        settings.AuthProvider = AuthProvider.ChatGPT;
        SettingsStore.Save(settings);
        _log($"chatgpt_signed_in email={credential.Email ?? "(unknown)"} client_id={issuedClientId}");
        return credential;
    }

    public async Task SignOutAsync(AppSettings settings, ISecretStore secrets, CancellationToken cancellationToken = default)
    {
        var credential = ChatGptCredentialStore.Load(secrets);
        if (credential is { RefreshToken: { Length: > 0 }, ClientId: { Length: > 0 } })
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = credential.RefreshToken!,
                    ["token_type_hint"] = "refresh_token",
                    ["client_id"] = credential.ClientId!,
                });
                var revokeUrl = await ResolveRevocationEndpointAsync(cancellationToken).ConfigureAwait(false);
                using var response = await _http.PostAsync(revokeUrl, content, cancellationToken)
                    .ConfigureAwait(false);
                _log($"chatgpt_revoke status={(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                _log($"chatgpt_revoke_error={ex.Message}");
            }
        }

        ChatGptCredentialStore.Delete(secrets);
        settings.ChatGptAccountEmail = null;
        // Keep ChatGptIssuedClientId + ChatGptHostId for reauthorization.
        if (settings.AuthProvider == AuthProvider.ChatGPT)
        {
            settings.AuthProvider = AuthProvider.OpenRouter;
        }

        SettingsStore.Save(settings);
        _log("chatgpt_signed_out");
    }

    public async Task<string> GetAccessTokenAsync(
        AppSettings settings,
        ISecretStore secrets,
        CancellationToken cancellationToken = default)
    {
        var credential = ChatGptCredentialStore.Load(secrets)
                         ?? throw new InvalidOperationException("Not signed in with ChatGPT.");

        if (!credential.HasUsableSession)
        {
            throw new InvalidOperationException("ChatGPT session missing plan-usage scopes. Sign in again.");
        }

        if (!credential.AccessTokenNearExpiry(TimeSpan.FromMinutes(2))
            && !string.IsNullOrWhiteSpace(credential.AccessToken))
        {
            return credential.AccessToken!;
        }

        if (string.IsNullOrWhiteSpace(credential.RefreshToken) || string.IsNullOrWhiteSpace(credential.ClientId))
        {
            throw new InvalidOperationException("ChatGPT refresh token missing — sign in again.");
        }

        var refreshed = await RefreshAsync(credential.ClientId!, credential.RefreshToken!, cancellationToken)
            .ConfigureAwait(false);
        credential.AccessToken = refreshed.AccessToken;
        credential.RefreshToken = refreshed.RefreshToken ?? credential.RefreshToken;
        credential.IdToken = refreshed.IdToken ?? credential.IdToken;
        credential.ExpiresIn = refreshed.ExpiresIn;
        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn));
        if (!string.IsNullOrWhiteSpace(refreshed.Scope))
        {
            credential.Scopes = SplitScopes(refreshed.Scope);
        }

        ChatGptCredentialStore.Save(secrets, credential);
        settings.ChatGptAccountEmail = credential.Email ?? settings.ChatGptAccountEmail;
        return credential.AccessToken!;
    }

    private async Task<TokenResponse> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["resource"] = ChatGptOAuthConstants.Resource,
        });
        using var response = await _http.PostAsync(ChatGptOAuthConstants.TokenUrl, content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ChatGPT token refresh {(int)response.StatusCode}: {body}");
        }

        return ParseTokenResponse(body);
    }

    private async Task<TokenResponse> ExchangeCodeAsync(
        string clientId,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["resource"] = ChatGptOAuthConstants.Resource,
        });
        using var response = await _http.PostAsync(ChatGptOAuthConstants.TokenUrl, content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ChatGPT code exchange {(int)response.StatusCode}: {body}");
        }

        return ParseTokenResponse(body);
    }

    private static TokenResponse ParseTokenResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString()
                     ?? throw new InvalidOperationException("Token response missing access_token.");
        var idToken = root.TryGetProperty("id_token", out var id) ? id.GetString() : null;
        return new TokenResponse(
            access,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            idToken,
            root.TryGetProperty("token_type", out var tt) ? tt.GetString() : "Bearer",
            root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600,
            root.TryGetProperty("scope", out var sc) ? sc.GetString() : null);
    }

    private static string BuildAuthorizeUrl(
        string clientId,
        string redirectUri,
        string state,
        string nonce,
        string codeChallenge,
        string hostId,
        bool newRegistration,
        string? idTokenHint,
        string? loginHint)
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("response_type", "code"),
            new("redirect_uri", redirectUri),
            new("scope", ChatGptOAuthConstants.Scopes),
            new("resource", ChatGptOAuthConstants.Resource),
            new("state", state),
            new("nonce", nonce),
            new("code_challenge_method", "S256"),
            new("code_challenge", codeChallenge),
            new("ext_agent_host_id", hostId),
        };
        if (newRegistration)
        {
            pairs.Add(new("agent_name_hint", ChatGptOAuthConstants.AgentNameHint));
        }

        if (!string.IsNullOrWhiteSpace(idTokenHint))
        {
            pairs.Add(new("id_token_hint", idTokenHint));
        }

        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            pairs.Add(new("login_hint", loginHint));
        }

        var query = string.Join("&", pairs.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{ChatGptOAuthConstants.AuthorizeUrl}?{query}";
    }

    private static HttpListener CreateLoopbackListener(out string redirectUri)
    {
        // Bind an ephemeral 127.0.0.1 port; path must remain /auth/callback per OSS SIWC docs.
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var port = 17_000 + Random.Shared.Next(0, 2000);
            var prefix = $"http://127.0.0.1:{port}{ChatGptOAuthConstants.CallbackPath}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                redirectUri = $"http://127.0.0.1:{port}{ChatGptOAuthConstants.CallbackPath}";
                return listener;
            }
            catch
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Could not bind a loopback port for ChatGPT OAuth callback.");
    }

    private static async Task<CallbackResult> WaitForCallbackAsync(
        HttpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch
            {
                // ignored
            }
        });

        HttpListenerContext ctx;
        try
        {
            ctx = await listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var query = ctx.Request.QueryString;
        var state = query["state"];
        var html = "<html><body><p>OpenClicky sign-in complete. You can close this tab.</p></body></html>";
        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            html = "<html><body><p>OpenClicky sign-in failed: state mismatch.</p></body></html>";
            await WriteHtmlAsync(ctx.Response, 400, html).ConfigureAwait(false);
            throw new InvalidOperationException("OAuth state mismatch.");
        }

        await WriteHtmlAsync(ctx.Response, 200, html).ConfigureAwait(false);
        return new CallbackResult(
            query["code"],
            query["state"],
            query["client_id"],
            query["scope"],
            query["error"],
            query["error_description"]);
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, int status, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = status;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private async Task<string> ResolveRevocationEndpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await _http.GetStringAsync(
                "https://auth.openai.com/.well-known/openid-configuration",
                cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(discovery);
            if (doc.RootElement.TryGetProperty("revocation_endpoint", out var ep)
                && ep.GetString() is { Length: > 0 } url)
            {
                return url;
            }
        }
        catch
        {
            // fall through
        }

        return ChatGptOAuthConstants.RevocationUrl;
    }

    private static List<string> SplitScopes(string? scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? new List<string>()
            : scope.Split([' ', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    private sealed record CallbackResult(
        string? Code,
        string? State,
        string? ClientId,
        string? Scope,
        string? Error,
        string? ErrorDescription);

    private sealed record TokenResponse(
        string AccessToken,
        string? RefreshToken,
        string? IdToken,
        string? TokenType,
        int ExpiresIn,
        string? Scope);
}
