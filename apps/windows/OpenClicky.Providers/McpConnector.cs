using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenClicky.Providers;

public sealed record RemoteMcpTool(string Name, string Description, bool ReadOnly, string InputSchemaJson);
public sealed record RemoteMcpCallResult(bool IsError, string Text);

/// <summary>One connected account the agent can reach through connector_tools/connector_call. Everything it returns is untrusted data.</summary>
public abstract class McpConnector : IDisposable
{
    protected McpConnector(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }

    public string Key { get; }
    public string DisplayName { get; }
    /// <summary>The account label (Work, Personal…) shown next to the name, or null.</summary>
    public string? Label { get; init; }

    public abstract Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken token);
    public abstract Task<RemoteMcpCallResult> CallToolAsync(string name, JsonElement arguments, CancellationToken token);
    public abstract void Dispose();
}

/// <summary>A non-2xx answer from a remote connector, with a status code callers can read (401 → sign in again).</summary>
public sealed class McpHttpException(int statusCode, string message) : HttpRequestException(message, null, (HttpStatusCode)statusCode)
{
    public new int StatusCode { get; } = statusCode;
}

/// <summary>
/// The MCP client logic shared by every JSON-RPC transport: the lazy initialize handshake (one request at a time),
/// tool paging and caps, tool calls, and envelope/error unwrapping. Subclasses only move messages.
/// </summary>
public abstract class JsonRpcMcpConnector : McpConnector
{
    public const int MaxResponseBytes = 1_048_576;
    public const int MaxTools = 200;
    protected const string ProtocolVersion = "2025-06-18";
    // Never disposed: a SemaphoreSlim without a wait handle holds nothing, and disposing it would turn a request that
    // ends because the connector was disposed into an ObjectDisposedException instead of its own error.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _nextId;
    private IReadOnlyList<RemoteMcpTool>? _tools;

    protected JsonRpcMcpConnector(string key, string displayName) : base(key, displayName) { }

    /// <summary>True once initialize and notifications/initialized have been sent.</summary>
    protected bool Initialized { get; private set; }

    public override async Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken token)
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

    public override async Task<RemoteMcpCallResult> CallToolAsync(string name, JsonElement arguments, CancellationToken token)
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
            if (!Initialized)
            {
                using (await SendAsync("initialize", new
                {
                    protocolVersion = ProtocolVersion,
                    capabilities = new { },
                    clientInfo = new { name = "OpenClicky", version = "0.7" },
                }, expectResult: true, token).ConfigureAwait(false)) { }
                using (await SendAsync("notifications/initialized", null, expectResult: false, token).ConfigureAwait(false)) { }
                Initialized = true;
            }
            return (await SendAsync(method, parameters, expectResult: true, token).ConfigureAwait(false))!;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Sends one request (or notification when <paramref name="expectResult"/> is false) and returns its <c>result</c> object.</summary>
    protected abstract Task<JsonDocument?> SendAsync(string method, object? parameters, bool expectResult, CancellationToken token);

    /// <summary>Builds the JSON-RPC envelope; requests get the next numeric id. Called only from <see cref="SendAsync"/>, under the gate.</summary>
    protected string Envelope(string method, object? parameters, bool expectResult, out int? id)
    {
        id = expectResult ? ++_nextId : null;
        var envelope = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method };
        if (id is not null) envelope["id"] = id;
        if (parameters is not null) envelope["params"] = parameters;
        return JsonSerializer.Serialize(envelope);
    }

    /// <summary>Unwraps a response envelope into a copy of its <c>result</c> object, or throws the server's (capped) error.</summary>
    protected JsonDocument ResultOf(string json, string method)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() : "unknown error";
            throw new InvalidOperationException($"{DisplayName} rejected {method}: {message?[..Math.Min(message.Length, 300)]}");
        }
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{DisplayName} returned no result for {method}.");
        return JsonDocument.Parse(result.GetRawText());
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
}
