using System.Net.Http.Headers;
using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed class OpenRouterSttClient : ISttClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    public OpenRouterSttClient(string apiKey, string model, string? baseUrl = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _model = model;
        _baseUrl = (baseUrl ?? "https://openrouter.ai/api/v1").TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wavBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "speech.wav");
        form.Add(new StringContent(_model), "model");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/openclicky/OpenClicky");
        request.Headers.TryAddWithoutValidation("X-Title", "OpenClicky");
        request.Content = form;

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenRouter STT {(int)response.StatusCode}: {body}");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("text", out var text))
        {
            return text.GetString() ?? string.Empty;
        }

        return body;
    }
}
