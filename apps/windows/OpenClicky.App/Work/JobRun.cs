using OpenClicky.Core;
using OpenClicky.Core.Work;

namespace OpenClicky.App;

/// <summary>
/// One run of a saved job on the OpenWork runtime, holding everything that run needs apart from the native task's single slot.
/// Created on the UI thread; <see cref="Live"/> and the steering are written from other threads, so they go through Volatile/Interlocked.
/// </summary>
internal sealed class JobRun(WorkJob job, string ask, string? routineId, AgentAccessMode access, IReadOnlyList<string> outputs)
{
    private JobLiveState _live = JobLiveState.Empty;
    private string? _steering;
    private int _steeringRevision;

    public WorkJob Job { get; } = job;
    public string Ask { get; } = ask;
    public string? RoutineId { get; } = routineId;
    public CancellationTokenSource Cts { get; } = new();
    public AgentTaskControl Control { get; } = new();
    /// <summary>The access mode this run started with. A settings change never widens it.</summary>
    public AgentAccessMode Access { get; } = access;
    /// <summary>The job's output files when the run started (relative to its output folder), as its prompt listed them.
    /// The job card reads the folder itself (<c>JobFiles</c>), so files the run writes show once the card is reopened.</summary>
    public IReadOnlyList<string> Outputs { get; } = outputs;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    /// <summary>The run's row in Tasks.</summary>
    public string TaskId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The run's checklist and step phrases so far. Written by the engine's polling thread, read by the shell and when the run is recorded.</summary>
    public JobLiveState Live { get => Volatile.Read(ref _live); set => Volatile.Write(ref _live, value); }

    /// <summary>The user's latest "change task" instruction for this run, if any.</summary>
    public string? Steering => Volatile.Read(ref _steering);
    public int SteeringRevision => Volatile.Read(ref _steeringRevision);

    public void Steer(string instruction)
    {
        Volatile.Write(ref _steering, instruction);
        Interlocked.Increment(ref _steeringRevision);
    }

    /// <summary>Cancels the run; a no-op once it has ended and disposed its token source.</summary>
    public void Cancel() { try { Cts.Cancel(); } catch (ObjectDisposedException) { } }

    /// <summary>The bounded line shown in Tasks.</summary>
    public string Display => $"{Job.Title}: {Ask}"[..Math.Min(Job.Title.Length + 2 + Ask.Length, 4000)];
}
