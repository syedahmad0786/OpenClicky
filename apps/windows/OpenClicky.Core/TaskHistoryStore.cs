using System.Text;
using System.Text.Json;

namespace OpenClicky.Core;

public static class TaskHistoryStore
{
    public const int MaxRuns = 20;
    public const int MaxEventsPerRun = 100;
    public const int MaxFileBytes = 1_048_576;

    private const int CurrentVersion = 1;
    private const int MaxIdLength = 128;
    private const int MaxTaskLength = 4_000;
    private const int MaxStatusLength = 64;
    private const int MaxMessageLength = 4_000;
    private const int MaxToolLength = 200;
    private static readonly object SyncRoot = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
    };
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        AgentProgressStates.Started,
        AgentProgressStates.Planning,
        AgentProgressStates.Proposed,
        AgentProgressStates.AwaitingApproval,
        AgentProgressStates.Executing,
        AgentProgressStates.Observing,
        AgentProgressStates.Completed,
        AgentProgressStates.Stopped,
        AgentProgressStates.Failed,
        AgentProgressStates.Paused,
    };

    public static string DefaultPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenClicky");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "task-history.json");
        }
    }

    public static IReadOnlyList<TaskRunRecord> Load(string? path = null)
    {
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            if (!File.Exists(path))
            {
                return Array.Empty<TaskRunRecord>();
            }

            try
            {
                return Read(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                throw new InvalidDataException(
                    "The saved task history could not be read and was left unchanged.",
                    ex);
            }
        }
    }

    public static IReadOnlyList<TaskRunRecord> MarkInterrupted(IReadOnlyList<TaskRunRecord> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return runs.Select(run => run.Status is AgentProgressStates.Completed or AgentProgressStates.Stopped or AgentProgressStates.Failed
            ? run
            : run with
            {
                Status = AgentProgressStates.Stopped,
                Events = run.Events.Append(new AgentProgress(0, AgentProgressStates.Stopped,
                    "App restarted; this previous task was not resumed.")).TakeLast(MaxEventsPerRun).ToList(),
            }).ToArray();
    }

    public static void Save(IEnumerable<TaskRunRecord> runs, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            if (File.Exists(path))
            {
                _ = Load(path);
            }

            var bounded = runs
                .TakeLast(MaxRuns)
                .Select(run =>
                {
                    ArgumentNullException.ThrowIfNull(run);
                    ArgumentNullException.ThrowIfNull(run.Events);
                    return run with { Events = run.Events.TakeLast(MaxEventsPerRun).ToList() };
                })
                .ToList();
            foreach (var run in bounded)
            {
                ValidateRun(run);
            }

            var bytes = Utf8.GetBytes(JsonSerializer.Serialize(
                new TaskHistoryFile { Version = CurrentVersion, Runs = bounded },
                JsonOptions));
            if (bytes.Length > MaxFileBytes)
            {
                throw new InvalidOperationException($"Task history exceeds the {MaxFileBytes}-byte limit.");
            }

            WriteAtomically(path, bytes);
        }
    }

    internal static void ValidateRun(TaskRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ValidateText(run.Id, MaxIdLength, nameof(run.Id), allowEmpty: false);
        ValidateText(run.Task, MaxTaskLength, nameof(run.Task), allowEmpty: false);
        ValidateStatus(run.Status, nameof(run.Status));
        ValidateText(run.AccessMode, MaxStatusLength, nameof(run.AccessMode), allowEmpty: false);
        ArgumentNullException.ThrowIfNull(run.Events);
        if (run.StartedAt == default)
        {
            throw new ArgumentException("Task start time is required.", nameof(run));
        }

        if (run.Events.Count > MaxEventsPerRun)
        {
            throw new ArgumentOutOfRangeException(nameof(run), $"A task run is limited to {MaxEventsPerRun} events.");
        }

        foreach (var progress in run.Events)
        {
            ArgumentNullException.ThrowIfNull(progress);
            if (progress.Step < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(run), "Task progress steps cannot be negative.");
            }

            ValidateStatus(progress.Status, nameof(progress.Status));
            ValidateText(progress.Message, MaxMessageLength, nameof(progress.Message), allowEmpty: false);
            if (progress.Tool is not null)
            {
                ValidateText(progress.Tool, MaxToolLength, nameof(progress.Tool), allowEmpty: true);
            }
        }
    }

    private static IReadOnlyList<TaskRunRecord> Read(string path)
    {
        string json;
        try
        {
            json = Utf8.GetString(ReadBoundedBytes(path));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Task history is not valid UTF-8.", ex);
        }

        var file = JsonSerializer.Deserialize<TaskHistoryFile>(json, JsonOptions)
            ?? throw new InvalidDataException("Task history is empty.");
        if (file.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported task history version {file.Version}.");
        }

        if (file.Runs is null || file.Runs.Count > MaxRuns || file.Runs.Any(run => run is null))
        {
            throw new InvalidDataException($"Task history must contain no more than {MaxRuns} valid runs.");
        }

        try
        {
            foreach (var run in file.Runs)
            {
                ValidateRun(run);
            }
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Task history contains invalid values.", ex);
        }

        return file.Runs;
    }

    private static byte[] ReadBoundedBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxFileBytes)
        {
            throw new InvalidDataException($"Task history exceeds the {MaxFileBytes}-byte limit.");
        }

        using var buffer = new MemoryStream((int)stream.Length);
        var chunk = new byte[8_192];
        while (buffer.Length <= MaxFileBytes)
        {
            var read = stream.Read(chunk, 0, Math.Min(chunk.Length, MaxFileBytes + 1 - (int)buffer.Length));
            if (read == 0)
            {
                return buffer.ToArray();
            }

            buffer.Write(chunk, 0, read);
        }

        throw new InvalidDataException($"Task history exceeds the {MaxFileBytes}-byte limit.");
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       8_192,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateStatus(string value, string parameterName)
    {
        ValidateText(value, MaxStatusLength, parameterName, allowEmpty: false);
        if (!AllowedStatuses.Contains(value))
        {
            throw new ArgumentException($"Unsupported task status '{value}'.", parameterName);
        }
    }

    private static void ValidateText(string value, int maxLength, string parameterName, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value is limited to {maxLength} characters.");
        }
    }

    private sealed class TaskHistoryFile
    {
        public int Version { get; set; }
        public List<TaskRunRecord>? Runs { get; set; }
    }
}

/// <summary>Encodes a stored local run as an AG-UI 1.0 NDJSON export; this is not a live transport.</summary>
public static class AgUiTaskEvents
{
    public const string ThreadId = "openclicky-local-tasks";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Encode(TaskRunRecord run)
    {
        TaskHistoryStore.ValidateRun(run);
        var events = new List<object>
        {
            new
            {
                type = "RUN_STARTED",
                threadId = ThreadId,
                runId = run.Id,
                timestamp = run.StartedAt.ToUnixTimeMilliseconds(),
            },
        };

        foreach (var progress in run.Events)
        {
            var stepName = $"{progress.Step}:{progress.Status}";
            var metadata = new { threadId = ThreadId, runId = run.Id };
            events.Add(new { type = "STEP_STARTED", stepName, metadata });
            events.Add(new
            {
                type = "CUSTOM",
                name = "openclicky.task.progress",
                value = new
                {
                    progress.Step,
                    progress.Status,
                    progress.Message,
                    progress.Tool,
                },
                metadata,
            });
            events.Add(new { type = "STEP_FINISHED", stepName, metadata });
        }

        if (run.Status == AgentProgressStates.Failed)
        {
            events.Add(new
            {
                type = "RUN_ERROR",
                message = run.Events.LastOrDefault(progress => progress.Status == AgentProgressStates.Failed)?.Message
                    ?? "Task failed.",
                code = "OPENCLICKY_TASK_FAILED",
                metadata = new { threadId = ThreadId, runId = run.Id },
            });
        }
        else if (run.Status is AgentProgressStates.Completed or AgentProgressStates.Stopped)
        {
            events.Add(new
            {
                type = "RUN_FINISHED",
                threadId = ThreadId,
                runId = run.Id,
                result = new { run.Status, run.AccessMode },
            });
        }

        return string.Join('\n', events.Select(item => JsonSerializer.Serialize(item, JsonOptions)));
    }
}
