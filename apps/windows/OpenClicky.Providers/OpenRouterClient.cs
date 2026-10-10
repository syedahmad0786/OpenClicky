using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenClicky.Providers;

/// <summary>
/// Minimal OpenAI-compatible chat client for OpenRouter (Phase B wiring).
/// Phase A does not call this.
/// </summary>
public sealed class OpenRouterClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public OpenRouterClient(string apiKey, string? baseUrl = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _baseUrl = (baseUrl ?? "https://openrouter.ai/api/v1").TrimEnd('/');
        _http = httpClient ?? new HttpClient();
    }

    public async Task<string> ChatCompletionsRawAsync(string jsonBody, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/openclicky/OpenClicky");
        request.Headers.TryAddWithoutValidation("X-Title", "OpenClicky");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenRouter error {(int)response.StatusCode}: {text}");
        }

        return text;
    }

    public static string BuildVisionUserMessage(string transcript, IEnumerable<(string label, string jpegBase64)> images)
    {
        var content = new List<object>
        {
            new { type = "text", text = transcript },
        };

        foreach (var (label, jpegBase64) in images)
        {
            content.Add(new { type = "text", text = label });
            content.Add(new
            {
                type = "image_url",
                image_url = new { url = $"data:image/jpeg;base64,{jpegBase64}" },
            });
        }

        return JsonSerializer.Serialize(new
        {
            messages = new object[]
            {
                new { role = "user", content },
            },
        });
    }
}
