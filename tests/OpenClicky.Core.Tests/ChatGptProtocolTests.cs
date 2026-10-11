using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class ChatGptProtocolTests
{
    private const string Completed = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Token sharing works.\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Token sharing works.\"}]}]}}\n\n";

    [Fact]
    public async Task Agent_UsesFinalMessageWithoutConcatenatingCommentary()
    {
        var commentary = JsonSerializer.Serialize("{\"tool\":\"click\",\"args\":{},\"rationale\":\"preliminary\"}");
        var final = JsonSerializer.Serialize("{\"tool\":\"uia_snapshot\",\"args\":{},\"rationale\":\"inspect first\"}");
        var body = $"data: {{\"type\":\"response.output_text.delta\",\"delta\":{commentary}}}\n\n"
            + $"data: {{\"type\":\"response.output_text.delta\",\"delta\":{final}}}\n\n"
            + $"data: {{\"type\":\"response.completed\",\"response\":{{\"status\":\"completed\",\"output\":["
            + $"{{\"type\":\"message\",\"phase\":\"commentary\",\"content\":[{{\"type\":\"output_text\",\"text\":{commentary}}}]}},"
            + $"{{\"type\":\"message\",\"phase\":\"final_answer\",\"content\":[{{\"type\":\"output_text\",\"text\":{final}}}]}}]}}}}\n\n";
        using var http = new HttpClient(new Handler((r, c) => Task.FromResult(Reply(body))));
        var planner = new ChatGptAgentPlanner(_ => Task.FromResult("fixture-token"), http);
        var step = await planner.NextStepAsync("inspect sandbox", [], [], "available-model");
        Assert.Equal("uia_snapshot", step.ToolName);
        Assert.Equal("inspect first", step.Rationale);
    }

    [Fact]
    public async Task CompletedOutput_IsAuthoritativeEvenWhenDeltasDiffer()
    {
        using var http = new HttpClient(new Handler((r, c) => Task.FromResult(Reply(Completed.Replace("\"delta\":\"Token sharing works.\"", "\"delta\":\"outdated partial\"")))));
        var client = new ChatGptResponsesClient(_ => Task.FromResult("fixture-token"), http);
        Assert.Equal("Token sharing works.", await client.CompleteAsync(null, [], "available-model"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedStreamItems_WorkWithAnEmptyTerminalOutput(bool terminal)
    {
        var body = "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"message\",\"phase\":\"commentary\",\"content\":[{\"type\":\"output_text\",\"text\":\"preliminary\"}]}}\n\n"
            + "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"message\",\"phase\":\"final_answer\",\"content\":[{\"type\":\"output_text\",\"text\":\"final only\"}]}}\n\n";
        if (terminal) body += "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n";
        using var http = new HttpClient(new Handler((r, c) => Task.FromResult(Reply(body))));
        var client = new ChatGptResponsesClient(_ => Task.FromResult("fixture-token"), http);
        if (terminal) Assert.Equal("final only", await client.CompleteAsync(null, [], "available-model"));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(null, [], "available-model"));
    }

    [Fact]
    public async Task CompletedInference_UsesPublicRouteAndLogsOnlyReceipt()
    {
        var logs = new List<string>();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("fixture-token", request.Headers.Authorization!.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("input").ValueKind);
            var reply = Reply(Completed);
            reply.Headers.Add("x-request-id", "test-request");
            return reply;
        }));
        var client = new ChatGptResponsesClient(_ => Task.FromResult("fixture-token"), http, logs.Add);
        Assert.Equal("Token sharing works.", await client.CompleteAsync("short reply", [new { role = "user", content = "test" }], "available-model"));
        Assert.Contains(logs, s => s.Contains("completed=true") && s.Contains("test-request"));
        Assert.DoesNotContain(logs, s => s.Contains("fixture-token") || s.Contains("Token sharing works."));
    }

    [Theory]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n")]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\ndata: {\"type\":\"response.incomplete\"}\n\n")]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\ndata: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}}\n\n")]
    [InlineData("data: {\"type\":\"error\",\"code\":\"subscription_sharing_usage_unavailable\"}\n\n")]
    [InlineData("data: {\"type\":\"response.completed\"}\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"incomplete\",\"output\":[]}}\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}}\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"phase\":\"commentary\",\"content\":[{\"type\":\"output_text\",\"text\":\"not final\"}]}]}}\n\n")]
    public async Task InterruptedOrFailedStream_NeverPublishesPartialAnswer(string body)
    {
        using var http = new HttpClient(new Handler((r, c) => Task.FromResult(Reply(body))));
        var client = new ChatGptResponsesClient(_ => Task.FromResult("fixture-token"), http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(null, [], "available-model"));
    }

    [Fact]
    public async Task ModelCatalog_PreservesAccountOrderingAndDisplayNames()
    {
        using var http = new HttpClient(new Handler((request, ct) =>
        {
            Assert.Equal("https://api.openai.com/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("fixture-token", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Reply("{\"models\":[{\"slug\":\"second\",\"display_name\":\"Second model\",\"visibility\":\"list\"},{\"slug\":\"hidden\",\"display_name\":\"Hidden\",\"visibility\":\"hidden\"},{\"slug\":\"first\",\"display_name\":\"First model\",\"visibility\":\"list\"}]}"));
        }));
        var models = await new ChatGptResponsesClient(_ => Task.FromResult("fixture-token"), http).ListModelsAsync();
        Assert.Equal(new[] { "second", "first" }, models.Select(m => m.Slug));
        Assert.Equal("Second model", models[0].DisplayName);
    }

    [Fact]
    public async Task AdmissionError_PreservesStatusDetailAndRequestId()
    {
        using var http = new HttpClient(new Handler((request, ct) =>
        {
            var reply = Reply("{\"detail\":\"Plan permission unavailable\"}", HttpStatusCode.Forbidden);
            reply.Headers.Add("openai-request-id", "test-admission");
            return Task.FromResult(reply);
        }));
        var client = new ChatGptResponsesClient(_ => Task.FromResult("fixture-token"), http);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(null, [], "available-model"));
        Assert.Contains("403", error.Message);
        Assert.Contains("Plan permission unavailable", error.Message);
        Assert.Contains("test-admission", error.Message);
    }

    [Fact]
    public async Task TalkHistoryAndScreens_UseCorrectResponsesContentTypes()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var input = json.RootElement.GetProperty("input");
            Assert.Equal("input_text", input[0].GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal("output_text", input[1].GetProperty("content")[0].GetProperty("type").GetString());
            var image = input[2].GetProperty("content")[2];
            Assert.Equal("input_image", image.GetProperty("type").GetString());
            Assert.StartsWith("data:image/jpeg;base64,", image.GetProperty("image_url").GetString());
            return Reply(Completed);
        }));
        var client = new ChatGptTalkClient(_ => Task.FromResult("fixture-token"), http);
        await client.CompleteVisionTalkAsync("test", [("user", "question"), ("assistant", "answer")], "follow-up",
            [new([0xff, 0xd8], 100, 100, 0, 0, 100, 100, true, "screen1")], "available-model");
    }

    [Fact]
    public async Task TokenRefresh_IsSerializedAndStoresRotatedTokensTogether()
    {
        var secrets = new Secrets();
        ChatGptCredentialStore.Save(secrets, Credential());
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            calls++;
            Assert.Equal("https://auth.openai.com/api/accounts/oauth/token", request.RequestUri!.AbsoluteUri);
            var form = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("grant_type=refresh_token", form);
            Assert.Contains("client_id=oaiapp_fixture", form);
            Assert.Contains("refresh_token=old-refresh", form);
            Assert.DoesNotContain("scope=", form);
            await Task.Delay(30, ct);
            return Reply("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
        }));
        var settings = new AppSettings();
        var a = new ChatGptOAuthService(httpClient: http);
        var b = new ChatGptOAuthService(httpClient: http);
        var tokens = await Task.WhenAll(a.GetAccessTokenAsync(settings, secrets), b.GetAccessTokenAsync(settings, secrets));
        Assert.Equal(1, calls);
        Assert.All(tokens, t => Assert.Equal("new-access", t));
        var saved = ChatGptCredentialStore.Load(secrets)!;
        Assert.Equal("new-refresh", saved.RefreshToken);
        Assert.True(saved.HasUsableSession);
    }

    [Theory]
    [InlineData("{\"access_token\":\"new\",\"expires_in\":3600,\"scope\":\"openid\"}")]
    [InlineData("{\"access_token\":\"new\",\"expires_in\":3600,\"scope\":\"openid+chatgpt.tokens.use.direct\"}")]
    [InlineData("{\"access_token\":\"\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"new\",\"expires_in\":-1}")]
    [InlineData("{\"access_token\":\"new\",\"expires_in\":3600,\"token_type\":\"MAC\"}")]
    public async Task InvalidRefreshResult_CannotPowerInference(string body)
    {
        var secrets = new Secrets();
        ChatGptCredentialStore.Save(secrets, Credential());
        using var http = new HttpClient(new Handler((r, ct) => Task.FromResult(Reply(body))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatGptOAuthService(httpClient: http).GetAccessTokenAsync(new(), secrets));
    }

    [Theory]
    [InlineData("invalid_grant", false)]
    [InlineData("refresh_token_reused", false)]
    [InlineData("temporarily_unavailable", true)]
    public async Task RefreshFailure_PreservesRegistrationAndOnlyClearsTerminalTokens(string code, bool keepTokens)
    {
        var secrets = new Secrets();
        ChatGptCredentialStore.Save(secrets, Credential());
        using var http = new HttpClient(new Handler((r, ct) => Task.FromResult(Reply($"{{\"error\":\"{code}\"}}", HttpStatusCode.BadRequest))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatGptOAuthService(httpClient: http).GetAccessTokenAsync(new(), secrets));
        var saved = ChatGptCredentialStore.Load(secrets)!;
        Assert.Equal("oaiapp_fixture", saved.ClientId);
        Assert.Equal("subject", saved.Subject);
        Assert.Equal(keepTokens, saved.RefreshToken is not null);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task SignOut_WaitsForRefreshAndCannotResurrectTokens(HttpStatusCode revokeStatus, bool confirmed)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-session-{Guid.NewGuid():N}.json");
        try
        {
            var secrets = new Secrets();
            ChatGptCredentialStore.Save(secrets, Credential());
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new Handler(async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(ct);
                    return Reply("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
                }
                if (request.Method == HttpMethod.Get)
                    return Reply("{\"revocation_endpoint\":\"https://auth.openai.com/api/accounts/oauth/revoke\"}");
                Assert.Equal("auth.openai.com", request.RequestUri.Host);
                Assert.Contains("token=new-refresh", await request.Content!.ReadAsStringAsync(ct));
                return Reply("", revokeStatus);
            }));
            var oauth = new ChatGptOAuthService(httpClient: http, settingsPath: path);
            var settings = new AppSettings();
            var refresh = oauth.GetAccessTokenAsync(settings, secrets);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var signOut = oauth.SignOutAsync(settings, secrets);
            release.SetResult();
            await refresh;
            Assert.Equal(confirmed, await signOut);
            var saved = ChatGptCredentialStore.Load(secrets)!;
            Assert.Null(saved.AccessToken);
            Assert.Null(saved.RefreshToken);
            Assert.Equal("oaiapp_fixture", saved.ClientId);
            Assert.Equal("subject", saved.Subject);
            Assert.Equal(AuthProvider.ChatGPT, settings.AuthProvider);
            Assert.DoesNotContain("new-access", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("signature")]
    [InlineData("unsigned")]
    [InlineData("nonce")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    public async Task IdentityValidation_RequiresSignatureAndBoundClaims(string scenario)
    {
        using var rsa = RSA.Create(2048);
        using var wrongRsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
        var signingKey = scenario == "signature" ? new RsaSecurityKey(wrongRsa) { KeyId = "fixture" } : key;
        var jwt = new JwtSecurityToken(
            scenario == "issuer" ? "https://example.com" : "https://auth.openai.com",
            scenario == "audience" ? "other-client" : "oaiapp_fixture",
            [new Claim("sub", "subject"), new Claim("nonce", scenario == "nonce" ? "wrong" : "pending-nonce"), new Claim("email", "test@example.com")],
            DateTime.UtcNow.AddHours(-2), scenario == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(10),
            scenario == "unsigned" ? null : new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "fixture" });
        var jwks = JsonSerializer.Serialize(new { keys = new[] { publicKey } });
        using var http = new HttpClient(new Handler((r, ct) => Task.FromResult(Reply(jwks))));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        if (scenario == "valid")
        {
            var identity = await ChatGptIdTokenValidator.ValidateAsync(token, "oaiapp_fixture", "pending-nonce", httpClient: http);
            Assert.Equal("subject", identity.Subject);
        }
        else await Assert.ThrowsAnyAsync<Exception>(() => ChatGptIdTokenValidator.ValidateAsync(token, "oaiapp_fixture", "pending-nonce", httpClient: http));
    }

    [Fact]
    public async Task JwksFailure_RejectsSignInRatherThanTrustingClaims()
    {
        using var http = new HttpClient(new Handler((r, ct) => throw new HttpRequestException("offline")));
        await Assert.ThrowsAsync<HttpRequestException>(() => ChatGptIdTokenValidator.ValidateAsync("untrusted", "client", "nonce", httpClient: http));
    }

    [Fact]
    public void ReturningIdentity_CannotReplaceAccountUsingSameEmail()
    {
        var existing = Credential();
        Assert.Throws<InvalidOperationException>(() => ChatGptOAuthService.ValidateReturningIdentity(existing, existing.ClientId!, new("other-subject", existing.Email)));
        ChatGptOAuthService.ValidateReturningIdentity(existing, existing.ClientId!, new(existing.Subject!, "changed@example.com"));
    }

    [Theory]
    [InlineData("good", true)]
    [InlineData("bad-state", false)]
    [InlineData("wrong-path", false)]
    public async Task LoopbackCallback_AcceptsExactDocumentedUriAndRejectsUnboundReturns(string scenario, bool accepted)
    {
        using var listener = ChatGptOAuthService.CreateLoopbackListener(out var redirect);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = ChatGptOAuthService.WaitForCallbackAsync(listener, "pending-state", cts.Token);
        using var http = new HttpClient();
        var uri = scenario == "wrong-path" ? redirect.Replace("/auth/callback", "/callback") : redirect;
        var state = scenario == "bad-state" ? "other-state" : "pending-state";
        using var response = await http.GetAsync(uri + "?state=" + state + "&code=fixture&client_id=oaiapp_fixture", cts.Token);
        if (accepted)
        {
            var callback = await pending;
            Assert.Equal("fixture", callback.Code);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("sign-in complete", await response.Content.ReadAsStringAsync(cts.Token));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private static ChatGptCredential Credential() => new()
    {
        Subject = "subject", ClientId = "oaiapp_fixture", AccessToken = "old-access", RefreshToken = "old-refresh",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1), Scopes = ["chatgpt.tokens.use.direct"], Email = "test@example.com",
    };

    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }

    private sealed class Secrets : ISecretStore
    {
        private string? _value;
        public string? Get(string name) => _value;
        public void Set(string name, string value) => _value = value;
        public void Delete(string name) => _value = null;
    }
}
