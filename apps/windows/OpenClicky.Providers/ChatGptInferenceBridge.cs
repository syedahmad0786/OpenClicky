using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenClicky.Providers;

/// <summary>A credential-isolating loopback bridge from OpenCode to ChatGPT plan inference.</summary>
public sealed class ChatGptInferenceBridge : IAsyncDisposable
{
    private static readonly Uri UpstreamOrigin = new("https://api.openai.com/");
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private readonly HttpClient _upstream;
    private readonly bool _ownsUpstream;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _activeGate = new();
    private readonly HashSet<Task> _active = [];
    private readonly Task _serveTask;

    private ChatGptInferenceBridge(Func<CancellationToken, Task<string>> accessTokenFactory, HttpClient? upstreamClient)
    {
        _accessToken = accessTokenFactory;
        _ownsUpstream = upstreamClient is null;
        _upstream = upstreamClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        EphemeralToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var port = ReservePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}/v1/");
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _serveTask = ServeAsync(_stop.Token);
    }

    public Uri BaseUri { get; }
    public string EphemeralToken { get; }

    public static Task<ChatGptInferenceBridge> StartAsync(
        Func<CancellationToken, Task<string>> accessTokenFactory,
        HttpClient? upstreamClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessTokenFactory);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatGptInferenceBridge(accessTokenFactory, upstreamClient));
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Close();
        try { await _serveTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        Task[] active;
        lock (_activeGate) active = [.. _active];
        try { await Task.WhenAll(active).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
        if (_ownsUpstream) _upstream.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            var task = HandleSafelyAsync(context, cancellationToken);
            lock (_activeGate) _active.Add(task);
            _ = task.ContinueWith(
                completed => { lock (_activeGate) _active.Remove(completed); },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleSafelyAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try { await HandleAsync(context, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !context.Response.OutputStream.CanWrite) { }
        catch (Exception ex)
        {
            if (context.Response.OutputStream.CanWrite)
                await WriteErrorAsync(
                    context.Response,
                    ex is InvalidDataException ? HttpStatusCode.BadRequest : HttpStatusCode.BadGateway,
                    ex is InvalidDataException ? "invalid_request" : "upstream_error",
                    CancellationToken.None).ConfigureAwait(false);
        }
        finally { try { context.Response.Close(); } catch { } }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken bridgeCancellation)
    {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(bridgeCancellation);
        var cancellationToken = requestCancellation.Token;
        if (!string.Equals(context.Request.UserHostName, $"127.0.0.1:{BaseUri.Port}", StringComparison.OrdinalIgnoreCase)
            || context.Request.RemoteEndPoint?.Address is not { } address || !IPAddress.IsLoopback(address))
        {
            await WriteErrorAsync(context.Response, HttpStatusCode.BadRequest, "invalid_host", cancellationToken).ConfigureAwait(false); return;
        }
        if (!Authenticate(context.Request))
        {
            context.Response.Headers[HttpResponseHeader.WwwAuthenticate] = "Bearer";
            await WriteErrorAsync(context.Response, HttpStatusCode.Unauthorized, "unauthorized", cancellationToken).ConfigureAwait(false); return;
        }

        var path = context.Request.Url?.AbsolutePath;
        if (context.Request.HttpMethod == "GET" && path == "/v1/models") await ProxyModelsAsync(context.Response, cancellationToken).ConfigureAwait(false);
        else if (context.Request.HttpMethod == "POST" && path == "/v1/responses") await ProxyResponsesAsync(context.Request, context.Response, cancellationToken).ConfigureAwait(false);
        else await WriteErrorAsync(context.Response, HttpStatusCode.NotFound, "unsupported_route", cancellationToken).ConfigureAwait(false);
    }

    private bool Authenticate(HttpListenerRequest request)
    {
        var value = request.Headers["Authorization"];
        if (value is null || !value.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var supplied = Encoding.UTF8.GetBytes(value[7..]);
        var expected = Encoding.UTF8.GetBytes(EphemeralToken);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private async Task ProxyModelsAsync(HttpListenerResponse local, CancellationToken cancellationToken)
    {
        using var upstream = await SendUpstreamAsync(HttpMethod.Get, "v1/models", null, cancellationToken).ConfigureAwait(false);
        if (!upstream.IsSuccessStatusCode) { await RelayFailureAsync(local, upstream, cancellationToken).ConfigureAwait(false); return; }
        var bytes = await ReadBoundedAsync(upstream.Content, ChatGptInferenceProtocol.MaxModelsResponseBytes, cancellationToken).ConfigureAwait(false);
        var adapted = ChatGptInferenceProtocol.AdaptModelsResponse(bytes);
        await WriteAsync(local, HttpStatusCode.OK, "application/json", adapted, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProxyResponsesAsync(HttpListenerRequest localRequest, HttpListenerResponse localResponse, CancellationToken cancellationToken)
    {
        if (localRequest.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        { await WriteErrorAsync(localResponse, HttpStatusCode.UnsupportedMediaType, "invalid_content_type", cancellationToken).ConfigureAwait(false); return; }
        if (localRequest.ContentLength64 > ChatGptInferenceProtocol.MaxRequestBytes)
        { await WriteErrorAsync(localResponse, HttpStatusCode.RequestEntityTooLarge, "request_too_large", cancellationToken).ConfigureAwait(false); return; }
        var requestBytes = await ReadBoundedAsync(localRequest.InputStream, ChatGptInferenceProtocol.MaxRequestBytes, cancellationToken).ConfigureAwait(false);
        var adapted = ChatGptInferenceProtocol.AdaptResponsesRequest(requestBytes);
        using var content = new ByteArrayContent(adapted);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var upstream = await SendUpstreamAsync(HttpMethod.Post, "v1/responses", content, cancellationToken).ConfigureAwait(false);
        if (!upstream.IsSuccessStatusCode) { await RelayFailureAsync(localResponse, upstream, cancellationToken).ConfigureAwait(false); return; }

        localResponse.StatusCode = (int)upstream.StatusCode;
        localResponse.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "text/event-stream";
        localResponse.SendChunked = true;
        await using var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await stream.CopyToAsync(localResponse.OutputStream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendUpstreamAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var token = await _accessToken(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("ChatGPT access token is unavailable.");
        using var request = new HttpRequestMessage(method, new Uri(UpstreamOrigin, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(method == HttpMethod.Post ? "text/event-stream" : "application/json"));
        return await _upstream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RelayFailureAsync(HttpListenerResponse local, HttpResponseMessage upstream, CancellationToken cancellationToken)
    {
        // Keep upstream bodies (which may contain account detail) away from the child process.
        var status = (int)upstream.StatusCode is >= 400 and <= 599 ? upstream.StatusCode : HttpStatusCode.BadGateway;
        var bytes = await ReadBoundedAsync(upstream.Content, 64 * 1024, cancellationToken).ConfigureAwait(false);
        var upstreamCode = SafeUpstreamCode(bytes);
        var requestId = SafeHeader(upstream.Headers, "x-request-id") ?? SafeHeader(upstream.Headers, "openai-request-id");
        await WriteErrorAsync(local, status, "chatgpt_request_failed", cancellationToken, upstreamCode, requestId).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(
        HttpListenerResponse response,
        HttpStatusCode status,
        string code,
        CancellationToken cancellationToken,
        string? upstreamCode = null,
        string? requestId = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            error = new { code, message = "The ChatGPT inference bridge could not complete the request.", upstream_code = upstreamCode, request_id = requestId },
        });
        await WriteAsync(response, status, "application/json", bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string? SafeUpstreamCode(byte[] body)
    {
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var error = root.TryGetProperty("error", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : root;
            if (!error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String) return null;
            var value = code.GetString();
            return value is { Length: > 0 and <= 80 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.') ? value : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? SafeHeader(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values)) return null;
        var value = values.FirstOrDefault();
        return value is { Length: > 0 and <= 128 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.') ? value : null;
    }

    private static async Task WriteAsync(HttpListenerResponse response, HttpStatusCode status, string contentType, byte[] bytes, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)status; response.ContentType = contentType; response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Upstream response exceeded its size limit.");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadBoundedAsync(stream, limit, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (result.Length <= limit)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)result.Length)), cancellationToken).ConfigureAwait(false);
            if (read == 0) return result.ToArray();
            result.Write(buffer, 0, read);
        }
        throw new InvalidDataException("HTTP body exceeded its size limit.");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
