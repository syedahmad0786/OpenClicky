using System.Net;
using System.Text;
using System.Text.Json;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class OpenCodeTaskClientTests
{
    [Fact]
    public async Task HealthAndCreateSession_UseOfficialPathsAndPayload()
    {
        var call = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            call++;
            if (call == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/global/health", request.RequestUri!.AbsolutePath);
                return Json("{\"healthy\":true,\"version\":\"1.18.25\"}");
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/session", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("OpenClicky task", body.RootElement.GetProperty("title").GetString());
            return Json("{\"id\":\"ses_123\",\"title\":\"OpenClicky task\"}");
        }));
        using var client = new OpenCodeTaskClient("http://127.0.0.1:4096/", null, null, http);

        var health = await client.GetHealthAsync();
        var session = await client.CreateSessionAsync("OpenClicky task");

        Assert.True(health.Healthy);
        Assert.Equal("1.18.25", health.Version);
        Assert.Equal("ses_123", session.Id);
        Assert.Equal(2, call);
    }

    [Fact]
    public async Task PromptMessagesStatusAndAbort_UseBoundedTypedContract()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            paths.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.RequestUri.AbsolutePath.EndsWith("/prompt_async", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var part = Assert.Single(body.RootElement.GetProperty("parts").EnumerateArray());
                Assert.Equal("text", part.GetProperty("type").GetString());
                Assert.Equal("Fix the tests", part.GetProperty("text").GetString());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/message", StringComparison.Ordinal))
            {
                return Json("""
                    [
                      {"info":{"id":"msg_user","role":"user"},"parts":[{"type":"text","text":"Fix the tests"}]},
                      {"info":{"id":"msg_ai","role":"assistant","finish":"stop"},"parts":[{"type":"text","text":"Done"}]}
                    ]
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/session/status")
            {
                return Json("{\"ses_123\":{\"type\":\"busy\"}}");
            }

            Assert.EndsWith("/abort", request.RequestUri.AbsolutePath);
            return Json("true");
        }));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        await client.SendPromptAsync("ses_123", "Fix the tests");
        var snapshot = await client.GetMessagesAsync("ses_123");
        await client.AbortAsync("ses_123");

        Assert.Equal("busy", snapshot.Status.Type);
        Assert.Equal(2, snapshot.Messages.Count);
        Assert.Equal("Done", snapshot.Messages[1].Text);
        Assert.Equal("stop", snapshot.Messages[1].FinishReason);
        Assert.Equal(new[]
        {
            "POST /session/ses_123/prompt_async",
            "GET /session/ses_123/message",
            "GET /session/status",
            "POST /session/ses_123/abort",
        }, paths);
    }

    [Fact]
    public async Task ProvidersAndPermissions_UseInstalledVersionContract()
    {
        var call = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            call++;
            if (call == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/provider", request.RequestUri!.AbsolutePath);
                return Json("""
                    {
                      "all":[{"id":"opencode","models":{"big-pickle":{},"small-free":{}}}],
                      "default":{"opencode":"big-pickle"},
                      "connected":["opencode"]
                    }
                    """);
            }

            if (call == 2)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/permission", request.RequestUri!.AbsolutePath);
                return Json("""
                    [{
                      "id":"per_123",
                      "sessionID":"ses_123",
                      "permission":"bash",
                      "patterns":["dotnet test *"],
                      "metadata":{},
                      "always":["dotnet test *"],
                      "tool":{"messageID":"msg_1","callID":"call_1"}
                    }]
                    """);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(call == 3 ? "/permission/per_123/reply" : "/permission/per_456/reply", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(call == 3 ? "once" : "reject", body.RootElement.GetProperty("reply").GetString());
            return Json("true");
        }));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        var providers = await client.GetProviderStatusAsync();
        var permissions = await client.GetPendingPermissionsAsync();
        await client.ReplyToPermissionAsync("per_123", allowOnce: true);
        await client.ReplyToPermissionAsync("per_456", allowOnce: false);

        Assert.Equal(["opencode"], providers.Connected);
        Assert.Equal("big-pickle", providers.DefaultModels["opencode"]);
        Assert.Equal(["big-pickle", "small-free"], providers.Models["opencode"]);
        var permission = Assert.Single(permissions);
        Assert.Equal("ses_123", permission.SessionId);
        Assert.Equal("bash", permission.Permission);
        Assert.Equal(["dotnet test *"], permission.Patterns);
        Assert.Equal("call_1", permission.ToolCallId);
        Assert.Equal(4, call);
    }

    [Fact]
    public async Task Prompt_CanSelectAnExactProviderModel()
    {
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var model = body.RootElement.GetProperty("model");
            Assert.Equal("zai", model.GetProperty("providerID").GetString());
            Assert.Equal("glm-5v-turbo", model.GetProperty("modelID").GetString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        await client.SendPromptAsync(
            "ses_123",
            "Describe the workspace",
            new OpenCodeModelSelection("zai", "glm-5v-turbo"));
    }

    [Fact]
    public async Task ConfiguredModel_ReadsOnlyTheModelIdentifier()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("/config", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json("{\"model\":\"zai/glm-5v-turbo\",\"provider\":{\"secret\":\"ignored\"}}"));
        }));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        Assert.Equal("zai/glm-5v-turbo", await client.GetConfiguredModelAsync());
    }

    [Fact]
    public async Task Messages_PreserveProviderErrorNameAndDetail()
    {
        var call = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            call++;
            return Task.FromResult(call == 1
                ? Json("""
                    [{"info":{"id":"msg_ai","role":"assistant","error":{"name":"ProviderAuthError","data":{"message":"Provider rejected the request"}}},"parts":[]}]
                    """)
                : Json("{}"));
        }));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        var snapshot = await client.GetMessagesAsync("ses_123");

        Assert.Equal("ProviderAuthError: Provider rejected the request", Assert.Single(snapshot.Messages).Error);
        Assert.Equal("idle", snapshot.Status.Type);
    }

    [Fact]
    public async Task Messages_SurfaceToolPartsWithTheirInput()
    {
        var call = 0;
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(++call == 1
            ? Json("""
                [{"info":{"id":"msg_ai","role":"assistant"},"parts":[
                  {"type":"text","text":"Working"},
                  {"type":"tool","callID":"call_1","tool":"todowrite","state":{"status":"completed","input":{"todos":[{"content":"A","status":"pending"}]},"output":"x"}},
                  {"type":"tool","callID":"call_2","tool":"openclicky_click","state":{"status":"pending","input":{}}},
                  {"type":"tool","tool":"bash","state":{"status":"running","input":{}}},
                  {"type":"tool","callID":"call_3","tool":"openclicky_hotkey","state":{"status":"running"}},
                  {"type":"step-start"}]}]
                """)
            : Json("{}"))));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        var message = Assert.Single((await client.GetMessagesAsync("ses_123")).Messages);

        Assert.Equal("Working", message.Text);
        Assert.Equal(["call_1", "call_2", "call_3"], message.Tools!.Select(t => t.CallId));
        Assert.Equal(["completed", "pending", "running"], message.Tools!.Select(t => t.Status));
        Assert.Equal("todowrite", message.Tools![0].Tool);
        Assert.Equal("A", message.Tools[0].Input!.Value.GetProperty("todos")[0].GetProperty("content").GetString());
        Assert.Null(message.Tools[2].Input);
    }

    [Theory]
    [InlineData("https://127.0.0.1:4096/")]
    [InlineData("http://127.0.0.2:4096/")]
    [InlineData("http://localhost.example:4096/")]
    [InlineData("http://user:pass@localhost:4096/")]
    [InlineData("http://localhost:4096/api")]
    [InlineData("http://localhost:4096/?directory=C%3A%5Cwork")]
    [InlineData("http://localhost:4096/#fragment")]
    public void EndpointValidation_RejectsAnythingExceptLoopbackOrigin(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => OpenCodeTaskClient.ValidateEndpoint(endpoint));
    }

    [Theory]
    [InlineData("http://127.0.0.1:4096/")]
    [InlineData("http://localhost:4096/")]
    [InlineData("http://[::1]:4096/")]
    public void EndpointValidation_AcceptsLiteralLoopbackOrigins(string endpoint)
    {
        Assert.Equal(endpoint, OpenCodeTaskClient.ValidateEndpoint(endpoint).AbsoluteUri);
    }

    [Fact]
    public async Task BasicAuth_IsSentWithoutAppearingInErrors()
    {
        const string username = "local-user";
        const string password = "private-password";
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal(
                $"{username}:{password}",
                Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));
        using var client = new OpenCodeTaskClient(
            "http://localhost:4096/",
            username,
            password,
            http);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetHealthAsync());

        Assert.DoesNotContain(username, error.Message);
        Assert.DoesNotContain(password, error.Message);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")), error.Message);
    }

    [Fact]
    public async Task Redirect_IsRejectedWithoutSecondRequest()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/steal") },
            });
        }));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetHealthAsync());

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_StopsPendingRequest()
    {
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        }));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetHealthAsync(cancellation.Token));
    }

    [Fact]
    public async Task OversizedResponse_IsRejectedBeforeParsing()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[OpenCodeTaskClient.MaxResponseBytes + 1]),
        })));
        using var client = new OpenCodeTaskClient("http://localhost:4096/", null, null, http);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetHealthAsync());
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }
}
