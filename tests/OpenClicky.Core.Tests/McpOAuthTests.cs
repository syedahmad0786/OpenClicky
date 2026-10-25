using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Web;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class McpOAuthTests
{
    private static readonly Uri Mcp = new("https://mcp.x.test/mcp");
    private const string Challenge = "Bearer resource_metadata=\"https://mcp.x.test/.well-known/oauth-protected-resource/mcp\", scope=\"read write\"";
    private const string SignedIn = "You're signed in. You can close this tab and go back to OpenClicky.";
    private const string NotFinished = "Sign-in didn't finish. Go back to OpenClicky to try again.";

    [Fact]
    public void ParseChallengeReadsMetadataAndScope()
    {
        var (metadata, scope) = McpOAuth.ParseChallenge(Challenge);
        Assert.Equal(new Uri("https://mcp.x.test/.well-known/oauth-protected-resource/mcp"), metadata);
        Assert.Equal("read write", scope);
        Assert.Equal((null, null), McpOAuth.ParseChallenge("Basic realm=\"x\""));
        Assert.Equal((null, null), McpOAuth.ParseChallenge(null));
    }

    [Fact]
    public void CandidatesFollowRfcOrder()
    {
        Assert.Equal(
            ["https://mcp.x.test/.well-known/oauth-protected-resource/mcp", "https://mcp.x.test/.well-known/oauth-protected-resource"],
            McpOAuth.ProtectedResourceCandidates(Mcp).Select(u => u.AbsoluteUri));
        Assert.Equal(["https://mcp.x.test/.well-known/oauth-protected-resource"],
            McpOAuth.ProtectedResourceCandidates(new Uri("https://mcp.x.test/")).Select(u => u.AbsoluteUri));
        Assert.Equal(
            ["https://as.test/.well-known/oauth-authorization-server/tenant", "https://as.test/.well-known/openid-configuration/tenant", "https://as.test/tenant/.well-known/openid-configuration"],
            McpOAuth.AuthorizationServerCandidates(new Uri("https://as.test/tenant")).Select(u => u.AbsoluteUri));
        Assert.Equal(["https://as.test/.well-known/oauth-authorization-server", "https://as.test/.well-known/openid-configuration"],
            McpOAuth.AuthorizationServerCandidates(new Uri("https://as.test")).Select(u => u.AbsoluteUri));
    }

    [Fact]
    public void CanonicalResourceDropsRootSlashAndFragment()
    {
        Assert.Equal("https://mcp.x.test", McpOAuth.CanonicalResource(new Uri("https://MCP.x.test/")));
        Assert.Equal("https://mcp.x.test/mcp", McpOAuth.CanonicalResource(new Uri("https://mcp.x.test/mcp")));
        Assert.Equal("https://mcp.x.test/mcp", McpOAuth.CanonicalResource(new Uri("https://mcp.x.test/mcp#part")));
        Assert.Equal("http://127.0.0.1:47613/callback", McpOAuth.RedirectUri());
    }

    [Fact]
    public async Task FullFlowWithDynamicRegistration()
    {
        var server = FakeServer.Standard();
        var browser = new Browser();
        var port = FreePort();
        using var oauth = new McpOAuth(server, browser.Open, port);
        var before = DateTimeOffset.UtcNow;

        var tokens = await oauth.SignInAsync(Mcp, null, Timeout());

        var initialize = JsonDocument.Parse(server.Single("POST", "https://mcp.x.test/mcp").Body).RootElement;
        Assert.Equal("initialize", initialize.GetProperty("method").GetString());
        Assert.Equal("2025-06-18", initialize.GetProperty("params").GetProperty("protocolVersion").GetString());
        Assert.Equal("OpenClicky", initialize.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString());
        Assert.Null(server.Single("POST", "https://mcp.x.test/mcp").Authorization);

        var registration = JsonDocument.Parse(server.Single("POST", "https://as.test/register").Body).RootElement;
        Assert.Equal([$"http://127.0.0.1:{port}/callback"], registration.GetProperty("redirect_uris").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("none", registration.GetProperty("token_endpoint_auth_method").GetString());
        var grants = registration.GetProperty("grant_types").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("authorization_code", grants);
        Assert.Contains("refresh_token", grants);
        Assert.Equal("OpenClicky", registration.GetProperty("client_name").GetString());

        var authorize = HttpUtility.ParseQueryString(browser.Authorize!.Query);
        Assert.StartsWith("https://as.test/authorize?", browser.Authorize.AbsoluteUri);
        Assert.Equal("code", authorize["response_type"]);
        Assert.Equal("dyn-1", authorize["client_id"]);
        Assert.Equal($"http://127.0.0.1:{port}/callback", authorize["redirect_uri"]);
        Assert.Equal("S256", authorize["code_challenge_method"]);
        Assert.Equal(43, authorize["code_challenge"]!.Length);
        Assert.False(string.IsNullOrEmpty(authorize["state"]));
        Assert.Equal("https://mcp.x.test/mcp", authorize["resource"]);
        Assert.Equal("read write", authorize["scope"]);

        var token = server.Single("POST", "https://as.test/token");
        var form = HttpUtility.ParseQueryString(token.Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(authorize["code_challenge"], Pkce.ChallengeS256(form["code_verifier"]!));
        Assert.Equal($"http://127.0.0.1:{port}/callback", form["redirect_uri"]);
        Assert.Equal("dyn-1", form["client_id"]);
        Assert.Equal("https://mcp.x.test/mcp", form["resource"]);
        Assert.Null(form["client_secret"]);
        Assert.Null(token.Authorization);

        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Equal("rt-1", tokens.RefreshToken);
        Assert.InRange(tokens.ExpiresAt!.Value, before.AddSeconds(3600), DateTimeOffset.UtcNow.AddSeconds(3600));
        Assert.Equal("https://as.test/token", tokens.TokenEndpoint);
        Assert.Equal("dyn-1", tokens.ClientId);
        Assert.Equal("https://mcp.x.test/mcp", tokens.Resource);
        Assert.Equal("read write", tokens.Scope);
        Assert.Equal(200, browser.Status);
        Assert.Contains(SignedIn, browser.Page);
    }

    [Fact]
    public async Task PreRegisteredClientSkipsRegistration()
    {
        var server = FakeServer.Standard();
        var browser = new Browser();
        using var oauth = new McpOAuth(server, browser.Open, FreePort());

        var tokens = await oauth.SignInAsync(Mcp, "abc", Timeout());

        Assert.Empty(server.Where("POST", "https://as.test/register"));
        Assert.Equal("abc", HttpUtility.ParseQueryString(browser.Authorize!.Query)["client_id"]);
        Assert.Equal("abc", HttpUtility.ParseQueryString(server.Single("POST", "https://as.test/token").Body)["client_id"]);
        Assert.Equal("abc", tokens.ClientId);
        Assert.Null(tokens.ClientSecret);
    }

    [Fact]
    public async Task ResourceFromMetadataWins()
    {
        var server = FakeServer.Standard();
        server.Json("GET", "https://mcp.x.test/.well-known/oauth-protected-resource/mcp",
            """{"resource":"https://mcp.x.test/","authorization_servers":["https://as.test"],"scopes_supported":["a","b"]}""");
        server.Route("POST", "https://mcp.x.test/mcp", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var browser = new Browser();
        using var oauth = new McpOAuth(server, browser.Open, FreePort());

        var tokens = await oauth.SignInAsync(Mcp, null, Timeout());

        var authorize = HttpUtility.ParseQueryString(browser.Authorize!.Query);
        Assert.Equal("https://mcp.x.test/", authorize["resource"]);
        Assert.Equal("a b", authorize["scope"]);
        Assert.Equal("https://mcp.x.test/", HttpUtility.ParseQueryString(server.Single("POST", "https://as.test/token").Body)["resource"]);
        Assert.Equal("https://mcp.x.test/", tokens.Resource);
    }

    [Fact]
    public async Task NoRegistrationEndpointNeedsClientId()
    {
        var server = FakeServer.Standard();
        server.Json("GET", "https://as.test/.well-known/oauth-authorization-server",
            """{"issuer":"https://as.test","authorization_endpoint":"https://as.test/authorize","token_endpoint":"https://as.test/token"}""");
        await Fails(McpOAuthFailure.RegistrationUnsupported, server);

        server = FakeServer.Standard();
        server.Route("POST", "https://as.test/register", _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        await Fails(McpOAuthFailure.ApprovedClientsOnly, server);

        server = new FakeServer();
        server.Route("POST", "https://mcp.x.test/mcp", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Fails(McpOAuthFailure.NoMetadata, server);
        Assert.Contains(server.Requests, r => r.Url == "https://mcp.x.test/.well-known/oauth-authorization-server");
    }

    [Fact]
    public async Task InsecureEndpointRefused()
    {
        var server = FakeServer.Standard();
        server.Json("GET", "https://as.test/.well-known/oauth-authorization-server",
            """{"issuer":"https://as.test","authorization_endpoint":"https://as.test/authorize","token_endpoint":"http://as.test/token","registration_endpoint":"https://as.test/register"}""");
        await Fails(McpOAuthFailure.InsecureEndpoint, server);
        Assert.DoesNotContain(server.Requests, r => r.Url.EndsWith("/token", StringComparison.Ordinal));

        server = FakeServer.Standard();
        server.Route("POST", "https://mcp.x.test/mcp", _ => Unauthorized("Bearer resource_metadata=\"http://mcp.x.test/.well-known/oauth-protected-resource\""));
        await Fails(McpOAuthFailure.InsecureEndpoint, server);
        Assert.DoesNotContain(server.Requests, r => r.Url.StartsWith("http://", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateMismatchFails()
    {
        var server = FakeServer.Standard();
        var browser = new Browser { Callback = _ => "/callback?code=the-code&state=wrong" };
        await Fails(McpOAuthFailure.StateMismatch, server, browser);
        Assert.Empty(server.Where("POST", "https://as.test/token"));
        Assert.Equal(400, browser.Status);
        Assert.Contains(NotFinished, browser.Page);
    }

    [Fact]
    public async Task DeniedInBrowserFails()
    {
        var server = FakeServer.Standard();
        var browser = new Browser { Callback = q => $"/callback?error=access_denied&state={Uri.EscapeDataString(q["state"]!)}" };
        await Fails(McpOAuthFailure.Denied, server, browser);
        Assert.Empty(server.Where("POST", "https://as.test/token"));
        Assert.Contains(NotFinished, browser.Page);
    }

    [Fact]
    public async Task TokenExchangeRejectedFails()
    {
        var server = FakeServer.Standard();
        server.Json("POST", "https://as.test/token", """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        var browser = new Browser();
        await Fails(McpOAuthFailure.TokenExchangeFailed, server, browser);
        Assert.Contains(NotFinished, browser.Page);
    }

    [Fact]
    public async Task IgnoresOtherPaths()
    {
        var server = FakeServer.Standard();
        var browser = new Browser { Before = "/favicon.ico" };
        using var oauth = new McpOAuth(server, browser.Open, FreePort());

        var tokens = await oauth.SignInAsync(Mcp, null, Timeout());

        Assert.Equal(404, browser.BeforeStatus);
        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Contains(SignedIn, browser.Page);
    }

    [Fact]
    public async Task PortInUseFails()
    {
        var server = FakeServer.Standard();
        var port = FreePort();
        using var busy = new HttpListener();
        busy.Prefixes.Add($"http://127.0.0.1:{port}/");
        busy.Start();
        var browser = new Browser();
        using var oauth = new McpOAuth(server, browser.Open, port);

        var error = await Assert.ThrowsAsync<McpOAuthException>(() => oauth.SignInAsync(Mcp, null, Timeout()));

        Assert.Equal(McpOAuthFailure.PortInUse, error.Failure);
        Assert.Empty(server.Requests);
        Assert.Null(browser.Authorize);
    }

    [Fact]
    public async Task CancellationTimesOutAndClosesListener()
    {
        var server = FakeServer.Standard();
        var port = FreePort();
        using var cts = new CancellationTokenSource();
        using var oauth = new McpOAuth(server, _ => { cts.CancelAfter(50); return Task.CompletedTask; }, port);

        var error = await Assert.ThrowsAsync<McpOAuthException>(() => oauth.SignInAsync(Mcp, null, cts.Token));

        Assert.Equal(McpOAuthFailure.TimedOut, error.Failure);
        using var again = new HttpListener();
        again.Prefixes.Add($"http://127.0.0.1:{port}/");
        again.Start(); // the sign-in listener was closed
    }

    [Fact]
    public async Task RefreshRotatesTokens()
    {
        var server = new FakeServer();
        server.Json("POST", "https://as.test/token", """{"access_token":"at-2","refresh_token":"rt-2","expires_in":600}""");
        using var oauth = new McpOAuth(server);
        var old = Tokens();

        var rotated = await oauth.RefreshAsync(old, Timeout());

        var form = HttpUtility.ParseQueryString(server.Single("POST", "https://as.test/token").Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("rt-1", form["refresh_token"]);
        Assert.Equal("client-1", form["client_id"]);
        Assert.Equal("https://mcp.x.test/mcp", form["resource"]);
        Assert.Equal(("at-2", "rt-2"), (rotated.AccessToken, rotated.RefreshToken));
        Assert.InRange(rotated.ExpiresAt!.Value, DateTimeOffset.UtcNow.AddSeconds(590), DateTimeOffset.UtcNow.AddSeconds(600));
        Assert.Equal(old with { AccessToken = "at-2", RefreshToken = "rt-2", ExpiresAt = rotated.ExpiresAt }, rotated);

        server.Json("POST", "https://as.test/token", """{"access_token":"at-3"}""");
        var kept = await oauth.RefreshAsync(rotated, Timeout());
        Assert.Equal(("at-3", "rt-2"), (kept.AccessToken, kept.RefreshToken));

        server.Json("POST", "https://as.test/token", """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        Assert.Equal(McpOAuthFailure.RefreshRejected, (await Assert.ThrowsAsync<McpOAuthException>(() => oauth.RefreshAsync(kept, Timeout()))).Failure);
        Assert.Equal(McpOAuthFailure.RefreshRejected,
            (await Assert.ThrowsAsync<McpOAuthException>(() => oauth.RefreshAsync(kept with { RefreshToken = null }, Timeout()))).Failure);
    }

    [Fact]
    public async Task ClientSecretUsesRegisteredMethod()
    {
        var server = new FakeServer();
        server.Json("POST", "https://as.test/token", """{"access_token":"at-2"}""");
        using var oauth = new McpOAuth(server);

        await oauth.RefreshAsync(Tokens() with { ClientSecret = "s 1", ClientAuthMethod = "client_secret_basic" }, Timeout());
        var basic = server.Requests[^1];
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client-1:s%201")), basic.Authorization);
        Assert.Null(HttpUtility.ParseQueryString(basic.Body)["client_secret"]);

        await oauth.RefreshAsync(Tokens() with { ClientSecret = "s1", ClientAuthMethod = "client_secret_post" }, Timeout());
        var post = server.Requests[^1];
        Assert.Null(post.Authorization);
        Assert.Equal("s1", HttpUtility.ParseQueryString(post.Body)["client_secret"]);
        Assert.Equal("client-1", HttpUtility.ParseQueryString(post.Body)["client_id"]);
    }

    internal static McpOAuthTokens Tokens(DateTimeOffset? expiresAt = null) =>
        new("at-1", "rt-1", expiresAt, "https://as.test/token", "client-1", null, null, "https://mcp.x.test/mcp", "read");

    private static async Task Fails(McpOAuthFailure expected, FakeServer server, Browser? browser = null)
    {
        browser ??= new Browser();
        using var oauth = new McpOAuth(server, browser.Open, FreePort());
        var error = await Assert.ThrowsAsync<McpOAuthException>(() => oauth.SignInAsync(Mcp, null, Timeout()));
        Assert.Equal(expected, error.Failure);
    }

    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private static HttpResponseMessage Unauthorized(string challenge)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", challenge);
        return response;
    }

    /// <summary>Plays the user's browser: records the authorize URL and makes a real GET to the loopback redirect.</summary>
    private sealed class Browser
    {
        public Func<NameValueCollection, string> Callback { get; init; } = q => $"/callback?code=the-code&state={Uri.EscapeDataString(q["state"]!)}";
        public string? Before { get; init; }
        public Uri? Authorize { get; private set; }
        public int Status { get; private set; }
        public int BeforeStatus { get; private set; }
        public string? Page { get; private set; }

        public async Task Open(Uri authorize)
        {
            Authorize = authorize;
            var query = HttpUtility.ParseQueryString(authorize.Query);
            var redirect = new Uri(query["redirect_uri"]!);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            if (Before is not null) BeforeStatus = (int)(await http.GetAsync(new Uri(redirect, Before))).StatusCode;
            using var response = await http.GetAsync(new Uri(redirect, Callback(query)));
            Status = (int)response.StatusCode;
            Page = await response.Content.ReadAsStringAsync();
        }
    }

    internal sealed record Seen(string Method, string Url, string Body, string? Authorization);

    /// <summary>Plays the MCP server and its authorization server.</summary>
    internal sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<string, HttpResponseMessage>> _routes = new();
        private readonly List<Seen> _requests = new();

        public IReadOnlyList<Seen> Requests { get { lock (_requests) return _requests.ToList(); } }
        public IEnumerable<Seen> Where(string method, string url) => Requests.Where(r => r.Method == method && r.Url == url);
        public Seen Single(string method, string url) => Assert.Single(Where(method, url));

        public void Route(string method, string url, Func<string, HttpResponseMessage> respond) => _routes[$"{method} {url}"] = respond;

        public void Json(string method, string url, string json, HttpStatusCode status = HttpStatusCode.OK) =>
            Route(method, url, _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

        public static FakeServer Standard()
        {
            var server = new FakeServer();
            server.Route("POST", "https://mcp.x.test/mcp", _ => Unauthorized(Challenge));
            server.Json("GET", "https://mcp.x.test/.well-known/oauth-protected-resource/mcp", """{"authorization_servers":["https://as.test"]}""");
            server.Json("GET", "https://as.test/.well-known/oauth-authorization-server",
                """{"issuer":"https://as.test","authorization_endpoint":"https://as.test/authorize","token_endpoint":"https://as.test/token","registration_endpoint":"https://as.test/register"}""");
            server.Json("POST", "https://as.test/register", """{"client_id":"dyn-1"}""", HttpStatusCode.Created);
            server.Json("POST", "https://as.test/token", """{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600,"token_type":"Bearer"}""");
            return server;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var url = request.RequestUri!.AbsoluteUri;
            lock (_requests) _requests.Add(new(request.Method.Method, url, body, request.Headers.Authorization?.ToString()));
            return _routes.TryGetValue($"{request.Method.Method} {url}", out var respond) ? respond(body) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
