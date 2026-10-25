using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenClicky.Providers;

/// <summary>Owns one private OpenWork server, which in turn owns the workspace's OpenCode engine. Each instance is its own node
/// process on its own loopback port (asked of Windows, port 0) with its own state folder and run lock, so concurrent jobs each start
/// their own. The server joins the app's Job Object, so it and the engine it starts die with the app.</summary>
public sealed class OpenWorkServerService : IAsyncDisposable
{
    internal const string BundledEngineVersion = "1.18.30";
    internal const string BundledPluginDependenciesSha256 = "B2D6A3F69F92EE153A7E17A626CFE478E12CBD9DFC59BC6FD9EEC96B49F16C77";
    private const int MaxSeedEntries = 20_000;
    private const long MaxSeedArchiveBytes = 32L * 1024 * 1024;
    private const long MaxSeedFileBytes = 16L * 1024 * 1024;
    private const long MaxSeedTotalBytes = 128L * 1024 * 1024;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan BootstrapHttpTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(2);
    private readonly Process _process;
    private readonly OpenCodeTaskClient _client;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private string? _activeSessionId;
    private const string UpdatePrefix = "Update from the user: ";
    /// <summary>Guards the steer state: updates held until the task prompt is sent, the session they then go to, and whether the run is over.</summary>
    private readonly object _steerGate = new();
    private readonly List<string> _heldUpdates = [];
    private string? _steerSessionId;
    private bool _runEnded;
    private int _updatesInFlight, _updatesSent;
    private bool _disposed;

    internal OpenWorkServerService(Process process, OpenCodeTaskClient client, string workspaceId, string workspacePath)
    {
        _process = process;
        _client = client;
        WorkspaceId = workspaceId;
        WorkspacePath = workspacePath;
    }

    public string WorkspaceId { get; }
    public string WorkspacePath { get; }

    public static Task<OpenWorkServerService> StartAsync(
        string workspacePath,
        string nodeExecutable,
        string serverEntrypoint,
        string engineExecutable,
        OpenCodeCompanionConfiguration companion,
        CancellationToken cancellationToken = default,
        Action<string, bool, long>? startupProgress = null) =>
        StartAsync(nodeExecutable, serverEntrypoint, engineExecutable, workspacePath,
            Path.Combine(Path.GetTempPath(), "OpenClicky", "openwork", Guid.NewGuid().ToString("N")), companion, cancellationToken, startupProgress);

    public static async Task<OpenWorkServerService> StartAsync(
        string nodePath,
        string serverBundlePath,
        string openCodePath,
        string workspacePath,
        string isolatedStateRoot,
        OpenCodeCompanionConfiguration companion,
        CancellationToken cancellationToken = default,
        Action<string, bool, long>? startupProgress = null)
    {
        var workspace = RequireDirectory(workspacePath, nameof(workspacePath));
        var node = RequireFile(nodePath, nameof(nodePath));
        var bundle = RequireFile(serverBundlePath, nameof(serverBundlePath));
        var openCode = RequireFile(openCodePath, nameof(openCodePath));
        var stateRoot = Path.GetFullPath(isolatedStateRoot);
        if (Path.GetPathRoot(stateRoot) == stateRoot) throw new ArgumentException("The isolated state path cannot be a drive root.", nameof(isolatedStateRoot));
        Directory.CreateDirectory(stateRoot);
        ReportStartup(startupProgress, "dependencies", completed: false, 0);
        var dependencyClock = Stopwatch.StartNew();
        SeedBundledPluginDependencies(bundle, stateRoot, cancellationToken);
        ReportStartup(startupProgress, "dependencies", completed: true, dependencyClock.ElapsedMilliseconds);
        var port = AvailablePort();
        var clientToken = Secret();
        var hostToken = Secret();
        var configJson = companion.ToConfigJson();
        EnsureDenyShellProfile(configJson);
        var start = CreateStartInfo(node, bundle, openCode, workspace, stateRoot, port, clientToken, hostToken, configJson);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        HttpClient? http = null;
        OpenCodeTaskClient? client = null;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The app-owned OpenWork server could not be started.");
            AppChildProcesses.Adopt(process); // at once: the engine it starts later inherits the job
            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            // A cold OpenCode engine reload can legitimately exceed 30 seconds on Windows.
            // This client exists only for bounded bootstrap and is disposed before task execution.
            http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = BootstrapHttpTimeout };
            var origin = new Uri($"http://127.0.0.1:{port}/");
            await TimedStageAsync("health", () => StageAsync("waiting for server health", () => WaitForHealthAsync(process, http, origin, cancellationToken), cancellationToken), startupProgress).ConfigureAwait(false);
            var workspaceId = await TimedStageAsync("workspace", () => StageAsync("discovering the workspace", () => ResolveWorkspaceIdAsync(http, origin, clientToken, workspace, cancellationToken), cancellationToken), startupProgress).ConfigureAwait(false);
            await ConfigureProfileAsync(http, origin, workspaceId, clientToken, hostToken, configJson, cancellationToken, startupProgress).ConfigureAwait(false);
            client = OpenCodeTaskClient.ForOpenWorkWorkspace(origin.AbsoluteUri, workspaceId, clientToken);
            return new(process, client, workspaceId, workspace);
        }
        catch
        {
            client?.Dispose();
            http?.Dispose();
            Stop(process);
            process.Dispose();
            throw;
        }
        finally { http?.Dispose(); }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string nodePath, string bundlePath, string openCodePath, string workspacePath,
        string stateRoot, int port, string clientToken, string hostToken, string companionConfig)
    {
        EnsureDenyShellProfile(companionConfig);
        var start = new ProcessStartInfo
        {
            FileName = nodePath,
            WorkingDirectory = workspacePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { bundlePath, "--host", "127.0.0.1", "--port", port.ToString(), "--approval", "manual", "--workspace", workspacePath })
            start.ArgumentList.Add(arg);
        start.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "COMSPEC", "PATHEXT", "PATH", "TEMP", "TMP", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) start.Environment[name] = value;
        var configHome = Path.Combine(stateRoot, "config");
        var dataHome = Path.Combine(stateRoot, "data");
        start.Environment["OPENWORK_MANAGE_OPENCODE"] = "1";
        start.Environment["OPENWORK_OPENCODE_BIN"] = openCodePath;
        start.Environment["OPENWORK_SERVER_CONFIG"] = Path.Combine(configHome, "openwork", "server.json");
        start.Environment["OPENWORK_TOKEN_STORE"] = Path.Combine(dataHome, "openwork", "tokens.json");
        start.Environment["OPENWORK_TOKEN"] = clientToken;
        start.Environment["OPENWORK_HOST_TOKEN"] = hostToken;
        start.Environment["OPENWORK_NO_UPDATE_CHECK"] = "1";
        start.Environment["OPENWORK_WEB_BOOTSTRAP_TOKEN"] = "0";
        start.Environment["OPENCODE_CONFIG_CONTENT"] = companionConfig;
        start.Environment["XDG_CONFIG_HOME"] = configHome;
        start.Environment["XDG_DATA_HOME"] = dataHome;
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(stateRoot, "cache");
        start.Environment["XDG_STATE_HOME"] = Path.Combine(stateRoot, "state");
        start.Environment["HOME"] = stateRoot;
        start.Environment["USERPROFILE"] = stateRoot;
        start.Environment.Remove("OPENCODE_CONFIG");
        start.Environment.Remove("OPENCODE_CONFIG_DIR");
        return start;
    }

    internal static void SeedBundledPluginDependencies(string serverBundlePath, string stateRoot, CancellationToken cancellationToken = default)
    {
        var packageRoot = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(serverBundlePath))!)?.FullName
            ?? throw new InvalidOperationException("The bundled OpenWork package path is invalid.");
        var dist = Path.Combine(packageRoot, "dist");
        var expected = Path.Combine(dist, $"opencode-plugin-deps-{BundledEngineVersion}.tgz");
        if (!File.Exists(expected))
        {
            var other = Directory.Exists(dist)
                ? Directory.EnumerateFiles(dist, "opencode-plugin-deps-*.tgz").FirstOrDefault()
                : null;
            if (other is not null)
                throw new NotSupportedException($"The bundled OpenCode plugin dependencies do not match engine {BundledEngineVersion}.");
            throw new FileNotFoundException("The bundled OpenCode plugin dependencies are missing from this build.", expected);
        }
        var target = Path.Combine(Path.GetFullPath(stateRoot), "config", "opencode");
        ExtractPluginDependencyArchive(expected, target, BundledPluginDependenciesSha256, cancellationToken);
    }

    internal static void ExtractPluginDependencyArchive(string archivePath, string targetPath, string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists || archiveInfo.Length <= 0 || archiveInfo.Length > MaxSeedArchiveBytes)
            throw new InvalidDataException("The bundled dependency archive exceeds its compressed size limit.");
        var target = Path.GetFullPath(targetPath);
        if (Directory.Exists(target) || File.Exists(target))
            throw new InvalidOperationException("The private OpenCode dependency directory already exists.");
        var parent = Directory.GetParent(target)?.FullName
            ?? throw new InvalidOperationException("The private OpenCode dependency path is invalid.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".opencode-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var stagingPrefix = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        var nodeModulesPrefix = Path.GetFullPath(Path.Combine(staging, "node_modules")) + Path.DirectorySeparatorChar;
        var entries = 0;
        long totalBytes = 0;
        var aliases = new List<(string Destination, string LinkName)>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actualHash = Convert.ToHexString(ComputeHash(archive, cancellationToken));
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The bundled OpenCode plugin dependencies failed integrity verification.");
            archive.Position = 0;
            using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new TarReader(gzip, leaveOpen: false);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > MaxSeedEntries) throw new InvalidDataException("The bundled dependency archive contains too many entries.");
                var name = entry.Name.Replace('\\', '/');
                var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (name.StartsWith('/') || Path.IsPathRooted(name) || parts.Length == 0
                    || parts.Any(IsUnsafeArchiveSegment)
                    || (parts[0] is not "package.json" and not "package-lock.json" and not "node_modules"))
                    throw new InvalidDataException("The bundled dependency archive contains an invalid path.");
                if (parts[0] is "package.json" or "package-lock.json" && parts.Length != 1)
                    throw new InvalidDataException("The bundled dependency archive contains an invalid manifest path.");
                var destination = Path.GetFullPath(Path.Combine(staging, Path.Combine(parts)));
                if (!destination.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The bundled dependency archive escapes its private directory.");
                if (entry.EntryType == TarEntryType.Directory)
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                if (entry.EntryType == TarEntryType.SymbolicLink)
                {
                    if (!destination.StartsWith(Path.Combine(staging, "node_modules", ".bin") + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(entry.LinkName))
                        throw new InvalidDataException("The bundled dependency archive contains an unsupported link.");
                    aliases.Add((destination, entry.LinkName));
                    continue;
                }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("The bundled dependency archive contains a link or unsupported entry.");
                if (entry.Length < 0 || entry.Length > MaxSeedFileBytes || totalBytes + entry.Length > MaxSeedTotalBytes)
                    throw new InvalidDataException("The bundled dependency archive exceeds its size limit.");
                totalBytes += entry.Length;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                CopyBounded(entry.DataStream, output, cancellationToken);
                if (output.Length != entry.Length) throw new InvalidDataException("The bundled dependency archive entry was incomplete.");
            }
            foreach (var (destination, linkName) in aliases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedLink = linkName.Replace('\\', '/');
                if (normalizedLink.StartsWith('/') || Path.IsPathRooted(normalizedLink))
                    throw new InvalidDataException("The bundled dependency alias has an absolute target.");
                var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(destination)!, normalizedLink.Replace('/', Path.DirectorySeparatorChar)));
                if (!source.StartsWith(nodeModulesPrefix, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(source)
                    || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0
                    || aliases.Any(alias => string.Equals(alias.Destination, source, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("The bundled dependency alias target is missing, chained, or outside node_modules.");
                var length = new FileInfo(source).Length;
                if (length > MaxSeedFileBytes || totalBytes + length > MaxSeedTotalBytes)
                    throw new InvalidDataException("The bundled dependency aliases exceed the size limit.");
                totalBytes += length;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                CopyBounded(input, output, cancellationToken);
                if (output.Length != length) throw new InvalidDataException("The bundled dependency alias was incomplete.");
            }
            if (!File.Exists(Path.Combine(staging, "package.json"))
                || !File.Exists(Path.Combine(staging, "package-lock.json"))
                || !Directory.Exists(Path.Combine(staging, "node_modules")))
                throw new InvalidDataException("The bundled dependency archive is incomplete.");
            Directory.Move(staging, target);
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }

    private static void CopyBounded(Stream? input, Stream output, CancellationToken cancellationToken)
    {
        if (input is null) return;
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
        }
    }

    private static byte[] ComputeHash(Stream input, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return hash.GetHashAndReset();
    }

    private static bool IsUnsafeArchiveSegment(string part)
    {
        if (part is "." or ".." || part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.')) return true;
        if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return true;
        var stem = part.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }

    public async Task<OpenCodeTaskResult> RunAsync(
        string title,
        string prompt,
        IProgress<OpenCodeTaskUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process.HasExited) throw new InvalidOperationException("The private OpenWork server is not running.");
            var session = await StageAsync("creating the task session", () => _client.CreateSessionAsync(title, cancellationToken), cancellationToken).ConfigureAwait(false);
            _activeSessionId = session.Id;
            progress?.Report(new(OpenCodeTaskUpdateKind.Started, session.Id, "OpenWork task started."));
            await StageAsync("sending the task prompt", () => _client.SendPromptAsync(session.Id, prompt, cancellationToken), cancellationToken).ConfigureAwait(false);
            // Updates the user sent before the session existed follow the task prompt, in order; later ones go straight in.
            while (true)
            {
                string[] held;
                lock (_steerGate)
                {
                    if (_heldUpdates.Count == 0) { _steerSessionId = session.Id; break; }
                    held = [.. _heldUpdates];
                    _heldUpdates.Clear();
                }
                foreach (var update in held)
                    await StageAsync("sending the user's update", () => _client.SendPromptAsync(session.Id, UpdatePrefix + update, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            string? lastText = null;
            var seenTools = new HashSet<string>();
            DateTimeOffset? idleWithoutTerminalSince = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_process.HasExited) throw new InvalidOperationException("The private OpenWork server stopped before the task completed.");
                var pending = await StageAsync("checking engine permissions", () => _client.GetPendingPermissionsAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
                if (pending.Any(item => item.SessionId == session.Id))
                    throw new InvalidOperationException("OpenWork requested an unexpected engine permission; the task was stopped.");
                int sentBefore;
                lock (_steerGate) sentBefore = _updatesSent;
                var snapshot = await StageAsync("reading task progress", () => _client.GetMessagesAsync(session.Id, cancellationToken), cancellationToken).ConfigureAwait(false);
                var error = OpenCodeWorkspaceService.GetAssistantError(snapshot.Messages);
                if (error is not null) throw new InvalidOperationException(error);
                foreach (var tool in NewToolCalls(snapshot.Messages, seenTools))
                    progress?.Report(new(OpenCodeTaskUpdateKind.ToolUsed, session.Id, tool.Tool, Tool: tool));
                var visible = snapshot.Messages.LastOrDefault(message => message.Role == "assistant" && !string.IsNullOrWhiteSpace(message.Text));
                if (visible is not null && visible.Text != lastText)
                {
                    lastText = visible.Text;
                    progress?.Report(new(OpenCodeTaskUpdateKind.Message, session.Id, visible.Text, visible));
                }
                if (snapshot.Status.Type == "idle" && OpenCodeWorkspaceService.HasTerminalAssistantMessage(snapshot.Messages) && EndUnlessUpdated(sentBefore))
                {
                    progress?.Report(new(OpenCodeTaskUpdateKind.Completed, session.Id, "OpenWork task finished."));
                    return new(session.Id, snapshot.Messages);
                }
                idleWithoutTerminalSince = snapshot.Status.Type == "idle"
                    ? idleWithoutTerminalSince ?? DateTimeOffset.UtcNow
                    : null;
                if (OpenCodeWorkspaceService.MissingTerminalTimedOut(idleWithoutTerminalSince, DateTimeOffset.UtcNow))
                    throw new InvalidOperationException(
                        "OpenWork became idle without returning a completed assistant message. No successful result was received.");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await AbortActiveBestEffortAsync().ConfigureAwait(false);
            if (_activeSessionId is { } sessionId)
                progress?.Report(new(OpenCodeTaskUpdateKind.Aborted, sessionId, "OpenWork task stopped."));
            throw;
        }
        catch (Exception exception)
        {
            await AbortActiveBestEffortAsync().ConfigureAwait(false);
            if (_activeSessionId is { } sessionId)
                progress?.Report(new(OpenCodeTaskUpdateKind.Failed, sessionId, exception.Message));
            throw;
        }
        finally
        {
            lock (_steerGate) { _runEnded = true; _steerSessionId = null; _heldUpdates.Clear(); }
            _activeSessionId = null;
            _runLock.Release();
        }
    }

    /// <summary>The assistant's tool calls that started since <paramref name="seen"/> was last filled, in order. A call still
    /// pending has no input yet, so it is reported once it runs.</summary>
    internal static IReadOnlyList<OpenCodeToolCall> NewToolCalls(IEnumerable<OpenCodeMessageSummary> messages, ISet<string> seen) =>
        messages.Where(m => m.Role == "assistant").SelectMany(m => m.Tools ?? [])
            .Where(t => t.Status != "pending" && seen.Add(t.CallId)).ToList();

    public Task<OpenCodeTaskResult> RunTaskAsync(
        string title, string prompt, IProgress<OpenCodeTaskUpdate>? progress = null,
        CancellationToken cancellationToken = default) => RunAsync(title, prompt, progress, cancellationToken);

    /// <summary>The user's words, sent into the running task's session as a user message ("Update from the user: ..."), which the
    /// engine takes up at its next step. Before the task prompt is sent they are held and sent right after it. False once the run
    /// has ended or this server is gone: nothing was sent.</summary>
    public async Task<bool> SendToRunningAsync(string text, CancellationToken ct)
    {
        string id;
        lock (_steerGate)
        {
            if (_disposed || _runEnded) return false;
            if (_steerSessionId is null) { _heldUpdates.Add(text); return true; }
            id = _steerSessionId;
            _updatesInFlight++;
        }
        try { await _client.SendPromptAsync(id, UpdatePrefix + text, ct).ConfigureAwait(false); }
        catch { lock (_steerGate) _updatesInFlight--; throw; }
        lock (_steerGate) { _updatesInFlight--; _updatesSent++; }
        return true;
    }

    /// <summary>The run may end on a finished answer only if no update was being sent or arrived after that snapshot was read
    /// (the snapshot may predate it); then later updates are refused instead of lost.</summary>
    private bool EndUnlessUpdated(int sentBefore)
    {
        lock (_steerGate)
        {
            if (_updatesInFlight > 0 || _updatesSent != sentBefore) return false;
            _runEnded = true;
            return true;
        }
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = _activeSessionId;
        if (id is not null) await _client.AbortAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task AbortActiveBestEffortAsync()
    {
        var id = _activeSessionId;
        if (id is not null)
        {
            using var timeout = new CancellationTokenSource(AbortTimeout);
            try { await _client.AbortAsync(id, timeout.Token).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }

    internal static string ParseWorkspaceId(string json, string expectedPath)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("workspaces", out var workspaces)
            || workspaces.ValueKind != JsonValueKind.Array || workspaces.GetArrayLength() > 100)
            throw new InvalidDataException("OpenWork returned an invalid workspace list.");
        var expected = Path.GetFullPath(expectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var item in workspaces.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String) continue;
            var candidate = path.GetString();
            if (candidate is not null && string.Equals(Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), expected, StringComparison.OrdinalIgnoreCase))
                return id.GetString() is { Length: > 0 and <= 200 } value ? value : throw new InvalidDataException("OpenWork returned an invalid workspace ID.");
        }
        throw new InvalidDataException("OpenWork did not expose the requested workspace.");
    }

    internal static void EnsureEffectiveProfile(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String
            || !model.GetString()!.StartsWith(OpenCodeCompanionConfiguration.ProviderId + "/", StringComparison.Ordinal)
            || !root.TryGetProperty("permission", out var permission) || permission.ValueKind != JsonValueKind.Object
            || !permission.TryGetProperty("*", out var wildcard) || wildcard.GetString() != "deny"
            || !permission.TryGetProperty("openclicky_*", out var brokerPermission) || brokerPermission.GetString() != "allow"
            || !root.TryGetProperty("enabled_providers", out var providers) || providers.ValueKind != JsonValueKind.Array
            || providers.GetArrayLength() != 1 || providers[0].GetString() != OpenCodeCompanionConfiguration.ProviderId
            || !root.TryGetProperty("mcp", out var mcp) || mcp.ValueKind != JsonValueKind.Object
            || !mcp.TryGetProperty("openclicky", out var broker) || broker.ValueKind != JsonValueKind.Object
            || !broker.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var brokerUri)
            || brokerUri.Scheme != Uri.UriSchemeHttp || brokerUri.Host != "127.0.0.1"
            || !broker.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Object
            || !headers.TryGetProperty("Authorization", out var authorization) || authorization.ValueKind != JsonValueKind.String
            || !authorization.GetString()!.StartsWith("Bearer ", StringComparison.Ordinal))
            throw new InvalidOperationException("OpenWork did not apply OpenClicky's deny-by-default engine profile.");
    }

    internal static void EnsureEffectiveProfile(string json, string expectedJson)
    {
        EnsureEffectiveProfile(json);
        using var effectiveDocument = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        using var expectedDocument = JsonDocument.Parse(expectedJson, new JsonDocumentOptions { MaxDepth = 32 });
        var effective = effectiveDocument.RootElement;
        var expected = expectedDocument.RootElement;

        RequireExactString(effective, expected, "model");
        RequireExactString(effective, expected, "small_model");
        RequireExactString(effective, expected, "default_agent");
        RequireExactObject(effective, expected, "permission");
        RequireExactArray(effective, expected, "plugin");

        var effectiveMcp = RequiredObject(effective, "mcp");
        var expectedMcp = RequiredObject(expected, "mcp");
        if (effectiveMcp.EnumerateObject().Count() != 1)
            throw new InvalidOperationException("OpenWork exposed an unexpected MCP server.");
        RequireExactObject(effectiveMcp, expectedMcp, "openclicky");

        var effectiveProviders = RequiredObject(effective, "provider");
        var expectedProviders = RequiredObject(expected, "provider");
        RequireExactObject(effectiveProviders, expectedProviders, OpenCodeCompanionConfiguration.ProviderId);

        var effectiveAgents = RequiredObject(effective, "agent");
        var expectedAgents = RequiredObject(expected, "agent");
        var effectiveCompanion = RequiredObject(effectiveAgents, "companion");
        var expectedCompanion = RequiredObject(expectedAgents, "companion");
        RequireExactObject(effectiveCompanion, expectedCompanion, "permission");
        RequireExactString(effectiveCompanion, expectedCompanion, "mode");
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"OpenWork omitted the required {name} profile section.");
        return value;
    }

    private static void RequireExactObject(JsonElement actualParent, JsonElement expectedParent, string name)
    {
        var actual = RequiredObject(actualParent, name);
        var expected = RequiredObject(expectedParent, name);
        if (!JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), JsonNode.Parse(expected.GetRawText())))
            throw new InvalidOperationException($"OpenWork changed the protected {name} profile section.");
    }

    private static void RequireExactString(JsonElement actual, JsonElement expected, string name)
    {
        if (!actual.TryGetProperty(name, out var actualValue) || actualValue.ValueKind != JsonValueKind.String
            || !expected.TryGetProperty(name, out var expectedValue) || expectedValue.ValueKind != JsonValueKind.String
            || actualValue.GetString() != expectedValue.GetString())
            throw new InvalidOperationException($"OpenWork changed the protected {name} profile value.");
    }

    private static void RequireExactArray(JsonElement actual, JsonElement expected, string name)
    {
        if (!actual.TryGetProperty(name, out var actualValue) || actualValue.ValueKind != JsonValueKind.Array
            || !expected.TryGetProperty(name, out var expectedValue) || expectedValue.ValueKind != JsonValueKind.Array
            || !JsonNode.DeepEquals(JsonNode.Parse(actualValue.GetRawText()), JsonNode.Parse(expectedValue.GetRawText())))
            throw new InvalidOperationException($"OpenWork changed the protected {name} profile value.");
    }

    private static async Task ConfigureProfileAsync(
        HttpClient http, Uri origin, string workspaceId, string clientToken, string hostToken,
        string configJson, CancellationToken token, Action<string, bool, long>? startupProgress)
    {
        await TimedStageAsync("profile", WriteProfileAsync, startupProgress).ConfigureAwait(false);
        await EnsureProfileAppliedAsync(http, origin, workspaceId, clientToken, configJson, token, startupProgress).ConfigureAwait(false);

        async Task WriteProfileAsync()
        {
        var route = $"workspace/{Uri.EscapeDataString(workspaceId)}/opencode-config";
        using var write = Request(HttpMethod.Post, new Uri(origin, route), clientToken,
            JsonContent.Create(new { scope = "global", content = configJson }));
        var writeTask = http.SendAsync(write, token);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        string? approvalId = null;
        while (!writeTask.IsCompleted && DateTimeOffset.UtcNow < deadline)
        {
            using var pendingRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "approvals"));
            pendingRequest.Headers.Add("X-OpenWork-Host-Token", hostToken);
            using var pendingResponse = await StageAsync("reading profile approvals", () => http.SendAsync(pendingRequest, token), token).ConfigureAwait(false);
            if (!pendingResponse.IsSuccessStatusCode) throw new HttpRequestException("OpenWork bootstrap approval discovery failed.");
            var pendingJson = await pendingResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var pending = JsonDocument.Parse(pendingJson);
            if (pending.RootElement.ValueKind != JsonValueKind.Object
                || !pending.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 100)
                throw new InvalidDataException("OpenWork returned an invalid approval list.");
            approvalId = items.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("workspaceId", out var workspace) && workspace.GetString() == workspaceId
                    && item.TryGetProperty("action", out var action) && action.GetString() == "config.global.write")
                .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (approvalId is not null) break;
            await Task.Delay(50, token).ConfigureAwait(false);
        }
        if (approvalId is null)
        {
            if (writeTask.IsCompleted)
            {
                using var early = await StageAsync("writing the companion profile", () => writeTask, token).ConfigureAwait(false);
                throw new HttpRequestException($"OpenWork profile write ended without a bootstrap approval (HTTP {(int)early.StatusCode}).");
            }
            throw new TimeoutException("OpenWork did not expose its bootstrap profile approval.");
        }
        using (var approve = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, $"approvals/{Uri.EscapeDataString(approvalId)}")))
        {
            approve.Headers.Add("X-OpenWork-Host-Token", hostToken);
            approve.Content = JsonContent.Create(new { reply = "allow" });
            using var approved = await StageAsync("approving the companion profile", () => http.SendAsync(approve, token), token).ConfigureAwait(false);
            if (!approved.IsSuccessStatusCode) throw new HttpRequestException("OpenWork rejected its bootstrap profile approval.");
        }
        using (var written = await StageAsync("writing the companion profile", () => writeTask, token).ConfigureAwait(false))
            if (!written.IsSuccessStatusCode) throw new HttpRequestException($"OpenWork profile write failed with HTTP {(int)written.StatusCode}.");
        }
    }

    internal static async Task EnsureProfileAppliedAsync(
        HttpClient http, Uri origin, string workspaceId, string clientToken,
        string configJson, CancellationToken token, Action<string, bool, long>? startupProgress = null)
    {
        if (await TimedStageAsync("effective-profile", () => EffectiveProfileMatchesAsync(http, origin, workspaceId, clientToken, configJson, token), startupProgress).ConfigureAwait(false))
            return;

        await TimedStageAsync("engine-reload", async () =>
        {
            using var reload = Request(HttpMethod.Post, new Uri(origin, $"workspace/{Uri.EscapeDataString(workspaceId)}/engine/reload"), clientToken);
            using var reloaded = await StageAsync("reloading the private engine", () => http.SendAsync(reload, token), token).ConfigureAwait(false);
            if (!reloaded.IsSuccessStatusCode) throw new HttpRequestException($"OpenWork engine reload failed with HTTP {(int)reloaded.StatusCode}.");
        }, startupProgress).ConfigureAwait(false);

        if (!await TimedStageAsync("effective-profile", () => EffectiveProfileMatchesAsync(http, origin, workspaceId, clientToken, configJson, token), startupProgress).ConfigureAwait(false))
            throw new InvalidOperationException("OpenWork still had a different effective profile after reload.");
    }

    private static async Task TimedStageAsync(string stage, Func<Task> action, Action<string, bool, long>? progress)
    {
        ReportStartup(progress, stage, completed: false, 0);
        var clock = Stopwatch.StartNew();
        await action().ConfigureAwait(false);
        ReportStartup(progress, stage, completed: true, clock.ElapsedMilliseconds);
    }

    private static async Task<T> TimedStageAsync<T>(string stage, Func<Task<T>> action, Action<string, bool, long>? progress)
    {
        ReportStartup(progress, stage, completed: false, 0);
        var clock = Stopwatch.StartNew();
        var result = await action().ConfigureAwait(false);
        ReportStartup(progress, stage, completed: true, clock.ElapsedMilliseconds);
        return result;
    }

    private static void ReportStartup(Action<string, bool, long>? progress, string stage, bool completed, long elapsedMilliseconds)
    {
        try { progress?.Invoke(stage, completed, elapsedMilliseconds); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private static async Task<bool> EffectiveProfileMatchesAsync(
        HttpClient http, Uri origin, string workspaceId, string clientToken,
        string configJson, CancellationToken token)
    {
        using var verify = Request(HttpMethod.Get, new Uri(origin, $"workspace/{Uri.EscapeDataString(workspaceId)}/opencode/config"), clientToken);
        using var verified = await StageAsync("verifying the effective profile", () => http.SendAsync(verify, token), token).ConfigureAwait(false);
        if (!verified.IsSuccessStatusCode) throw new HttpRequestException("OpenWork effective profile verification failed.");
        var effective = await verified.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (effective.Length > OpenCodeTaskClient.MaxResponseBytes) throw new InvalidDataException("OpenWork effective profile response was too large.");
        try
        {
            EnsureEffectiveProfile(effective, configJson);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, string bearer, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new("Bearer", bearer);
        return request;
    }

    private static async Task StageAsync(string stage, Func<Task> action, CancellationToken cancellationToken)
    {
        try { await action().ConfigureAwait(false); }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenWork timed out while {stage}.", exception);
        }
    }

    private static async Task<T> StageAsync<T>(string stage, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try { return await action().ConfigureAwait(false); }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenWork timed out while {stage}.", exception);
        }
    }

    private static async Task<string> ResolveWorkspaceIdAsync(HttpClient http, Uri origin, string token, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "workspaces"));
        request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"OpenWork workspace discovery failed with HTTP {(int)response.StatusCode}.");
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (json.Length > OpenCodeTaskClient.MaxResponseBytes) throw new InvalidDataException("OpenWork workspace response was too large.");
        return ParseWorkspaceId(json, path);
    }

    private static async Task WaitForHealthAsync(Process process, HttpClient http, Uri origin, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (process.HasExited) throw new InvalidOperationException("The private OpenWork server exited during startup.");
            try
            {
                using var response = await http.GetAsync(new Uri(origin, "health"), timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) when (!timeout.IsCancellationRequested) { }
            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
        }
    }

    private static void EnsureDenyShellProfile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("permission", out var permission) || permission.ValueKind != JsonValueKind.Object
            || !permission.TryGetProperty("*", out var wildcard) || wildcard.GetString() != "deny"
            || !root.TryGetProperty("enabled_providers", out var providers) || providers.ValueKind != JsonValueKind.Array
            || providers.GetArrayLength() != 1 || providers[0].GetString() != OpenCodeCompanionConfiguration.ProviderId)
            throw new InvalidOperationException("The OpenWork engine profile must deny unbrokered tools and enable only OpenClicky's provider.");
    }

    private static string RequireFile(string path, string name) => File.Exists(Path.GetFullPath(path))
        ? Path.GetFullPath(path) : throw new FileNotFoundException($"Required app-owned runtime was not found: {name}.", path);
    private static string RequireDirectory(string path, string name) => Directory.Exists(Path.GetFullPath(path))
        ? Path.GetFullPath(path) : throw new DirectoryNotFoundException($"Required {name} was not found.");
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static int AvailablePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private static void Stop(Process process) { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5_000); } } catch (InvalidOperationException) { } }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await AbortActiveBestEffortAsync().ConfigureAwait(false);
        _client.Dispose();
        Stop(_process);
        _process.Dispose();
        _runLock.Dispose();
    }
}
