using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>Minimal Streamable HTTP MCP client for one user-added connector. Everything it returns is untrusted data.</summary>
public sealed class RemoteMcpConnector : JsonRpcMcpConnector
{
    private readonly HttpClient _http;
    private readonly ConnectorSecret _secret;
    private readonly Func<CancellationToken, Task<string>>? _bearer;
    private string? _sessionId;

    /// <param name="bearer">Supplies a fresh access token for every request (browser sign-in); it replaces any API-key Authorization header.</param>
    public RemoteMcpConnector(string key, string displayName, ConnectorSecret secret, HttpMessageHandler? handler = null,
        Func<CancellationToken, Task<string>>? bearer = null) : base(key, displayName)
    {
        ConnectorRegistry.ValidateUrl(secret.Url);
        _secret = secret;
        _bearer = bearer;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    }

    protected override async Task<JsonDocument?> SendAsync(string method, object? parameters, bool expectResult, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _secret.Url)
        {
            Content = new StringContent(Envelope(method, parameters, expectResult, out var id), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_secret.HeaderName is { } header && _secret.HeaderValue is { } value) request.Headers.TryAddWithoutValidation(header, value);
        foreach (var (name, headerValue) in _secret.Headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(name, headerValue);
        if (_bearer is not null)
        {
            var accessToken = await _bearer(token).ConfigureAwait(false);
            request.Headers.Remove("Authorization");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
        }
        if (_sessionId is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        if (Initialized) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessions)) _sessionId ??= sessions.FirstOrDefault();
        if (!response.IsSuccessStatusCode)
            throw new McpHttpException((int)response.StatusCode,
                $"{DisplayName} answered HTTP {(int)response.StatusCode} to {method}. Check its address and credential in Connections.");
        if (!expectResult) return null;
        var body = await ReadBoundedAsync(response, token).ConfigureAwait(false);
        return ResultOf(response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? FromEventStream(body, id!.Value) : body, method);
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

    public override void Dispose() => _http.Dispose();
}
