using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace OpenClicky.Providers;

public enum OpenCodeTaskUpdateKind
{
    Started,
    Status,
    Message,
    PermissionRequested,
    PermissionResolved,
    Completed,
    Failed,
    Aborted,
    ToolUsed,
}

public sealed record OpenCodeTaskUpdate(
    OpenCodeTaskUpdateKind Kind,
    string SessionId,
    string Text,
    OpenCodeMessageSummary? Message = null,
    OpenCodePermissionRequest? Permission = null,
    OpenCodeToolCall? Tool = null);

public sealed record OpenCodeTaskResult(
    string SessionId,
    IReadOnlyList<OpenCodeMessageSummary> Messages);

public sealed record OpenCodeRuntimeStatus(
    string Version,
    IReadOnlyList<string> ConnectedProviders,
    string? ConfiguredModel,
    IReadOnlyDictionary<string, string> DefaultModels,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Models);

/// <summary>Owns one hidden, password-protected OpenCode process for one selected workspace.</summary>
public sealed class OpenCodeWorkspaceService : IAsyncDisposable
{
    public const string SupportedVersion = "1.18.0";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan MissingTerminalTimeout = TimeSpan.FromSeconds(5);
    private readonly Process _process;
    private readonly OpenCodeTaskClient _client;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private string? _activeSessionId;
    private bool _disposed;

    private OpenCodeWorkspaceService(
        string workspacePath,
        Process process,
        OpenCodeTaskClient client,
        OpenCodeRuntimeStatus status)
    {
        WorkspacePath = workspacePath;
        _process = process;
        _client = client;
        Status = status;
    }

    public string WorkspacePath { get; }

    public OpenCodeRuntimeStatus Status { get; private set; }

    public static async Task<OpenCodeWorkspaceService> StartAsync(
        string workspacePath,
        string? executablePath = null,
        CancellationToken cancellationToken = default,
        OpenCodeCompanionConfiguration? companion = null)
    {
        var workspace = ValidateWorkspace(workspacePath);
        var executable = ResolveExecutable(executablePath);
        var port = GetAvailablePort();
        var passwordBytes = RandomNumberGenerator.GetBytes(32);
        var password = Convert.ToBase64String(passwordBytes);
        CryptographicOperations.ZeroMemory(passwordBytes);
        var process = new Process
        {
            StartInfo = CreateStartInfo(executable, workspace, port, password, companion),
            EnableRaisingEvents = true,
        };

        OpenCodeTaskClient? client = null;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The installed OpenCode runtime could not be started.");
            }

            _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            client = new OpenCodeTaskClient($"http://127.0.0.1:{port}/", password: password);
            var health = await WaitForHealthAsync(process, client, cancellationToken).ConfigureAwait(false);
            if (!health.Healthy)
            {
                throw new InvalidOperationException("The local OpenCode runtime reported that it is not healthy.");
            }

            EnsureSupportedVersion(health.Version);

            var providers = await client.GetProviderStatusAsync(cancellationToken).ConfigureAwait(false);
            var configuredModel = await client.GetConfiguredModelAsync(cancellationToken).ConfigureAwait(false);
            return new OpenCodeWorkspaceService(
                workspace,
                process,
                client,
                new OpenCodeRuntimeStatus(
                    health.Version!,
                    providers.Connected,
                    configuredModel,
                    providers.DefaultModels,
                    providers.Models));
        }
        catch
        {
            client?.Dispose();
            StopProcess(process);
            process.Dispose();
            throw;
        }
    }

    public async Task<OpenCodeTaskResult> RunTaskAsync(
        string title,
        string prompt,
        Func<OpenCodePermissionRequest, CancellationToken, Task<bool>> confirmPermission,
        IProgress<OpenCodeTaskUpdate>? progress = null,
        CancellationToken cancellationToken = default) =>
        await RunTaskAsync(
                title,
                prompt,
                null,
                confirmPermission,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<OpenCodeTaskResult> RunTaskAsync(
        string title,
        string prompt,
        OpenCodeModelSelection? model,
        Func<OpenCodePermissionRequest, CancellationToken, Task<bool>> confirmPermission,
        IProgress<OpenCodeTaskUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmPermission);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var providers = await _client.GetProviderStatusAsync(cancellationToken).ConfigureAwait(false);
            var configuredModel = await _client.GetConfiguredModelAsync(cancellationToken).ConfigureAwait(false);
            Status = Status with
            {
                ConnectedProviders = providers.Connected,
                ConfiguredModel = configuredModel,
                DefaultModels = providers.DefaultModels,
                Models = providers.Models,
            };
            if (providers.Connected.Count == 0)
            {
                throw new InvalidOperationException(
                    "OpenCode has no connected provider. Connect one in OpenCode before running this task.");
            }

            model ??= ResolveConfiguredModel(configuredModel, providers);
            if (model is null)
            {
                throw new InvalidOperationException(
                    "Choose an exact OpenCode provider and model before running this task.");
            }

            if (!providers.Models.TryGetValue(model.ProviderId, out var usableModels)
                || !usableModels.Contains(model.ModelId, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"OpenCode model '{model.ProviderId}/{model.ModelId}' is not available from a connected provider.");
            }

            var session = await _client.CreateSessionAsync(title, cancellationToken).ConfigureAwait(false);
            _activeSessionId = session.Id;
            Report(progress, OpenCodeTaskUpdateKind.Started, session.Id, "OpenCode task started.");
            await _client.SendPromptAsync(session.Id, prompt, model, cancellationToken).ConfigureAwait(false);

            var permissionIds = new HashSet<string>(StringComparer.Ordinal);
            var messageText = new Dictionary<string, string>(StringComparer.Ordinal);
            string? previousStatusText = null;
            DateTimeOffset? idleWithoutTerminalSince = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_process.HasExited)
                {
                    throw new InvalidOperationException("The local OpenCode runtime stopped before the task completed.");
                }

                foreach (var permission in await _client.GetPendingPermissionsAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (permission.SessionId != session.Id || !permissionIds.Add(permission.Id))
                    {
                        continue;
                    }

                    Report(
                        progress,
                        OpenCodeTaskUpdateKind.PermissionRequested,
                        session.Id,
                        permission.Permission,
                        permission: permission);
                    var approved = await confirmPermission(permission, cancellationToken).ConfigureAwait(false);
                    await _client.ReplyToPermissionAsync(permission.Id, approved, cancellationToken).ConfigureAwait(false);
                    Report(
                        progress,
                        OpenCodeTaskUpdateKind.PermissionResolved,
                        session.Id,
                        approved ? "Allowed once." : "Rejected.",
                        permission: permission);
                    if (!approved)
                    {
                        throw new OperationCanceledException("OpenCode task stopped because permission was rejected.");
                    }
                }

                var snapshot = await _client.GetMessagesAsync(session.Id, cancellationToken).ConfigureAwait(false);
                foreach (var message in snapshot.Messages)
                {
                    messageText.TryGetValue(message.Id, out var previousText);
                    messageText[message.Id] = message.Text;
                    if (!ShouldReportMessage(previousText, message))
                    {
                        continue;
                    }

                    Report(
                        progress,
                        OpenCodeTaskUpdateKind.Message,
                        session.Id,
                        message.Text,
                        message: message);
                }

                var statusText = FormatStatusProgress(snapshot.Status);
                if (!string.Equals(previousStatusText, statusText, StringComparison.Ordinal))
                {
                    previousStatusText = statusText;
                    Report(progress, OpenCodeTaskUpdateKind.Status, session.Id, statusText);
                }

                if (GetAssistantError(snapshot.Messages) is { } assistantError)
                {
                    throw new InvalidOperationException("OpenCode task failed: " + assistantError);
                }

                if (snapshot.Status.Type == "idle" && HasTerminalAssistantMessage(snapshot.Messages))
                {
                    Report(progress, OpenCodeTaskUpdateKind.Completed, session.Id, "OpenCode run finished.");
                    return new OpenCodeTaskResult(session.Id, snapshot.Messages);
                }

                idleWithoutTerminalSince = snapshot.Status.Type == "idle"
                    ? idleWithoutTerminalSince ?? DateTimeOffset.UtcNow
                    : null;
                if (MissingTerminalTimedOut(idleWithoutTerminalSince, DateTimeOffset.UtcNow))
                {
                    throw new InvalidOperationException(
                        "OpenCode became idle without returning a completed assistant message. No successful result was received.");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await TryAbortAsync().ConfigureAwait(false);
            if (_activeSessionId is { } sessionId)
            {
                Report(progress, OpenCodeTaskUpdateKind.Aborted, sessionId, "OpenCode task stopped.");
            }

            throw;
        }
        catch (Exception ex)
        {
            await TryAbortAsync().ConfigureAwait(false);
            if (_activeSessionId is { } sessionId)
            {
                Report(progress, OpenCodeTaskUpdateKind.Failed, sessionId, ex.Message);
            }

            throw;
        }
        finally
        {
            _activeSessionId = null;
            _runLock.Release();
        }
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeSessionId is { } sessionId)
        {
            await _client.AbortAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await TryAbortAsync().ConfigureAwait(false);
        _client.Dispose();
        StopProcess(_process);
        _process.Dispose();
        _runLock.Dispose();
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string workspacePath,
        int port,
        string password,
        OpenCodeCompanionConfiguration? companion = null)
    {
        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "serve", "--pure", "--hostname", "127.0.0.1", "--port", port.ToString(),
        })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["OPENCODE_SERVER_PASSWORD"] = password;
        start.Environment["OPENCODE_CONFIG_CONTENT"] = companion?.ToConfigJson()
            ?? "{\"permission\":{\"*\":\"ask\",\"external_directory\":\"deny\"}}";
        if (companion is not null)
        {
            // Keep the companion's engine history and provider configuration separate from personal OpenCode.
            foreach (var kind in new[] { "DATA", "CONFIG", "STATE", "CACHE" })
            {
                var directory = Path.Combine(workspacePath, ".runtime", kind.ToLowerInvariant());
                Directory.CreateDirectory(directory);
                start.Environment["XDG_" + kind + "_HOME"] = directory;
            }
        }
        return start;
    }

    internal static string ValidateWorkspace(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var fullPath = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("The selected OpenCode workspace does not exist.");
        }

        if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A drive root is too broad for an OpenCode workspace.", nameof(workspacePath));
        }

        return fullPath;
    }

    internal static bool HasTerminalAssistantMessage(IReadOnlyList<OpenCodeMessageSummary> messages)
    {
        // A user message newer than the answer (a steer posted as the engine finished) is still to be answered.
        var message = messages.LastOrDefault(message => message.Role is "assistant" or "user");
        return message is { Role: "assistant" }
            && !string.IsNullOrWhiteSpace(message.Text)
            && message.FinishReason is "stop" or "length" or "content-filter" or "unknown"
            && message.Error is null;
    }

    internal static string? GetAssistantError(IReadOnlyList<OpenCodeMessageSummary> messages) =>
        messages.LastOrDefault(message => message.Role == "assistant" && message.Error is not null)?.Error
        ?? (messages.LastOrDefault(message => message.Role == "assistant")?.FinishReason == "error"
            ? "OpenCode returned an assistant error without details."
            : null);

    internal static bool MissingTerminalTimedOut(DateTimeOffset? idleSince, DateTimeOffset now) =>
        idleSince is { } started && now - started >= MissingTerminalTimeout;

    internal static bool ShouldReportMessage(string? previousText, OpenCodeMessageSummary message) =>
        !string.IsNullOrWhiteSpace(message.Text)
        && !string.Equals(previousText, message.Text, StringComparison.Ordinal);

    internal static string FormatStatusProgress(OpenCodeSessionStatus status)
    {
        var message = status.Message?.Trim();
        return string.IsNullOrWhiteSpace(message) ? status.Type : $"{status.Type}: {message}";
    }

    internal static string ResolveExecutable(
        string? executablePath,
        string? appBaseDirectory = null,
        string? processPath = null)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var explicitPath = Path.GetFullPath(executablePath);
            return File.Exists(explicitPath)
                ? explicitPath
                : throw new FileNotFoundException("The selected OpenCode executable does not exist.", explicitPath);
        }

        var names = OperatingSystem.IsWindows() ? new[] { "opencode.exe", "opencode.cmd" } : new[] { "opencode" };
        var actualProcessPath = processPath ?? Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(actualProcessPath)
            && Path.GetDirectoryName(Path.GetFullPath(actualProcessPath)) is { } processDirectory
            && ResolveBundledRuntime(processDirectory, names) is { } processRuntime)
        {
            return processRuntime;
        }

        var appDirectory = Path.GetFullPath(appBaseDirectory ?? AppContext.BaseDirectory);
        if (ResolveBundledRuntime(appDirectory, names) is { } appBaseRuntime)
        {
            return appBaseRuntime;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        var bunCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".bun",
            "bin",
            OperatingSystem.IsWindows() ? "opencode.exe" : "opencode");
        return File.Exists(bunCandidate)
            ? bunCandidate
            : throw new FileNotFoundException("OpenCode is not installed or is not available on PATH.");
    }

    internal static void EnsureSupportedVersion(string? version)
    {
        if (!string.Equals(version, SupportedVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"OpenCode {version ?? "unknown"} is installed; this build requires the app-owned OpenCode {SupportedVersion} runtime.");
        }
    }

    private static string? ResolveBundledRuntime(string appDirectory, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var bundled = Path.Combine(appDirectory, "runtime", name);
            if (File.Exists(bundled))
            {
                return Path.GetFullPath(bundled);
            }
        }

        return null;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static OpenCodeModelSelection? ResolveConfiguredModel(
        string? configuredModel,
        OpenCodeProviderStatus providers)
    {
        if (string.IsNullOrWhiteSpace(configuredModel))
        {
            return null;
        }

        foreach (var providerId in providers.Connected.OrderByDescending(id => id.Length))
        {
            var prefix = providerId + "/";
            if (configuredModel.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new OpenCodeModelSelection(providerId, configuredModel[prefix.Length..]);
            }
        }

        return null;
    }

    private static async Task<OpenCodeHealth> WaitForHealthAsync(
        Process process,
        OpenCodeTaskClient client,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException("The local OpenCode runtime exited during startup.");
            }

            try
            {
                return await client.GetHealthAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                await Task.Delay(PollInterval, timeout.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task TryAbortAsync()
    {
        if (_activeSessionId is not { } sessionId || _process.HasExited)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _client.AbortAsync(sessionId, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // The owned process is stopped during disposal if graceful abort is unavailable.
        }
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void Report(
        IProgress<OpenCodeTaskUpdate>? progress,
        OpenCodeTaskUpdateKind kind,
        string sessionId,
        string text,
        OpenCodeMessageSummary? message = null,
        OpenCodePermissionRequest? permission = null) =>
        progress?.Report(new OpenCodeTaskUpdate(kind, sessionId, text, message, permission));
}
