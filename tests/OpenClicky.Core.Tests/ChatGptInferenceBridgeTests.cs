using System.Net;
using System.Text;
using System.Text.Json;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class ChatGptInferenceBridgeTests
{
    [Fact]
    public void Protocol_EnforcesSiwcHistoryAndNamespacesTools()
    {
        var source = Encoding.UTF8.GetBytes("""
            {"model":"gpt-test","input":[{"role":"system","content":"rules"},{"type":"function_call","call_id":"call-1","name":"openclicky_observe_screen","arguments":"{}"},{"type":"function_call_output","call_id":"call-1","output":"ok"}],"tools":[{"type":"function","name":"openclicky_observe_screen","description":"Observe","parameters":{"type":"object"}}],"temperature":0,"previous_response_id":"resp-old","store":true,"stream":false}
            """);

        using var adapted = JsonDocument.Parse(ChatGptInferenceProtocol.AdaptResponsesRequest(source));
        var root = adapted.RootElement;
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("previous_response_id", out _));
        Assert.Equal("developer", root.GetProperty("input")[0].GetProperty("role").GetString());
        Assert.Equal("opencode", root.GetProperty("input")[1].GetProperty("namespace").GetString());
        Assert.Equal("namespace", root.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.Equal("openclicky_observe_screen", root.GetProperty("tools")[0].GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"model\":\"x\",\"input\":\"not-history\"}")]
    [InlineData("{\"model\":\"x\",\"input\":[],\"tools\":[{\"type\":\"computer\"}]}")]
    public void Protocol_RejectsInvalidOrUnsupportedRequests(string json) =>
        Assert.Throws<InvalidDataException>(() => ChatGptInferenceProtocol.AdaptResponsesRequest(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("{\"model\":\"x\",\"input\":[{\"type\":\"function_call\",\"call_id\":\"c1\",\"name\":\"read\",\"arguments\":\"{}\"}]}")]
    [InlineData("{\"model\":\"x\",\"input\":[{\"type\":\"function_call_output\",\"call_id\":\"c1\",\"output\":\"x\"}]}")]
    public void Protocol_RejectsNonOpenClickyToolsAndOrphanedOutputs(string json) =>
        Assert.Throws<InvalidDataException>(() => ChatGptInferenceProtocol.AdaptResponsesRequest(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Protocol_StripsBuiltinCatalogToolsBeforeUpstream()
    {
        var source = Encoding.UTF8.GetBytes("""
            {"model":"x","input":[],"tools":[{"type":"function","name":"shell"},{"type":"function","name":"read"},{"type":"namespace","name":"builtins","description":"Internal tools","tools":[{"type":"function","name":"write"},{"type":"function","name":"glob"}]},{"type":"function","name":"openclicky_click"}]}
            """);

        using var adapted = JsonDocument.Parse(ChatGptInferenceProtocol.AdaptResponsesRequest(source));
        var nested = adapted.RootElement.GetProperty("tools")[0].GetProperty("tools");
        Assert.Single(nested.EnumerateArray());
        Assert.Equal("openclicky_click", nested[0].GetProperty("name").GetString());
        Assert.DoesNotContain("\"shell\"", Encoding.UTF8.GetString(ChatGptInferenceProtocol.AdaptResponsesRequest(source)));
    }

    [Fact]
    public void Protocol_RejectsCatalogWithoutAnOpenClickyTool()
    {
        var source = Encoding.UTF8.GetBytes("{\"model\":\"x\",\"input\":[],\"tools\":[{\"type\":\"function\",\"name\":\"shell\"},{\"type\":\"function\",\"name\":\"read\"}]}");
        Assert.Throws<InvalidDataException>(() => ChatGptInferenceProtocol.AdaptResponsesRequest(source));
    }

    [Fact]
    public async Task Bridge_IsolatesOAuthAndAdaptsModelsAndStreamingResponses()
    {
        var requests = new List<(Uri Uri, string? Authorization, string? Body)>();
        using var upstream = new HttpClient(new DelegateHandler(async (request, cancellationToken) =>
        {
            requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, "{\"models\":[{\"slug\":\"gpt-a\",\"display_name\":\"A\",\"visibility\":\"list\"},{\"slug\":\"hidden\",\"display_name\":\"H\",\"visibility\":\"hide\"}]}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"limit\"}}}\n\n", Encoding.UTF8, "text/event-stream"),
            };
        }));
        await using var bridge = await ChatGptInferenceBridge.StartAsync(_ => Task.FromResult("real-oauth"), upstream);
        using var child = new HttpClient { BaseAddress = bridge.BaseUri };
        child.DefaultRequestHeaders.Authorization = new("Bearer", bridge.EphemeralToken);

        var models = await child.GetStringAsync("models");
        using var modelJson = JsonDocument.Parse(models);
        Assert.Equal("gpt-a", modelJson.RootElement.GetProperty("data")[0].GetProperty("id").GetString());
        Assert.DoesNotContain("hidden", models);

        using var response = await child.PostAsync("responses", new StringContent("{\"model\":\"gpt-a\",\"input\":[]}", Encoding.UTF8, "application/json"));
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("response.failed", sse);
        Assert.All(requests, item => Assert.Equal("Bearer real-oauth", item.Authorization));
        Assert.DoesNotContain("real-oauth", bridge.EphemeralToken);
        Assert.Equal("api.openai.com", requests[0].Uri.Host);
    }

    [Fact]
    public async Task Bridge_RejectsMissingChildBearerWithoutCallingTokenFactory()
    {
        var tokenCalls = 0;
        using var upstream = new HttpClient(new DelegateHandler((_, _) => throw new Xunit.Sdk.XunitException("Upstream must not be called.")));
        await using var bridge = await ChatGptInferenceBridge.StartAsync(_ => { tokenCalls++; return Task.FromResult("secret"); }, upstream);
        using var child = new HttpClient { BaseAddress = bridge.BaseUri };
        using var response = await child.GetAsync("models");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, tokenCalls);
    }

    [Fact]
    public async Task Bridge_CancelsTheSingleUpstreamAttempt()
    {
        var attempts = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var upstream = new HttpClient(new DelegateHandler(async (_, cancellationToken) =>
        {
            attempts++;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }));
        await using var bridge = await ChatGptInferenceBridge.StartAsync(_ => Task.FromResult("oauth"), upstream);
        using var child = new HttpClient { BaseAddress = bridge.BaseUri };
        child.DefaultRequestHeaders.Authorization = new("Bearer", bridge.EphemeralToken);
        using var stop = new CancellationTokenSource();
        var pending = child.PostAsync("responses", new StringContent("{\"model\":\"gpt-a\",\"input\":[]}", Encoding.UTF8, "application/json"), stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, attempts);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
