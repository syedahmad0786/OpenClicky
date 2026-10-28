using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// Claude through Anthropic's native Messages API (POST /v1/messages) with the user's own key in x-api-key.
/// Native rather than the OpenAI-compatible endpoint: images go as base64 blocks, replies stream over SSE,
/// and no sampling parameters are sent (current Claude models reject non-default temperature).
/// </summary>
public static class AnthropicMessages
{
    public const string DefaultBaseUrl = "https://api.anthropic.com/v1", Version = "2023-06-01";
    // Thinking counts against max_tokens on current models; 16k keeps a non-streaming reply under HTTP timeouts.
    internal const int MaxTokens = 16_000;

    internal static object ImageBlock(ScreenCaptureResult screen) => new
    {
        type = "image",
        source = new { type = "base64", media_type = "image/jpeg", data = Convert.ToBase64String(screen.JpegBytes) },
    };

    internal static object Label(ScreenCaptureResult screen) => new
    {
        type = "text",
        text = $"{screen.Label} (image dimensions: {screen.PixelWidth}x{screen.PixelHeight} pixels)",
    };

    internal static HttpRequestMessage Request(string baseUrl, string apiKey, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/messages");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", Version);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>The reply text: every text block joined (thinking blocks are skipped).</summary>
    internal static string Text(JsonElement message) =>
        message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? string.Concat(content.EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(b => b.GetProperty("text").GetString()))
            : "";

    internal static async Task EnsureOkAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException($"Anthropic {what} {(int)response.StatusCode}: {body}");
    }
}

public sealed class AnthropicTalkClient : IStreamingTalkClient
{
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly HttpClient _http;
    private readonly string _apiKey;
    internal string BaseUrl { get; }

    public AnthropicTalkClient(string apiKey, string? baseUrl = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        BaseUrl = (baseUrl ?? AnthropicMessages.DefaultBaseUrl).TrimEnd('/');
        _http = httpClient ?? DefaultHttp;
    }

    public Task<string> CompleteVisionTalkAsync(string systemPrompt, IReadOnlyList<(string role, string text)> history, string userTranscript,
        IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken cancellationToken = default) =>
        StreamVisionTalkAsync(systemPrompt, history, userTranscript, screens, model, _ => { }, cancellationToken);

    public async Task<string> StreamVisionTalkAsync(string systemPrompt, IReadOnlyList<(string role, string text)> history, string userTranscript,
        IReadOnlyList<ScreenCaptureResult> screens, string model, Action<string> responseChanged, CancellationToken cancellationToken = default)
    {
        var messages = history.Where(h => !string.IsNullOrWhiteSpace(h.text)).Select(h => (object)new { role = h.role == "assistant" ? "assistant" : "user", content = h.text }).ToList();
        var user = new List<object> { new { type = "text", text = userTranscript } };
        foreach (var screen in screens)
        {
            user.Add(AnthropicMessages.Label(screen));
            user.Add(AnthropicMessages.ImageBlock(screen));
        }
        messages.Add(new { role = "user", content = user });

        using var request = AnthropicMessages.Request(BaseUrl, _apiKey,
            new { model, max_tokens = AnthropicMessages.MaxTokens, system = systemPrompt, messages, stream = true });
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await AnthropicMessages.EnsureOkAsync(response, "chat", cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadSseAsync(stream, responseChanged, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Accumulates text_delta events; an error event throws. Thinking deltas are ignored.</summary>
    internal static async Task<string> ReadSseAsync(Stream stream, Action<string> responseChanged, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            using var doc = JsonDocument.Parse(line.AsMemory(5));
            var root = doc.RootElement;
            switch (root.TryGetProperty("type", out var t) ? t.GetString() : null)
            {
                case "content_block_delta" when root.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta":
                    text.Append(delta.GetProperty("text").GetString());
                    responseChanged(text.ToString());
                    break;
                case "error":
                    throw new InvalidOperationException("Anthropic chat stream error: " + root.GetProperty("error"));
                case "message_stop":
                    return text.ToString();
            }
        }
        return text.ToString();
    }
}

public sealed class AnthropicAgentPlanner : IAgentPlanner
{
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly HttpClient _http;
    private readonly string _apiKey;
    internal string BaseUrl { get; }

    public AnthropicAgentPlanner(string apiKey, string? baseUrl = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        BaseUrl = (baseUrl ?? AnthropicMessages.DefaultBaseUrl).TrimEnd('/');
        _http = httpClient ?? DefaultHttp;
    }

    public async Task<AgentPlanStep> NextStepAsync(string task, IReadOnlyList<ScreenCaptureResult> screens, IReadOnlyList<string> priorResults,
        string model, CancellationToken cancellationToken = default)
    {
        var user = new List<object> { new { type = "text", text = $"task: {task}" } };
        if (priorResults.Count > 0) user.Add(new { type = "text", text = "prior step results:\n" + string.Join("\n", priorResults) });
        foreach (var screen in screens.Take(2))
        {
            user.Add(AnthropicMessages.Label(screen));
            user.Add(AnthropicMessages.ImageBlock(screen));
        }

        using var request = AnthropicMessages.Request(BaseUrl, _apiKey, new
        {
            model,
            max_tokens = AnthropicMessages.MaxTokens,
            system = CompanionPrompts.AgentSystemPrompt,
            messages = new object[] { new { role = "user", content = user } },
        });
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await AnthropicMessages.EnsureOkAsync(response, "agent", cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var text = AnthropicMessages.Text(doc.RootElement);
        return OpenRouterAgentPlanner.ParseStep(text.Length == 0 ? "{}" : text);
    }
}
