using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenClicky.Providers;

/// <summary>Authenticated AG-UI SSE adapter into the application's existing conversation controller.</summary>
public sealed class CompanionAgUiServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _active = new(1, 1);
    private readonly Func<string, Action<string, bool>, CancellationToken, Task> _run;
    private readonly List<Task> _handlers = [];
    private Task? _pump;

    public CompanionAgUiServer(Func<string, Action<string, bool>, CancellationToken, Task> run)
    {
        _run = run;
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/agent/");
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _listener.Prefixes.Add(Endpoint.AbsoluteUri);
    }

    public Uri Endpoint { get; }
    public string Token { get; }
    public void Start() { _listener.Start(); _pump = Task.Run(PumpAsync); }

    private async Task PumpAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token); }
            catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { break; }
            lock (_handlers) { _handlers.RemoveAll(task => task.IsCompleted); _handlers.Add(HandleAsync(context)); }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var acquired = false;
        using var job = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        job.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            var request = context.Request;
            var expected = Encoding.UTF8.GetBytes("Bearer " + Token);
            var supplied = Encoding.UTF8.GetBytes(request.Headers["Authorization"] ?? "");
            if (request.Url?.Authority != Endpoint.Authority || request.Url.AbsolutePath != Endpoint.AbsolutePath
                || !CryptographicOperations.FixedTimeEquals(supplied, expected) || request.Headers["Origin"] is not null)
            { context.Response.StatusCode = 401; return; }
            if (request.HttpMethod != "POST") { context.Response.StatusCode = 405; return; }
            if (request.ContentLength64 > 262144) { context.Response.StatusCode = 413; return; }
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            using (var readLimit = CancellationTokenSource.CreateLinkedTokenSource(job.Token))
            {
                readLimit.CancelAfter(TimeSpan.FromSeconds(15));
                int count;
                while ((count = await request.InputStream.ReadAsync(buffer, readLimit.Token)) != 0)
                {
                    if (body.Length + count > 262144) { context.Response.StatusCode = 413; return; }
                    body.Write(buffer, 0, count);
                }
            }
            var (thread, run, prompt) = ParseRequest(body.ToArray());
            acquired = await _active.WaitAsync(0, job.Token);
            if (!acquired) { context.Response.StatusCode = 409; return; }
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.SendChunked = true;
            var events = Channel.CreateBounded<object>(128);
            var messageId = Guid.NewGuid().ToString("N");
            async Task Write(object value)
            {
                var bytes = Encoding.UTF8.GetBytes("data: " + JsonSerializer.Serialize(value) + "\n\n");
                await context.Response.OutputStream.WriteAsync(bytes, job.Token);
                await context.Response.OutputStream.FlushAsync(job.Token);
            }
            async Task KeepAlive()
            {
                var bytes = Encoding.UTF8.GetBytes(": keepalive\n\n");
                await context.Response.OutputStream.WriteAsync(bytes, job.Token);
                await context.Response.OutputStream.FlushAsync(job.Token);
            }
            await Write(new { type = "RUN_STARTED", threadId = thread, runId = run });
            await Write(new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" });
            var previous = "";
            var production = Task.Run(async () =>
            {
                try
                {
                    await _run(prompt, (text, complete) =>
                    {
                        if (text.Length > 32000) throw new InvalidDataException("The response is too large.");
                        var delta = text.StartsWith(previous, StringComparison.Ordinal) ? text[previous.Length..] : "\n" + text;
                        previous = text;
                        if (delta.Length > 0 && !events.Writer.TryWrite(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta }))
                            throw new IOException("The response reader fell behind.");
                    }, job.Token);
                    await events.Writer.WriteAsync(new { type = "TEXT_MESSAGE_END", messageId }, job.Token);
                    await events.Writer.WriteAsync(new { type = "RUN_FINISHED", threadId = thread, runId = run }, job.Token);
                }
                catch (Exception ex)
                {
                    events.Writer.TryWrite(new { type = "RUN_ERROR", message = ex is OperationCanceledException ? "Stopped. Your existing history is preserved." : "The companion could not complete this request. Check its native status and retry." });
                }
                finally { events.Writer.TryComplete(); }
            }, CancellationToken.None);
            try
            {
                while (true)
                {
                    while (events.Reader.TryRead(out var item)) await Write(item);
                    if (events.Reader.Completion.IsCompleted) { await events.Reader.Completion; break; }
                    var readable = events.Reader.WaitToReadAsync(job.Token).AsTask();
                    if (await Task.WhenAny(readable, Task.Delay(TimeSpan.FromSeconds(1), job.Token)) != readable) await KeepAlive();
                    else if (!await readable) break;
                }
            }
            finally { job.Cancel(); try { await production.WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { } }
        }
        catch (JsonException) { if (!context.Response.SendChunked) context.Response.StatusCode = 400; }
        catch (InvalidDataException) { if (!context.Response.SendChunked) context.Response.StatusCode = 400; }
        catch (Exception ex) when (ex is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            if (acquired) _active.Release();
            try { context.Response.Close(); } catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        }
    }

    internal static (string Thread, string Run, string Prompt) ParseRequest(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid AG-UI request.");
        static string Text(JsonElement value, string name, int max)
        {
            if (!value.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String
                || field.GetString() is not { Length: > 0 } text || text.Length > max) throw new InvalidDataException("Invalid AG-UI request.");
            return text;
        }
        var thread = Text(root, "threadId", 160); var run = Text(root, "runId", 160);
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() > 1000)
            throw new InvalidDataException("Invalid messages.");
        var last = messages.EnumerateArray().LastOrDefault();
        if (last.ValueKind != JsonValueKind.Object || Text(last, "role", 20) != "user") throw new InvalidDataException("A new user message is required.");
        return (thread, run, Text(last, "content", 12000));
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Close();
        if (_pump is not null) await _pump;
        Task[] handlers; lock (_handlers) handlers = _handlers.ToArray();
        await Task.WhenAll(handlers);
        _active.Dispose(); _stop.Dispose();
    }
}
