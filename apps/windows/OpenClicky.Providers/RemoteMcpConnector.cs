using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed record RemoteMcpTool(string Name, string Description, bool ReadOnly, string InputSchemaJson);
public sealed record RemoteMcpCallResult(bool IsError, string Text);

/// <summary>Minimal Streamable HTTP MCP client for one user-added connector. Everything it returns is untrusted data.</summary>
public sealed class RemoteMcpConnector : IDisposable
{
    public const int MaxResponseBytes = 1_048_576;
    public const int MaxTools = 200;
    private const string ProtocolVersion = "2025-06-18";
    private readonly HttpClient _http;
    private readonly ConnectorSecret _secret;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _sessionId;
    private bool _initialized;
    private int _nextId;
    private IReadOnlyList<RemoteMcpTool>? _tools;

    public RemoteMcpConnector(string key, string displayName, ConnectorSecret secret, HttpMessageHandler? handler = null)
    {
        ConnectorRegistry.ValidateUrl(secret.Url);
        Key = key;
        DisplayName = displayName;
        _secret = secret;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string Key { get; }
    public string DisplayName { get; }

    public async Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken token)
    {
        if (_tools is not null) return _tools;
        var tools = new List<RemoteMcpTool>();
        string? cursor = null;
        for (var page = 0; page < 5 && tools.Count < MaxTools; page++)
        {
            using var result = await RequestAsync("tools/list", cursor is null ? new { } : new { cursor }, token).ConfigureAwait(false);
            if (!result.RootElement.TryGetProperty("tools", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"{DisplayName} returned an invalid tool list.");
            foreach (var item in list.EnumerateArray().Take(MaxTools - tools.Count))
            {
                if (!item.TryGetProperty("name", out var name) || name.GetString() is not { Length: > 0 and <= 128 } toolName) continue;
                var description = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                var readOnly = item.TryGetProperty("annotations", out var a) && a.ValueKind == JsonValueKind.Object
                    && a.TryGetProperty("readOnlyHint", out var hint) && hint.ValueKind == JsonValueKind.True;
                var schema = item.TryGetProperty("inputSchema", out var s) ? s.GetRawText() : "{}";
                tools.Add(new(toolName, description[..Math.Min(description.Length, 500)], readOnly, schema[..Math.Min(schema.Length, 4_000)]));
            }
            cursor = result.RootElement.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (cursor is null) break;
        }
        return _tools = tools;
    }

    public async Task<RemoteMcpCallResult> CallToolAsync(string name, JsonElement arguments, CancellationToken token)
    {
        using var result = await RequestAsync("tools/call", new { name, arguments }, token).ConfigureAwait(false);
        var isError = result.RootElement.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        var text = new StringBuilder();
        if (result.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var item in content.EnumerateArray())
                text.AppendLine(item.TryGetProperty("type", out var type) && type.GetString() == "text" && item.TryGetProperty("text", out var t)
                    ? t.GetString() : $"[{(type.ValueKind == JsonValueKind.String ? type.GetString() : "unknown")} content omitted]");
        else if (result.RootElement.TryGetProperty("structuredContent", out var structured))
            text.Append(structured.GetRawText());
        var value = text.ToString().Trim();
        return new(isError, value[..Math.Min(value.Length, 20_000)]);
    }

    private async Task<JsonDocument> RequestAsync(string method, object parameters, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!_initialized)
            {
                using (await SendAsync("initialize", new
                {
                    protocolVersion = ProtocolVersion,
                    capabilities = new { },
                    clientInfo = new { name = "OpenClicky", version = "0.7" },
                }, expectResult: true, token).ConfigureAwait(false)) { }
                using (await SendAsync("notifications/initialized", null, expectResult: false, token).ConfigureAwait(false)) { }
                _initialized = true;
            }
            return (await SendAsync(method, parameters, expectResult: true, token).ConfigureAwait(false))!;
        }
        finally { _gate.Release(); }
    }

    private async Task<JsonDocument?> SendAsync(string method, object? parameters, bool expectResult, CancellationToken token)
    {
        var id = expectResult ? ++_nextId : (int?)null;
        var envelope = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method };
        if (id is not null) envelope["id"] = id;
        if (parameters is not null) envelope["params"] = parameters;
        using var request = new HttpRequestMessage(HttpMethod.Post, _secret.Url)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_secret.HeaderName is { } header && _secret.HeaderValue is { } value) request.Headers.TryAddWithoutValidation(header, value);
        if (_sessionId is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        if (_initialized) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessions)) _sessionId ??= sessions.FirstOrDefault();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{DisplayName} answered HTTP {(int)response.StatusCode} to {method}. Check its address and credential in Connections.");
        if (!expectResult) return null;
        var body = await ReadBoundedAsync(response, token).ConfigureAwait(false);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? FromEventStream(body, id!.Value) : body;
        var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
            document.Dispose();
            throw new InvalidOperationException($"{DisplayName} rejected {method}: {message?[..Math.Min(message.Length, 300)]}");
        }
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidDataException($"{DisplayName} returned no result for {method}.");
        }
        var clone = JsonDocument.Parse(result.GetRawText());
        document.Dispose();
        return clone;
    }

    internal static string FromEventStream(string body, int id)
    {
        foreach (var line in body.Split('\n'))
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            try
            {
                using var candidate = JsonDocument.Parse(data);
                if (candidate.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id)
                    return data;
            }
            catch (JsonException) { }
        }
        throw new InvalidDataException("The connector's event stream did not contain the response.");
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16_384];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > MaxResponseBytes) throw new InvalidDataException("The connector's response was too large.");
            memory.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
