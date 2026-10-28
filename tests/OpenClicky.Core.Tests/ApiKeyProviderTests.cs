using System.Net;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

/// <summary>Claude (Anthropic) and Grok (xAI) as API-key providers. No network: requests go to a recording handler.</summary>
[Collection("OfflineEnv")]
public class ApiKeyProviderTests
{
    private sealed class Secrets : ISecretStore
    {
        public readonly Dictionary<string, string> Values = [];
        public string? Get(string name) => Values.GetValueOrDefault(name);
        public void Set(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }

    private sealed class Recorder(string body, string mediaType = "application/json") : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string RequestBody = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        }
    }

    private static readonly ScreenCaptureResult Screen = new([1, 2, 3], 1920, 1080, 0, 0, 1920, 1080, true, "Screen 1");

    private static Secrets With(string name, string key) { var s = new Secrets(); s.Set(name, key); return s; }

    [Fact]
    public void FactoryPicksTheClientAndEndpointPerProvider()
    {
        var claude = new AppSettings { AuthProvider = AuthProvider.Anthropic };
        var anthropic = With(SecretNames.AnthropicApiKey, " sk-ant-test ");
        Assert.Equal("https://api.anthropic.com/v1", Assert.IsType<AnthropicTalkClient>(ProviderFactory.CreateTalkClient(claude, anthropic)).BaseUrl);
        Assert.IsType<AnthropicAgentPlanner>(ProviderFactory.CreateAgentPlanner(claude, anthropic));

        var grok = new AppSettings { AuthProvider = AuthProvider.Xai, OpenRouterApiKey = "sk-or-ignored" };
        var xai = With(SecretNames.XaiApiKey, "xai-test");
        var talk = Assert.IsType<OpenRouterTalkClient>(ProviderFactory.CreateTalkClient(grok, xai));
        Assert.Equal(("https://api.x.ai/v1", "xAI"), (talk.BaseUrl, talk.Provider));
        var planner = Assert.IsType<OpenRouterAgentPlanner>(ProviderFactory.CreateAgentPlanner(grok, xai));
        Assert.Equal(("https://api.x.ai/v1", "xAI"), (planner.BaseUrl, planner.Provider));
    }

    [Fact]
    public void FactoryReturnsNullWithoutTheProvidersOwnKey()
    {
        // Another provider's key never stands in: an OpenRouter key or the Anthropic key does not serve Grok.
        var crossed = With(SecretNames.AnthropicApiKey, "sk-ant-test");
        var grok = new AppSettings { AuthProvider = AuthProvider.Xai, OpenRouterApiKey = "sk-or-test" };
        Assert.Null(ProviderFactory.CreateTalkClient(grok, crossed));
        Assert.Null(ProviderFactory.CreateAgentPlanner(grok, crossed));
        var claude = new AppSettings { AuthProvider = AuthProvider.Anthropic };
        Assert.Null(ProviderFactory.CreateTalkClient(claude, null));
        Assert.Null(ProviderFactory.CreateAgentPlanner(claude, With(SecretNames.AnthropicApiKey, "  ")));
    }

    [Fact]
    public void ModelsResolvePerProviderWithDefaults()
    {
        Assert.Equal("claude-sonnet-5-5", new AppSettings { AuthProvider = AuthProvider.Anthropic }.ResolveTalkModel());
        Assert.Equal("grok-4", new AppSettings { AuthProvider = AuthProvider.Xai, XaiModel = " " }.ResolveVisionModel());
        Assert.Equal("grok-4-fast", new AppSettings { AuthProvider = AuthProvider.Xai, XaiModel = " grok-4-fast " }.ResolveVisionModel());
    }

    [Fact]
    public async Task AnthropicTalkSendsNativeMessagesRequestAndStreamsText()
    {
        const string sse = """
            event: message_start
            data: {"type":"message_start","message":{"id":"m","content":[]}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"hidden"}}

            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Hello "}}

            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"there"}}

            data: {"type":"message_stop"}

            """;
        var handler = new Recorder(sse, "text/event-stream");
        var client = new AnthropicTalkClient("sk-ant-test", httpClient: new HttpClient(handler));
        var seen = new List<string>();
        var reply = await client.StreamVisionTalkAsync("sys", [("user", "earlier"), ("assistant", "")], "what is this?", [Screen], "claude-sonnet-5-5", seen.Add);

        Assert.Equal("Hello there", reply);
        Assert.Equal(["Hello ", "Hello there"], seen);
        var req = handler.Request!;
        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
        Assert.Equal("sk-ant-test", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", req.Headers.GetValues("anthropic-version").Single());
        Assert.Null(req.Headers.Authorization);
        using var body = JsonDocument.Parse(handler.RequestBody);
        var root = body.RootElement;
        Assert.Equal("sys", root.GetProperty("system").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.TryGetProperty("temperature", out _));
        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength()); // the empty assistant turn is dropped
        var image = messages[1].GetProperty("content")[2];
        Assert.Equal("image", image.GetProperty("type").GetString());
        Assert.Equal("base64", image.GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("AQID", image.GetProperty("source").GetProperty("data").GetString());
    }

    [Fact]
    public async Task AnthropicStreamErrorThrows()
    {
        var handler = new Recorder("data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}\n\n", "text/event-stream");
        var client = new AnthropicTalkClient("k", httpClient: new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteVisionTalkAsync("s", [], "hi", [], "m"));
    }

    [Fact]
    public async Task AnthropicPlannerParsesTheTextBlock()
    {
        var handler = new Recorder("""{"content":[{"type":"thinking","thinking":""},{"type":"text","text":"{\"tool\":\"done\",\"args\":{\"summary\":\"ok\"}}"}]}""");
        var planner = new AnthropicAgentPlanner("sk-ant-test", httpClient: new HttpClient(handler));
        var step = await planner.NextStepAsync("task", [Screen], [], "claude-sonnet-5-5");
        Assert.True(step.Done);
        Assert.Equal("ok", step.Arguments["summary"]);
        Assert.Equal("sk-ant-test", handler.Request!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task XaiUsesBearerWithoutOpenRouterAttribution()
    {
        var handler = new Recorder("""{"choices":[{"message":{"content":"hi"}}]}""");
        var client = new OpenRouterTalkClient("xai-test", ProviderFactory.XaiBaseUrl, new HttpClient(handler), provider: "xAI");
        Assert.Equal("hi", await client.CompleteVisionTalkAsync("s", [], "hello", [Screen], "grok-4"));
        var req = handler.Request!;
        Assert.Equal("https://api.x.ai/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer xai-test", req.Headers.Authorization!.ToString());
        Assert.False(req.Headers.Contains("HTTP-Referer"));
        Assert.False(req.Headers.Contains("X-Title"));
    }
}
