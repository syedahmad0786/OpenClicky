using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed class OpenRouterAgentPlanner : IAgentPlanner
{
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    /// <summary>Who serves this OpenAI-compatible endpoint ("OpenRouter", "xAI"): error text, and OpenRouter's attribution headers.</summary>
    internal string Provider { get; }
    internal string BaseUrl => _baseUrl;

    public OpenRouterAgentPlanner(string apiKey, string? baseUrl = null, HttpClient? httpClient = null, string provider = "OpenRouter")
    {
        _apiKey = apiKey;
        Provider = provider;
        _baseUrl = (baseUrl ?? "https://openrouter.ai/api/v1").TrimEnd('/');
        _http = httpClient ?? DefaultHttp;
    }

    public async Task<AgentPlanStep> NextStepAsync(
        string task,
        IReadOnlyList<ScreenCaptureResult> screens,
        IReadOnlyList<string> priorResults,
        string model,
        CancellationToken cancellationToken = default)
    {
        var userContent = new List<object>
        {
            new { type = "text", text = $"task: {task}" },
        };

        if (priorResults.Count > 0)
        {
            userContent.Add(new
            {
                type = "text",
                text = "prior step results:\n" + string.Join("\n", priorResults),
            });
        }

        foreach (var screen in screens.Take(2))
        {
            userContent.Add(new
            {
                type = "text",
                text = $"{screen.Label} (image dimensions: {screen.PixelWidth}x{screen.PixelHeight} pixels)",
            });
            userContent.Add(new
            {
                type = "image_url",
                image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(screen.JpegBytes)}" },
            });
        }

        var payload = new
        {
            model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = CompanionPrompts.AgentSystemPrompt },
                new { role = "user", content = userContent },
            },
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (Provider == "OpenRouter")
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/openclicky/OpenClicky");
            request.Headers.TryAddWithoutValidation("X-Title", "OpenClicky");
        }
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{Provider} agent {(int)response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return ParseStep(content);
    }

    public static AgentPlanStep ParseStep(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                trimmed = trimmed[start..(end + 1)];
            }
        }

        using var doc = JsonDocument.Parse(trimmed);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
            throw new FormatException("The agent response did not contain a tool.");
        var tool = (toolEl.GetString() ?? "").Trim().ToLowerInvariant();
        if (tool is not ("uia_snapshot" or "screenshot" or "click" or "type_text" or "hotkey" or "scroll" or "wait" or "list_apps" or "open_app" or "done"))
            throw new FormatException("The agent selected an unsupported tool.");
        var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? string.Empty : string.Empty;
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in argsEl.EnumerateObject())
            {
                args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.ToString();
            }
        }

        var done = tool.Equals("done", StringComparison.OrdinalIgnoreCase);
        return new AgentPlanStep(tool, args, rationale, done);
    }
}
