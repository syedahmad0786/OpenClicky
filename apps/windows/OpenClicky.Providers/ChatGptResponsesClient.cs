using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// ChatGPT plan usage via the public Responses API (OAuth bearer).
/// https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference
/// </summary>
public sealed class ChatGptResponsesClient
{
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _accessToken;

    public ChatGptResponsesClient(
        Func<CancellationToken, Task<string>> accessTokenFactory,
        HttpClient? httpClient = null)
    {
        _accessToken = accessTokenFactory;
        _http = httpClient ?? DefaultHttp;
    }

    public async Task<string> CompleteAsync(
        string? instructions,
        IReadOnlyList<object> input,
        string model,
        CancellationToken cancellationToken = default)
    {
        var token = await _accessToken(cancellationToken).ConfigureAwait(false);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["input"] = input,
            ["store"] = false,
            ["stream"] = true,
        };
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            payload["instructions"] = instructions;
        }

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var bodyStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using var readerErr = new StreamReader(bodyStream);
            var err = await readerErr.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"ChatGPT Responses {(int)response.StatusCode}: {err}");
        }

        return await ReadSseTextAsync(bodyStream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadSseTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var text = new StringBuilder();
        var completed = false;
        string? failCode = null;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data is "" or "[DONE]")
            {
                continue;
            }

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "response.output_text.delta":
                    if (root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                    {
                        text.Append(delta.GetString());
                    }

                    break;
                case "response.failed":
                    failCode = TryErrorCode(root) ?? "response.failed";
                    break;
                case "response.completed":
                    completed = true;
                    break;
            }
        }

        if (failCode is not null)
        {
            throw new InvalidOperationException($"ChatGPT Responses failed: {failCode}");
        }

        if (!completed && text.Length == 0)
        {
            throw new InvalidOperationException("ChatGPT Responses stream ended without response.completed.");
        }

        return text.ToString();
    }

    private static string? TryErrorCode(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var response)
            || !response.TryGetProperty("error", out var error)
            || error.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return error.TryGetProperty("code", out var code) ? code.GetString() : error.ToString();
    }
}
