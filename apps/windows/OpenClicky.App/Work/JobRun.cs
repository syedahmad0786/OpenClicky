using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Work;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>
/// One run of a saved job on the OpenWork runtime, holding everything that run needs apart from the native task's single slot.
/// Created on the UI thread; <see cref="Live"/> and the steering are written from other threads, so they go through Volatile/Interlocked.
/// </summary>
/// <param name="fromTalk">Started from a Talk request: its result's title replaces the placeholder title.</param>
internal sealed class JobRun(WorkJob job, string ask, string? routineId, AgentAccessMode access, IReadOnlyList<string> outputs, bool fromTalk = false)
{
    private JobLiveState _live = JobLiveState.Empty;
    private volatile bool _waitingForScreen;
    private string? _steering;
    private OpenWorkServerService? _server;
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
    public bool FromTalk { get; } = fromTalk;
    /// <summary>A desktop tool call is waiting for another run to give the screen back (the card says so).</summary>
    public bool WaitingForScreen { get => _waitingForScreen; set => _waitingForScreen = value; }
    /// <summary>The run's row in Tasks.</summary>
    public string TaskId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The run's checklist and step phrases so far. Written by the engine's polling thread, read by the shell and when the run is recorded.</summary>
    public JobLiveState Live { get => Volatile.Read(ref _live); set => Volatile.Write(ref _live, value); }

    /// <summary>The user's latest "change task" instruction for this run, if any.</summary>
    public string? Steering => Volatile.Read(ref _steering);
    public int SteeringRevision => Volatile.Read(ref _steeringRevision);

    /// <summary>The run's OpenWork server once it has started; a steer goes into its running session. Set on the run's thread.</summary>
    public OpenWorkServerService? Server { get => Volatile.Read(ref _server); set => Volatile.Write(ref _server, value); }

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

/// <summary>
/// An open run as the screen sees it: a job run or the native task, registered for the run's whole life under its desktop lease
/// holder name. Esc, the drive cards, take-back and the settings guard act through this; its access snapshot never widens.
/// </summary>
internal sealed class RunHandle(string holder, CancellationTokenSource cts, AgentTaskControl control, string taskId,
    AgentAccessMode access, DriveSession drive, JobRun? job)
{
    private long _releasedAtTicks; // UTC ticks of the last time it gave the lease back; 0 while it holds it (or never held it)

    public string Holder { get; } = holder;
    public CancellationTokenSource Cts { get; } = cts;
    public AgentTaskControl Control { get; } = control;
    public string TaskId { get; } = taskId;
    public AgentAccessMode Access { get; } = access;
    public DriveSession Drive { get; } = drive;
    public JobRun? Job { get; } = job;

    public DateTimeOffset? ReleasedAt => Interlocked.Read(ref _releasedAtTicks) is var t and > 0 ? new DateTimeOffset(t, TimeSpan.Zero) : null;
    public void MarkHolding() => Interlocked.Exchange(ref _releasedAtTicks, 0);
    public void MarkReleased(DateTimeOffset at) => Interlocked.Exchange(ref _releasedAtTicks, at.UtcTicks);

    /// <summary>Cancels the run and lets a paused run see it; a no-op once the run has ended.</summary>
    public void Stop()
    {
        try { Cts.Cancel(); } catch (ObjectDisposedException) { }
        Control.Resume();
    }
}
