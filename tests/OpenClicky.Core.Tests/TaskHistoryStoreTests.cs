using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class TaskHistoryStoreTests
{
    [Fact]
    public void RoundTrip_KeepsNewestRunsAndEvents()
    {
        var path = TemporaryPath();
        try
        {
            var runs = Enumerable.Range(0, TaskHistoryStore.MaxRuns + 2)
                .Select(run => NewRun(
                    $"run-{run}",
                    AgentProgressStates.Completed,
                    Enumerable.Range(0, TaskHistoryStore.MaxEventsPerRun + 5)
                        .Select(step => new AgentProgress(step, AgentProgressStates.Executing, $"event {step}"))
                        .ToList()))
                .ToList();

            TaskHistoryStore.Save(runs, path);
            var loaded = TaskHistoryStore.Load(path);

            Assert.Equal(TaskHistoryStore.MaxRuns, loaded.Count);
            Assert.Equal("run-2", loaded[0].Id);
            Assert.Equal(TaskHistoryStore.MaxEventsPerRun, loaded[0].Events.Count);
            Assert.Equal(5, loaded[0].Events[0].Step);
            Assert.Equal(TaskHistoryStore.MaxEventsPerRun + 4, loaded[0].Events[^1].Step);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void CorruptExistingFile_IsNotLoadedOrOverwritten()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllText(path, "{ invalid-json");
            var original = File.ReadAllBytes(path);

            Assert.Throws<InvalidDataException>(() => TaskHistoryStore.Load(path));
            Assert.Throws<InvalidDataException>(() => TaskHistoryStore.Save(
                new[] { NewRun("run-1", AgentProgressStates.Started) },
                path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void OversizedExistingFile_IsNotReadOrOverwritten()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, new byte[TaskHistoryStore.MaxFileBytes + 1]);

            Assert.Throws<InvalidDataException>(() => TaskHistoryStore.Load(path));
            Assert.Throws<InvalidDataException>(() => TaskHistoryStore.Save(
                new[] { NewRun("run-1", AgentProgressStates.Started) },
                path));
            Assert.Equal(TaskHistoryStore.MaxFileBytes + 1, new FileInfo(path).Length);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Encode_ProducesAgUiLifecycleAndProgressNdjson()
    {
        var run = NewRun(
            "run-1",
            AgentProgressStates.Completed,
            new List<AgentProgress>
            {
                new(1, AgentProgressStates.Planning, "Planning"),
                new(2, AgentProgressStates.Executing, "Opening app", "launch_app"),
            });

        var lines = AgUiTaskEvents.Encode(run).Split('\n');
        Assert.Equal(8, lines.Length);
        using var started = JsonDocument.Parse(lines[0]);
        Assert.Equal("RUN_STARTED", started.RootElement.GetProperty("type").GetString());
        Assert.Equal(AgUiTaskEvents.ThreadId, started.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("run-1", started.RootElement.GetProperty("runId").GetString());

        using var custom = JsonDocument.Parse(lines[2]);
        Assert.Equal("CUSTOM", custom.RootElement.GetProperty("type").GetString());
        Assert.Equal("openclicky.task.progress", custom.RootElement.GetProperty("name").GetString());
        Assert.Equal("launch_app", JsonDocument.Parse(lines[5]).RootElement
            .GetProperty("value").GetProperty("tool").GetString());
        Assert.Equal("run-1", custom.RootElement.GetProperty("metadata").GetProperty("runId").GetString());

        using var finished = JsonDocument.Parse(lines[^1]);
        Assert.Equal("RUN_FINISHED", finished.RootElement.GetProperty("type").GetString());
        Assert.Equal("completed", finished.RootElement.GetProperty("result").GetProperty("status").GetString());
    }

    [Fact]
    public void Encode_FailedRunEndsWithAgUiError()
    {
        var run = NewRun(
            "run-failed",
            AgentProgressStates.Failed,
            new List<AgentProgress> { new(1, AgentProgressStates.Failed, "Window did not open") });

        using var error = JsonDocument.Parse(AgUiTaskEvents.Encode(run).Split('\n')[^1]);
        Assert.Equal("RUN_ERROR", error.RootElement.GetProperty("type").GetString());
        Assert.Equal("Window did not open", error.RootElement.GetProperty("message").GetString());
        Assert.Equal("run-failed", error.RootElement.GetProperty("metadata").GetProperty("runId").GetString());
    }

    [Fact]
    public void Encode_NonterminalRunDoesNotCloseAgUiRun()
    {
        var run = NewRun(
            "run-paused",
            AgentProgressStates.Paused,
            new List<AgentProgress> { new(1, AgentProgressStates.Paused, "Paused by user") });

        var types = AgUiTaskEvents.Encode(run).Split('\n')
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString())
            .ToList();
        Assert.DoesNotContain("RUN_FINISHED", types);
        Assert.DoesNotContain("RUN_ERROR", types);
        Assert.Equal("STEP_FINISHED", types[^1]);
    }

    [Fact]
    public void PersistedExecutingRunIsStoppedAndNeverResumedAfterRestart()
    {
        var path = TemporaryPath();
        try
        {
            TaskHistoryStore.Save([NewRun("run-interrupted", AgentProgressStates.Executing,
                [new(0, AgentProgressStates.Started, "Task started"), new(1, AgentProgressStates.Executing, "Opening app")])], path);

            var recovered = TaskHistoryStore.MarkInterrupted(TaskHistoryStore.Load(path));
            TaskHistoryStore.Save(recovered, path);
            var reopened = Assert.Single(TaskHistoryStore.Load(path));

            Assert.Equal(AgentProgressStates.Stopped, reopened.Status);
            Assert.Equal("App restarted; this previous task was not resumed.", reopened.Events[^1].Message);
            Assert.DoesNotContain(reopened.Events, item => item.Status == AgentProgressStates.Completed);
        }
        finally
        {
            Delete(path);
        }
    }

    private static TaskRunRecord NewRun(
        string id,
        string status,
        List<AgentProgress>? events = null) =>
        new(id, "Open Settings", DateTimeOffset.Parse("2026-10-11T12:00:00Z"), status,
            events ?? new List<AgentProgress> { new(0, status, status) }, "observe");

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"openclicky-task-history-{Guid.NewGuid():N}.json");

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
