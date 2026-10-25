using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// MCP over a local command's stdin/stdout: one JSON object per line. The command starts on the first request, its
/// stderr is drained and discarded, and <see cref="Dispose"/> kills its whole process tree. Everything it returns is untrusted data.
/// </summary>
public sealed class StdioMcpConnector : JsonRpcMcpConnector
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly string[] DirectExtensions = [".exe", ".com"];
    private static readonly string[] LauncherExtensions = [".cmd", ".bat"];
    private readonly ConnectorSecret? _secret;
    private readonly TimeSpan _requestTimeout;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string?>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly char[] _buffer = new char[16_384];
    private int _bufferStart, _bufferEnd;
    private Process? _process;
    private Stream? _input;
    private StreamReader? _output;
    private volatile bool _stopped;
    private int _disposed;

    /// <summary>Runs <see cref="ConnectorSecret.Command"/> with its arguments and environment.</summary>
    public StdioMcpConnector(string key, string displayName, ConnectorSecret secret) : this(key, displayName, secret, null) { }

    internal StdioMcpConnector(string key, string displayName, ConnectorSecret secret, TimeSpan? requestTimeout) : base(key, displayName)
    {
        _secret = secret;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>Speaks to an already-running server: <paramref name="serverOutput"/> is what it writes, <paramref name="serverInput"/> what it reads.</summary>
    internal StdioMcpConnector(string key, string displayName, Stream serverOutput, Stream serverInput, TimeSpan? requestTimeout = null) : base(key, displayName)
    {
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(60);
        Attach(serverOutput, serverInput);
    }

    internal int? ProcessId { get; private set; }

    /// <summary>Starts the command now (normally done by the first request). Throws <see cref="InvalidOperationException"/> with a readable reason.</summary>
    internal void Start()
    {
        if (_output is not null || _secret is null) return;
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var launch = Launch(_secret.Command ?? "", _secret.Arguments ?? [], Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT"), out var error) ?? throw new InvalidOperationException(error);
        var start = new ProcessStartInfo(launch.FileName, launch.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (var (name, value) in _secret.Environment ?? new Dictionary<string, string>()) start.Environment[name] = value;
        var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new InvalidOperationException($"OpenClicky couldn't start {Path.GetFileName(launch.FileName)}. Check the command in Connections.");
        }
        // Dispose may have run between the check above and here: whichever side sees the other's write kills the process.
        Interlocked.Exchange(ref _process, process);
        if (Volatile.Read(ref _disposed) != 0)
        {
            KillTree(process);
            throw new ObjectDisposedException(GetType().Name);
        }
        ProcessId = process.Id;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        Attach(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
    }

    private void Attach(Stream serverOutput, Stream serverInput)
    {
        _input = serverInput;
        _output = new StreamReader(serverOutput, Utf8, detectEncodingFromByteOrderMarks: false);
        _ = Task.Run(ReadLoopAsync);
    }

    protected override async Task<JsonDocument?> SendAsync(string method, object? parameters, bool expectResult, CancellationToken token)
    {
        Start();
        var json = Envelope(method, parameters, expectResult, out var id);
        TaskCompletionSource<string?>? reply = null;
        if (id is { } requestId)
        {
            reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[requestId] = reply;
        }
        // The limit covers the write too: a server that stops reading its stdin fills the pipe and would block it forever.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            if (_stopped) throw Stopped(method);
            try { await WriteLineAsync(json, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { throw Stopped(method); }
            if (reply is null) return null;
            var line = await reply.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            return line is null ? throw Stopped(method) : ResultOf(line, method);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            if (_lifetime.IsCancellationRequested) throw Stopped(method);
            throw new TimeoutException($"{DisplayName} did not answer {method} within {Math.Ceiling(_requestTimeout.TotalSeconds)} seconds.");
        }
        finally { if (id is { } pendingId) _pending.TryRemove(pendingId, out _); }
    }

    private InvalidOperationException Stopped(string method) =>
        new($"{DisplayName} stopped before it answered {method}. Check its command and arguments in Connections.");

    private async Task WriteLineAsync(string json, CancellationToken token)
    {
        var bytes = Utf8.GetBytes(json + "\n");
        await _writeLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _input!.WriteAsync(bytes, token).ConfigureAwait(false);
            await _input.FlushAsync(token).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Routes each line: responses complete their request by numeric id, server requests get an answer, anything else is skipped.</summary>
    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                var (line, tooLong) = await ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                if (line is null && !tooLong) break;
                if (tooLong)
                {
                    foreach (var waiting in _pending.Values) waiting.TrySetException(new InvalidDataException("The connector's response was too large."));
                    continue;
                }
                await RouteAsync(line!).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
        finally
        {
            _stopped = true;
            foreach (var waiting in _pending.Values) waiting.TrySetResult(null);
        }
    }

    private async Task RouteAsync(string line)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            var hasId = root.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.String;
            if (root.TryGetProperty("method", out var method))
            {
                if (!hasId) return; // a notification
                object reply = method.ValueKind == JsonValueKind.String && method.GetString() == "ping"
                    ? new { jsonrpc = "2.0", id, result = new { } }
                    : new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Method not found" } };
                try { await WriteLineAsync(JsonSerializer.Serialize(reply), _lifetime.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
                return;
            }
            if (hasId && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) && _pending.TryGetValue(number, out var waiting))
                waiting.TrySetResult(line);
        }
    }

    /// <summary>Reads one line of at most <see cref="JsonRpcMcpConnector.MaxResponseBytes"/> characters; a longer one is skipped and reported.</summary>
    private async Task<(string? Line, bool TooLong)> ReadLineAsync(CancellationToken token)
    {
        var line = new StringBuilder();
        var tooLong = false;
        while (true)
        {
            if (_bufferStart == _bufferEnd)
            {
                _bufferStart = 0;
                _bufferEnd = await _output!.ReadAsync(_buffer, token).ConfigureAwait(false);
                if (_bufferEnd == 0) return line.Length == 0 && !tooLong ? (null, false) : (tooLong ? null : line.ToString(), tooLong);
            }
            var newline = Array.IndexOf(_buffer, '\n', _bufferStart, _bufferEnd - _bufferStart);
            var end = newline < 0 ? _bufferEnd : newline;
            if (!tooLong && line.Length + end - _bufferStart > MaxResponseBytes) { tooLong = true; line.Clear(); }
            if (!tooLong) line.Append(_buffer, _bufferStart, end - _bufferStart);
            _bufferStart = newline < 0 ? _bufferEnd : newline + 1;
            if (newline >= 0) return tooLong ? (null, true) : (line.ToString().TrimEnd('\r'), false);
        }
    }

    /// <summary>
    /// Resolves a command: a full path (C:\Tools\server.exe) as is, a bare name through each PATH entry only (never the
    /// current directory), trying each PATHEXT extension (and the bare name when it has one); a relative path with a
    /// folder is refused. .exe/.com run directly with ArgumentList-style quoting; .cmd/.bat run through
    /// %ComSpec% /d /s /c and may not carry cmd metacharacters. Public so the app can refuse a command before saving it;
    /// returns null with a readable <paramref name="error"/> when the command can't be started.
    /// </summary>
    public static (string FileName, string Arguments)? Launch(string command, IReadOnlyList<string> args, string? pathVariable, string? pathExt, out string error)
    {
        error = $"OpenClicky couldn't find {command}. Check the command, or give its full path.";
        command = command.Trim();
        if (command.Length == 0) return null;
        var extensions = (pathExt ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => DirectExtensions.Contains(e, StringComparer.OrdinalIgnoreCase) || LauncherExtensions.Contains(e, StringComparer.OrdinalIgnoreCase)).ToArray();
        // A drive-relative name like C:server.exe counts too: Path.Combine would resolve it against that drive's current directory.
        var hasDirectory = command.IndexOfAny(['\\', '/', ':']) >= 0;
        if (hasDirectory && !Path.IsPathFullyQualified(command))
        {
            error = "Give the program's full path, like C:\\Tools\\server.exe, or just its name.";
            return null;
        }
        string? found = null;
        try
        {
            IEnumerable<string> folders = hasDirectory ? [""]
                : (pathVariable ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.Trim('"'));
            string[] bare = Path.HasExtension(command) ? [command] : [];
            var names = bare.Concat(extensions.Select(e => command + e.ToLowerInvariant())).ToArray();
            found = folders.SelectMany(folder => names.Select(name => folder.Length == 0 ? Path.GetFullPath(name) : Path.Combine(folder, name)))
                .FirstOrDefault(File.Exists);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { }
        if (found is null) return null;
        var extension = Path.GetExtension(found);
        if (DirectExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            error = "";
            return (found, string.Join(' ', args.Select(Quote)));
        }
        if (!LauncherExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            error = "OpenClicky can start only .exe, .com, .cmd or .bat files. Run scripts through their interpreter.";
            return null;
        }
        if (args.Any(a => a.IndexOfAny(['"', '%', '^', '&', '|', '<', '>']) >= 0))
        {
            error = "Arguments for .cmd launchers can't contain \" % ^ & | < or >.";
            return null;
        }
        error = "";
        var shell = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec ? comSpec : Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return (shell, "/d /s /c \"" + string.Join(' ', new[] { found }.Concat(args).Select(QuoteForLauncher)) + "\"");
    }

    /// <summary>
    /// Wraps one launcher argument (it has no quote characters) in quotes, doubling trailing backslashes so the program the
    /// launcher forwards it to (CommandLineToArgvW rules) still reads C:\Notes\ and not an escaped closing quote.
    /// </summary>
    private static string QuoteForLauncher(string argument) =>
        "\"" + argument + new string('\\', argument.Length - argument.TrimEnd('\\').Length) + "\"";

    /// <summary>Quotes one argument exactly as ProcessStartInfo.ArgumentList would (CommandLineToArgvW rules).</summary>
    private static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;
        var quoted = new StringBuilder("\"");
        for (var i = 0; i < argument.Length; i++)
        {
            var slashes = 0;
            while (i < argument.Length && argument[i] == '\\') { slashes++; i++; }
            if (i == argument.Length) { quoted.Append('\\', slashes * 2); break; }
            if (argument[i] == '"') quoted.Append('\\', slashes * 2 + 1).Append('"');
            else quoted.Append('\\', slashes).Append(argument[i]);
        }
        return quoted.Append('"').ToString();
    }

    /// <summary>Kills the process and its descendants, also when the process itself (say, a .cmd launcher) already exited.</summary>
    private static void KillTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        try { process.Dispose(); } catch (Exception) { }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
        if (Volatile.Read(ref _process) is { } process) KillTree(process);
        else
        {
            try { _input?.Dispose(); } catch (Exception) { }
            try { _output?.Dispose(); } catch (Exception) { }
        }
    }
}
