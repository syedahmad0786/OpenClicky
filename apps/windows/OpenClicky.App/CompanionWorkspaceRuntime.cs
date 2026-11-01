using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenClicky.App;

/// <summary>Owns one loopback-only Node workspace child and only that child.</summary>
internal sealed class CompanionWorkspaceRuntime : IAsyncDisposable
{
    private readonly Process _process;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private CompanionWorkspaceRuntime(Process process, Uri origin, string sessionToken)
    {
        _process = process;
        Origin = origin;
        SessionToken = sessionToken;
    }

    public Uri Origin { get; }
    public string SessionToken { get; }

    public static async Task<CompanionWorkspaceRuntime> StartAsync(
        string runtimeDirectory,
        string nodeExecutable,
        Uri hostAgentUri,
        string hostAgentToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeExecutable);
        ArgumentNullException.ThrowIfNull(hostAgentUri);
        if (!hostAgentUri.IsLoopback || hostAgentUri.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("The AG-UI host endpoint must use loopback HTTP.", nameof(hostAgentUri));
        var serverPath = Path.GetFullPath(Path.Combine(runtimeDirectory, "server.mjs"));
        if (!File.Exists(serverPath)) throw new FileNotFoundException("The packaged workspace runtime is missing.", serverPath);
        if (!File.Exists(nodeExecutable)) throw new FileNotFoundException("The packaged Node executable is missing.", nodeExecutable);

        var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(nodeExecutable, $"\"{serverPath}\"")
        {
            WorkingDirectory = Path.GetFullPath(runtimeDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        CopyEnvironment("SystemRoot", start);
        CopyEnvironment("TEMP", start);
        start.Environment["NODE_ENV"] = "production";
        start.Environment["OPENCLICKY_WORKSPACE_BIND"] = "127.0.0.1";
        start.Environment["OPENCLICKY_WORKSPACE_PORT"] = "0";
        start.Environment["OPENCLICKY_WORKSPACE_TOKEN"] = sessionToken;
        start.Environment["OPENCLICKY_HOST_AGENT_URL"] = hostAgentUri.AbsoluteUri;
        if (hostAgentToken.Length > 0) start.Environment["OPENCLICKY_HOST_AGENT_TOKEN"] = hostAgentToken;

        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the workspace runtime.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            _ = DrainAsync(process.StandardError);
            string? line;
            JsonDocument? ready = null;
            while ((line = await process.StandardOutput.ReadLineAsync(timeout.Token)) is not null)
            {
                if (!line.StartsWith("{\"kind\":\"ready\"", StringComparison.Ordinal)) continue;
                try { ready = JsonDocument.Parse(line); } catch (JsonException) { }
                if (ready is not null) break;
            }
            if (ready is null) throw new InvalidOperationException("The workspace runtime exited before it became ready.");
            using (ready)
            {
            if (ready.RootElement.GetProperty("kind").GetString() != "ready"
                || !Uri.TryCreate(ready.RootElement.GetProperty("origin").GetString(), UriKind.Absolute, out var origin)
                || !origin.IsLoopback || origin.Scheme != Uri.UriSchemeHttp)
                throw new InvalidOperationException("The workspace runtime returned an invalid readiness record.");
            using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var healthResponse = await healthClient.GetAsync(origin, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var healthBody = await healthResponse.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!healthResponse.IsSuccessStatusCode
                || healthResponse.Content.Headers.ContentType?.MediaType != "text/html"
                || !healthBody.Contains("<div id=\"root\"></div>", StringComparison.Ordinal)
                || !healthBody.Contains("<title>OpenClicky workspace</title>", StringComparison.Ordinal))
                throw new InvalidOperationException("The workspace runtime started, but its packaged interface is unavailable.");
            _ = DrainAsync(process.StandardOutput);
            return new CompanionWorkspaceRuntime(process, origin, sessionToken);
            }
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Origin, "/shutdown"));
                request.Headers.Authorization = new("Bearer", SessionToken);
                try { await _http.SendAsync(request); } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { }
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
        }
        finally { _http.Dispose(); _process.Dispose(); }
    }

    private static void CopyEnvironment(string name, ProcessStartInfo start)
    {
        if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) start.Environment[name] = value;
    }
    private static async Task DrainAsync(StreamReader reader) { try { while (await reader.ReadLineAsync() is not null) { } } catch (ObjectDisposedException) { } }
}
