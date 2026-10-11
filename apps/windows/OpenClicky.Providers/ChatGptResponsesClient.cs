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
    private readonly Action<string> _log;

    public ChatGptResponsesClient(
        Func<CancellationToken, Task<string>> accessTokenFactory,
        HttpClient? httpClient = null,
        Action<string>? log = null)
    {
        _accessToken = accessTokenFactory;
        _http = httpClient ?? DefaultHttp;
        _log = log ?? (_ => { });
    }

    public async Task<IReadOnlyList<ChatGptModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var token = await _accessToken(cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw ApiFailure(response, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("models").EnumerateArray()
            .Where(m => m.TryGetProperty("visibility", out var v) && v.GetString() == "list")
            .Select(m => new ChatGptModel(m.GetProperty("slug").GetString()!, m.GetProperty("display_name").GetString()!))
            .Where(m => !string.IsNullOrWhiteSpace(m.Slug) && !string.IsNullOrWhiteSpace(m.DisplayName)).ToList();
    }

    public async Task<string> CompleteAsync(
        string? instructions,
        IReadOnlyList<object> input,
        string model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("Choose an available ChatGPT model in Settings.");
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
        _log($"chatgpt_response model={model} status={(int)response.StatusCode} request_id={RequestId(response)}");
        var bodyStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using var readerErr = new StreamReader(bodyStream);
            var err = await readerErr.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw ApiFailure(response, err);
        }

        try
        {
            var result = await ReadSseTextAsync(bodyStream, cancellationToken).ConfigureAwait(false);
            _log($"chatgpt_response completed=true model={model} request_id={RequestId(response)}");
            return result;
        }
        catch (Exception ex)
        {
            _log($"chatgpt_response completed=false error={ex.GetType().Name} request_id={RequestId(response)}");
            throw;
        }
    }

    private static async Task<string> ReadSseTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var text = new StringBuilder();

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
                case "response.incomplete":
                case "error":
                    throw new InvalidOperationException($"ChatGPT response stopped: {TryErrorCode(root) ?? type}. Review your connection and ChatGPT usage settings.");
                case "response.completed":
                    if (text.Length == 0) throw new InvalidOperationException("ChatGPT completed without a text answer.");
                    return text.ToString();
            }
        }

        throw new InvalidOperationException("ChatGPT Responses stream ended without response.completed. No partial answer was accepted.");
    }

    private static string? TryErrorCode(JsonElement root)
    {
        if (root.TryGetProperty("code", out var directCode)) return directCode.GetString();
        if (!root.TryGetProperty("response", out var response)
            || !response.TryGetProperty("error", out var error)
            || error.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return error.TryGetProperty("code", out var code) ? code.GetString() : error.ToString();
    }

    internal static string RequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-request-id", out var values) || response.Headers.TryGetValues("openai-request-id", out values)
            ? values.FirstOrDefault() ?? "unavailable" : "unavailable";

    private static InvalidOperationException ApiFailure(HttpResponseMessage response, string body)
    {
        var detail = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            detail = root.TryGetProperty("error", out var error) ? error.ToString()
                : root.TryGetProperty("detail", out var admission) ? admission.ToString() : body;
        }
        catch (JsonException) { }
        if (detail.Length > 1000) detail = detail[..1000];
        return new InvalidOperationException($"ChatGPT API HTTP {(int)response.StatusCode}, request {RequestId(response)}: {detail}\nManage plan usage: https://chatgpt.com/settings/usage");
    }
}

public sealed record ChatGptModel(string Slug, string DisplayName);
