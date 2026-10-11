using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed class OpenRouterTalkClient : ITalkClient
{
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public OpenRouterTalkClient(string apiKey, string? baseUrl = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _baseUrl = (baseUrl ?? "https://openrouter.ai/api/v1").TrimEnd('/');
        _http = httpClient ?? DefaultHttp;
    }

    public async Task<string> CompleteVisionTalkAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string text)> history,
        string userTranscript,
        IReadOnlyList<ScreenCaptureResult> screens,
        string model,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
        };

        foreach (var (role, text) in history)
        {
            messages.Add(new { role, content = text });
        }

        var userContent = new List<object>
        {
            new { type = "text", text = userTranscript },
        };

        foreach (var screen in screens)
        {
            var label =
                $"{screen.Label} (image dimensions: {screen.PixelWidth}x{screen.PixelHeight} pixels)";
            userContent.Add(new { type = "text", text = label });
            userContent.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:image/jpeg;base64,{Convert.ToBase64String(screen.JpegBytes)}",
                },
            });
        }

        messages.Add(new { role = "user", content = userContent });

        var payload = new
        {
            model,
            messages,
            temperature = 0.7,
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/openclicky/OpenClicky");
        request.Headers.TryAddWithoutValidation("X-Title", "OpenClicky");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenRouter chat {(int)response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content");

        return content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? string.Empty
            : content.ToString();
    }
}
