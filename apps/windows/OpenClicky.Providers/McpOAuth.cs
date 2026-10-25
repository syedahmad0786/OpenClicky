using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// Browser sign-in for a remote MCP server: protected-resource and authorization-server discovery, dynamic client
/// registration, PKCE S256, the <c>resource</c> parameter, a loopback callback on a fixed port, and token refresh.
/// The user signs in in their own browser on the server's page; OpenClicky only receives the tokens. Every server
/// answer is untrusted data and none of it (nor any token, code, secret or URL) reaches an exception message.
/// </summary>
public sealed partial class McpOAuth : IDisposable
{
    public const int CallbackPort = 47613;
    private const string SignedInPage = "You're signed in. You can close this tab and go back to OpenClicky.";
    private const string FailedPage = "Sign-in didn't finish. Go back to OpenClicky to try again.";

    private readonly HttpClient _http;
    private readonly Func<Uri, Task> _openBrowser;
    private readonly int _callbackPort;

    public McpOAuth(HttpMessageHandler? handler = null, Func<Uri, Task>? openBrowser = null, int callbackPort = CallbackPort)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = JsonRpcMcpConnector.MaxResponseBytes,
        };
        _openBrowser = openBrowser ?? OpenDefaultBrowser;
        _callbackPort = callbackPort;
    }

    public static string RedirectUri(int port = CallbackPort) => $"http://127.0.0.1:{port}/callback";

    /// <summary>Runs the whole sign-in. The caller bounds it (5 minutes); cancellation becomes <see cref="McpOAuthFailure.TimedOut"/>.</summary>
    public async Task<McpOAuthTokens> SignInAsync(Uri mcpUrl, string? clientId, CancellationToken ct)
    {
        Secure(mcpUrl, mcpUrl);
        // Bound first, so a busy port fails before anything is registered with the server.
        using var listener = Listen(_callbackPort);
        try
        {
            var (resource, scope, server) = await DiscoverAsync(mcpUrl, ct).ConfigureAwait(false);
            var redirect = RedirectUri(_callbackPort);
            var client = string.IsNullOrWhiteSpace(clientId)
                ? await RegisterAsync(server.Registration, redirect, ct).ConfigureAwait(false)
                : new Client(clientId.Trim(), null, null);
            var verifier = Pkce.CreateVerifier();
            var state = Pkce.RandomUrlToken();
            var query = new List<(string, string)>
            {
                ("response_type", "code"), ("client_id", client.Id), ("redirect_uri", redirect),
                ("code_challenge", Pkce.ChallengeS256(verifier)), ("code_challenge_method", "S256"), ("state", state), ("resource", resource),
            };
            if (scope is not null) query.Add(("scope", scope));
            var authorize = new UriBuilder(server.Authorize)
            {
                Query = (server.Authorize.Query.Length > 1 ? server.Authorize.Query[1..] + "&" : "") + Form(query),
            }.Uri;

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var callback = AwaitCallbackAsync(listener, state, async code =>
            {
                var answer = await TokenRequestAsync(server.Token, client.Id, client.Secret, client.AuthMethod,
                    [("grant_type", "authorization_code"), ("code", code), ("code_verifier", verifier), ("redirect_uri", redirect), ("resource", resource)],
                    McpOAuthFailure.TokenExchangeFailed, stop.Token).ConfigureAwait(false);
                return new McpOAuthTokens(answer.AccessToken, answer.RefreshToken, answer.ExpiresAt, server.Token.AbsoluteUri,
                    client.Id, client.Secret, client.AuthMethod, resource, answer.Scope ?? scope);
            }, stop.Token);
            try { await _openBrowser(authorize).ConfigureAwait(false); }
            catch
            {
                stop.Cancel();
                try { await callback.ConfigureAwait(false); } catch { /* the browser failure is the error to report */ }
                throw;
            }
            return await callback.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new McpOAuthException(McpOAuthFailure.TimedOut);
        }
    }

    /// <summary>Exchanges the refresh token. A rotated refresh token replaces the old one; an absent one keeps it.</summary>
    public async Task<McpOAuthTokens> RefreshAsync(McpOAuthTokens tokens, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tokens.RefreshToken) || !Uri.TryCreate(tokens.TokenEndpoint, UriKind.Absolute, out var endpoint))
            throw new McpOAuthException(McpOAuthFailure.RefreshRejected);
        Secure(endpoint, Uri.TryCreate(tokens.Resource, UriKind.Absolute, out var resource) ? resource : endpoint);
        var answer = await TokenRequestAsync(endpoint, tokens.ClientId, tokens.ClientSecret, tokens.ClientAuthMethod,
            [("grant_type", "refresh_token"), ("refresh_token", tokens.RefreshToken), ("resource", tokens.Resource)],
            McpOAuthFailure.RefreshRejected, ct).ConfigureAwait(false);
        return tokens with
        {
            AccessToken = answer.AccessToken,
            RefreshToken = answer.RefreshToken ?? tokens.RefreshToken,
            ExpiresAt = answer.ExpiresAt,
            Scope = answer.Scope ?? tokens.Scope,
        };
    }

    /// <summary>Reads <c>resource_metadata</c> and <c>scope</c> from a Bearer challenge (RFC 9728 §5.1); anything else gives nulls.</summary>
    public static (Uri? ResourceMetadata, string? Scope) ParseChallenge(string? wwwAuthenticate)
    {
        if (wwwAuthenticate is null || BearerScheme().Match(wwwAuthenticate) is not { Success: true } bearer) return (null, null);
        Uri? metadata = null;
        string? scope = null;
        foreach (Match parameter in ChallengeParameter().Matches(wwwAuthenticate, bearer.Index + bearer.Length))
        {
            var value = parameter.Groups[2].Success ? Regex.Replace(parameter.Groups[2].Value, @"\\(.)", "$1") : parameter.Groups[3].Value;
            if (parameter.Groups[1].Value.Equals("resource_metadata", StringComparison.OrdinalIgnoreCase))
                metadata ??= Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
            else if (parameter.Groups[1].Value.Equals("scope", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                scope ??= value;
        }
        return (metadata, scope);
    }

    /// <summary>RFC 9728 §3.1: the path-suffixed well-known URL first, then the root one.</summary>
    public static IReadOnlyList<Uri> ProtectedResourceCandidates(Uri mcpUrl) =>
        WellKnown(mcpUrl, path => path.Length > 0
            ? ["/.well-known/oauth-protected-resource" + path, "/.well-known/oauth-protected-resource"]
            : ["/.well-known/oauth-protected-resource"]);

    /// <summary>RFC 8414 §3.1 plus OpenID Connect discovery, in the order the MCP authorization spec gives.</summary>
    public static IReadOnlyList<Uri> AuthorizationServerCandidates(Uri issuer) =>
        WellKnown(issuer, path => path.Length > 0
            ? ["/.well-known/oauth-authorization-server" + path, "/.well-known/openid-configuration" + path, path + "/.well-known/openid-configuration"]
            : ["/.well-known/oauth-authorization-server", "/.well-known/openid-configuration"]);

    /// <summary>RFC 8707 resource indicator: lower-case scheme and host, no fragment, no lone root slash.</summary>
    public static string CanonicalResource(Uri mcpUrl)
    {
        var value = mcpUrl.GetLeftPart(UriPartial.Query);
        return mcpUrl.AbsolutePath == "/" && mcpUrl.Query.Length == 0 ? value.TrimEnd('/') : value;
    }

    public void Dispose() => _http.Dispose();

    private sealed record Server(Uri Authorize, Uri Token, Uri? Registration);
    private sealed record Client(string Id, string? Secret, string? AuthMethod);
    private sealed record TokenAnswer(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string? Scope);

    private async Task<(string Resource, string? Scope, Server Server)> DiscoverAsync(Uri mcpUrl, CancellationToken ct)
    {
        var (challengeMetadata, challengeScope) = ParseChallenge(await ChallengeAsync(mcpUrl, ct).ConfigureAwait(false));
        var resourceCandidates = ProtectedResourceCandidates(mcpUrl);
        using var resourceMetadata = await FirstJsonAsync(
            challengeMetadata is null ? resourceCandidates : [challengeMetadata, .. resourceCandidates], mcpUrl, ct).ConfigureAwait(false);
        var protectedResource = resourceMetadata?.RootElement ?? default;

        var issuer = protectedResource.ValueKind == JsonValueKind.Object
            && protectedResource.TryGetProperty("authorization_servers", out var servers) && servers.ValueKind == JsonValueKind.Array
            && servers.GetArrayLength() > 0 && servers[0].ValueKind == JsonValueKind.String
            && Uri.TryCreate(servers[0].GetString(), UriKind.Absolute, out var listed)
                ? listed
                : new Uri(mcpUrl.GetLeftPart(UriPartial.Authority));
        var scopes = protectedResource.ValueKind == JsonValueKind.Object
            && protectedResource.TryGetProperty("scopes_supported", out var supported) && supported.ValueKind == JsonValueKind.Array
                ? string.Join(' ', supported.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)))
                : "";
        // RFC 9728 §3.3: metadata naming another resource (say, a victim server's, via a hostile challenge) is refused,
        // so a token meant for that resource is never sent to this server.
        var resource = Text(protectedResource, "resource") is { } named
            ? Covers(named, mcpUrl) ? named : throw new McpOAuthException(McpOAuthFailure.InsecureEndpoint)
            : CanonicalResource(mcpUrl);
        var scope = challengeScope ?? (scopes.Length > 0 ? scopes : null);

        using var serverMetadata = await FirstJsonAsync(AuthorizationServerCandidates(issuer), mcpUrl, ct).ConfigureAwait(false)
            ?? throw new McpOAuthException(McpOAuthFailure.NoMetadata);
        var root = serverMetadata.RootElement;
        var authorize = Endpoint(root, "authorization_endpoint", mcpUrl) ?? throw new McpOAuthException(McpOAuthFailure.NoMetadata);
        var token = Endpoint(root, "token_endpoint", mcpUrl) ?? throw new McpOAuthException(McpOAuthFailure.NoMetadata);
        return (resource, scope, new Server(authorize, token, Endpoint(root, "registration_endpoint", mcpUrl)));
    }

    /// <summary>An unauthenticated initialize; a 401 carries the challenge. Only the headers are read.</summary>
    private async Task<string?> ChallengeAsync(Uri mcpUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize",
                ["params"] = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "OpenClicky", version = "0.7" } },
            }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; } // our 30 s timeout: try the well-known URLs
        using var _ = response;
        return response.StatusCode == HttpStatusCode.Unauthorized && response.Headers.TryGetValues("WWW-Authenticate", out var values)
            ? string.Join(", ", values)
            : null;
    }

    /// <summary>The first candidate answering 200 with a JSON object; every candidate must be a secure address.</summary>
    private async Task<JsonDocument?> FirstJsonAsync(IEnumerable<Uri> candidates, Uri mcpUrl, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            Secure(candidate, mcpUrl);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK) continue;
                var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
                document.Dispose();
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } // our 30 s timeout: next candidate
            catch (JsonException) { }
        }
        return null;
    }

    private async Task<Client> RegisterAsync(Uri? endpoint, string redirect, CancellationToken ct)
    {
        if (endpoint is null) throw new McpOAuthException(McpOAuthFailure.RegistrationUnsupported);
        using var content = new StringContent(JsonSerializer.Serialize(new
        {
            client_name = "OpenClicky",
            redirect_uris = new[] { redirect },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
        }), Encoding.UTF8, "application/json");
        using var response = await TimeoutIs(McpOAuthFailure.TokenExchangeFailed, _http.PostAsync(endpoint, content, ct), ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new McpOAuthException(McpOAuthFailure.ApprovedClientsOnly);
        if (!response.IsSuccessStatusCode) throw new McpOAuthException(McpOAuthFailure.RegistrationUnsupported);
        using var answer = await ParseAsync(response, McpOAuthFailure.RegistrationUnsupported, ct).ConfigureAwait(false);
        return new Client(Text(answer.RootElement, "client_id") ?? throw new McpOAuthException(McpOAuthFailure.RegistrationUnsupported),
            Text(answer.RootElement, "client_secret"), Text(answer.RootElement, "token_endpoint_auth_method"));
    }

    private async Task<TokenAnswer> TokenRequestAsync(Uri endpoint, string clientId, string? secret, string? method,
        List<(string, string)> fields, McpOAuthFailure rejected, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (secret is not null && method == "client_secret_basic")
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(clientId) + ":" + Uri.EscapeDataString(secret))));
        else
        {
            fields.Add(("client_id", clientId));
            if (secret is not null) fields.Add(("client_secret", secret));
        }
        request.Content = new StringContent(Form(fields), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // A sign-in exchange that times out failed; a refresh that times out is transient and stays a plain timeout.
        using var response = rejected == McpOAuthFailure.TokenExchangeFailed
            ? await TimeoutIs(rejected, _http.SendAsync(request, ct), ct).ConfigureAwait(false)
            : await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // A refresh the server refuses needs a new sign-in; a server error is transient and must not force one.
            if (rejected == McpOAuthFailure.TokenExchangeFailed || response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw new McpOAuthException(rejected);
            throw new HttpRequestException($"The sign-in server answered HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
        using var answer = await ParseAsync(response, rejected, ct).ConfigureAwait(false);
        var root = answer.RootElement;
        var expiresIn = Seconds(root);
        return new TokenAnswer(Text(root, "access_token") ?? throw new McpOAuthException(rejected), Text(root, "refresh_token"),
            expiresIn is { } value ? DateTimeOffset.UtcNow.AddSeconds(value) : null, Text(root, "scope"));
    }

    /// <summary><c>expires_in</c> as a number or numeric string, clamped to ten years; null when absent (never refresh).</summary>
    private static double? Seconds(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var value)) return null;
        double seconds;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) seconds = number;
        else if (value.ValueKind != JsonValueKind.String || !double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out seconds)) return null;
        return double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 10 * 365 * 86_400d) : null;
    }

    /// <summary>Turns our own HTTP timeout (not the caller's cancellation) into <paramref name="failure"/>.</summary>
    private static async Task<HttpResponseMessage> TimeoutIs(McpOAuthFailure failure, Task<HttpResponseMessage> send, CancellationToken ct)
    {
        try { return await send.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new McpOAuthException(failure); }
    }

    /// <summary>Same scheme, host and port, and a segment-wise path prefix: <c>https://h/</c> covers <c>https://h/mcp</c>, <c>https://h/m</c> does not.</summary>
    private static bool Covers(string resource, Uri mcpUrl)
    {
        if (!Uri.TryCreate(resource, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, mcpUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.IdnHost, mcpUrl.IdnHost, StringComparison.OrdinalIgnoreCase) || uri.Port != mcpUrl.Port) return false;
        var prefix = uri.AbsolutePath.TrimEnd('/');
        var path = mcpUrl.AbsolutePath;
        return prefix.Length == 0 || path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, McpOAuthFailure failure, CancellationToken ct)
    {
        try
        {
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException) { }
        throw new McpOAuthException(failure);
    }

    private static HttpListener Listen(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            listener.Start();
            return listener;
        }
        catch (HttpListenerException)
        {
            listener.Close();
            throw new McpOAuthException(McpOAuthFailure.PortInUse);
        }
    }

    /// <summary>Serves the loopback until the real callback arrives: other paths get 404, then state, error and code are checked in that order.</summary>
    private static async Task<McpOAuthTokens> AwaitCallbackAsync(HttpListener listener, string state,
        Func<string, Task<McpOAuthTokens>> exchange, CancellationToken ct)
    {
        using var stop = ct.Register(() =>
        {
            try { listener.Stop(); } catch (ObjectDisposedException) { }
        });
        while (true)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException && ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            if (context.Request.HttpMethod != "GET" || context.Request.Url?.AbsolutePath != "/callback")
            {
                await ReplyAsync(context.Response, 404, "Not found.").ConfigureAwait(false);
                continue;
            }
            var query = context.Request.QueryString;
            McpOAuthTokens tokens;
            try
            {
                if (!string.Equals(query["state"], state, StringComparison.Ordinal)) throw new McpOAuthException(McpOAuthFailure.StateMismatch);
                if (query["error"] is not null) throw new McpOAuthException(McpOAuthFailure.Denied);
                if (query["code"] is not { Length: > 0 } code) throw new McpOAuthException(McpOAuthFailure.StateMismatch);
                tokens = await exchange(code).ConfigureAwait(false);
            }
            catch
            {
                await ReplyAsync(context.Response, 400, FailedPage).ConfigureAwait(false);
                throw;
            }
            await ReplyAsync(context.Response, 200, SignedInPage).ConfigureAwait(false);
            return tokens;
        }
    }

    private static async Task ReplyAsync(HttpListenerResponse response, int status, string text)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(
                $"<!doctype html><html><head><meta charset=\"utf-8\"><title>OpenClicky</title></head><body><p>{text}</p></body></html>");
            response.StatusCode = status;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // The browser went away; the sign-in result does not depend on the page.
        }
    }

    private static Task OpenDefaultBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>https anywhere, or http to this PC only for a server that is itself on this PC (a remote server's metadata cannot point tokens at a local port).</summary>
    private static void Secure(Uri uri, Uri mcpUrl)
    {
        if (!uri.IsAbsoluteUri || !(uri.Scheme == Uri.UriSchemeHttps
                || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && mcpUrl.IsAbsoluteUri && mcpUrl.IsLoopback)))
            throw new McpOAuthException(McpOAuthFailure.InsecureEndpoint);
    }

    private static Uri? Endpoint(JsonElement root, string name, Uri mcpUrl)
    {
        if (Text(root, name) is not { } value || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        Secure(uri, mcpUrl);
        return uri;
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text ? text : null;

    private static string Form(IEnumerable<(string Name, string Value)> fields) =>
        string.Join("&", fields.Select(f => Uri.EscapeDataString(f.Name) + "=" + Uri.EscapeDataString(f.Value)));

    private static IReadOnlyList<Uri> WellKnown(Uri baseUri, Func<string, string[]> paths)
    {
        var origin = baseUri.GetLeftPart(UriPartial.Authority);
        return paths(baseUri.AbsolutePath.TrimEnd('/')).Select(path => new Uri(origin + path)).ToList();
    }

    [GeneratedRegex(@"(?:^|[\s,])Bearer(?=\s|,|$)", RegexOptions.IgnoreCase)]
    private static partial Regex BearerScheme();

    [GeneratedRegex(@"([A-Za-z0-9_-]+)\s*=\s*(?:""((?:[^""\\]|\\.)*)""|([^\s,""]+))")]
    private static partial Regex ChallengeParameter();
}
