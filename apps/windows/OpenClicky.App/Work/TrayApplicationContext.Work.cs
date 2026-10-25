using System.Collections.Concurrent;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>
/// Work: saved jobs run on the OpenWork runtime, each with its own <see cref="JobRun"/>, up to five at once (<see cref="JobAdmission"/>);
/// the rest wait in order. A native task (Talk "do X" off the OpenWork runtime) keeps the single slot (<c>_taskCts</c>, ...) and
/// runs beside the jobs. Only one run may use the screen and input at a time (<see cref="DesktopLease"/>).
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private readonly WorkStore _work = new();
    private readonly System.Windows.Forms.Timer _routineTimer = new() { Interval = 60_000 };
    /// <summary>Running job runs by job id. Added on admission, removed when the run ends (on the run's thread).</summary>
    private readonly ConcurrentDictionary<string, JobRun> _jobRuns = new(StringComparer.Ordinal);
    /// <summary>Which jobs hold one of the five slots and which wait. The authority on starting a job; <see cref="_jobRuns"/> follows it.</summary>
    private readonly JobAdmission _admission = new();
    /// <summary>The screen and input: a job takes it per desktop tool call, a native task for its whole run.</summary>
    private readonly DesktopLease _lease = new();
    /// <summary>Every open run (jobs and the native task) by lease holder name, for the run's whole life.</summary>
    private readonly ConcurrentDictionary<string, RunHandle> _runHandles = new(StringComparer.Ordinal);
    /// <summary>The run on the screen: the lease holder, or the run that held it last while that run is still open (a job gives the
    /// lease back between tool calls but is still the one driving). Esc, the drive cards and take-back act on it.</summary>
    private RunHandle? _screen;
    /// <summary>UI thread: how each queued job was asked to start, and the job as it was before it was queued.</summary>
    private readonly Dictionary<string, QueuedStart> _queuedStarts = new(StringComparer.Ordinal);
    /// <summary>UI thread: each job card's output files, so a push does not hit the disk; dropped when a job finishes or is opened (also mid-run).</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _jobOutputs = new(StringComparer.Ordinal);
    /// <summary>UI thread: the follow-up each job last started with (none: its own request), so Try again asks the same.
    /// ponytail: in memory only; after a restart Try again runs the job's own request.</summary>
    private readonly Dictionary<string, string?> _lastFollowUps = new(StringComparer.Ordinal);
    /// <summary>A queued job's ask grows with each steer up to this.</summary>
    private const int MaxQueuedAsk = 8_000;
    /// <summary>UI thread: scheduled runs in flight; the PC stays awake while any is.</summary>
    private int _routineRuns;

    private sealed record QueuedStart(string? FollowUp, string? RoutineId, bool FromTalk, WorkJob Before);

    /// <summary>A native task or a job run is open (running or paused).</summary>
    private bool TaskOpen => _taskCts is not null || !_jobRuns.IsEmpty;

    /// <summary>The runs the last pause paused: a resume (button or voice) resumes these, wherever the screen has gone since.</summary>
    private IReadOnlyList<RunHandle> _lastPaused = [];

    /// <summary>The last holder while it still counts as on the screen (<see cref="DesktopLease.StillOnScreen"/>): holding the
    /// lease, driving, or within 10 s of giving it back. A job that only looked at the screen a while ago is a background job.</summary>
    private RunHandle? ScreenRun => Volatile.Read(ref _screen) is { } run
        && DesktopLease.StillOnScreen(_lease.Holder == run.Holder, run.Drive.Phase, run.ReleasedAt, DateTimeOffset.UtcNow) ? run : null;
    private RunHandle? NativeRun => _runHandles.TryGetValue(DesktopLease.NativeTaskHolder, out var run) ? run : null;

    /// <summary>The one run a resume or a "change task" acts on: the run on the screen, else the native task.</summary>
    private RunHandle? TargetRun => ScreenRun ?? NativeRun;

    /// <summary>What Esc, Stop, a pause and take-back act on: the run on the screen plus any native task. Other jobs keep going.</summary>
    private IEnumerable<RunHandle> ForegroundRuns()
    {
        var screen = ScreenRun;
        if (screen is not null) yield return screen;
        if (NativeRun is { } native && !ReferenceEquals(native, screen)) yield return native;
    }

    /// <summary>Some open run is not paused, so it may send input at any moment (talk typing, drawn buttons and detaching hold back).</summary>
    private bool RunMayAct => _runHandles.Any(entry => !entry.Value.Control.IsPaused);

    /// <summary>Admits a native task (<paramref name="work"/> null) into the single slot, or a job run that <see cref="_admission"/> let in.</summary>
    private bool TryAdmit(CancellationTokenSource native, JobRun? work) => work is null
        ? Interlocked.CompareExchange(ref _taskCts, native, null) is null
        : _jobRuns.TryAdd(work.Job.Id, work);

    /// <summary>The run's thread, as it ends: only this run leaves the table.</summary>
    private void EndJobRun(JobRun run) => _jobRuns.TryRemove(new KeyValuePair<string, JobRun>(run.Job.Id, run));

    /// <summary>Esc, Stop and the Esc pill: stops the run on the screen and any native task.</summary>
    private void CancelTask()
    {
        try { _taskCts?.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var run in ForegroundRuns()) run.Stop();
    }

    /// <summary>Quitting, or a new provider or proxy (the engine every run talks to): stops every run.</summary>
    private void CancelAllRuns()
    {
        try { _taskCts?.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var run in _runHandles.Values) run.Stop();
        foreach (var run in _jobRuns.Values) { run.Cancel(); run.Control.Resume(); } // one admitted a moment ago may not have a handle yet
    }

    /// <summary>A narrower access mode stops the runs that started wider; nothing ever widens a running job.</summary>
    private void CancelRunsWiderThan(AgentAccessMode mode)
    {
        foreach (var run in _runHandles.Values)
            if (run.Access > mode) { Log("run_stopped_access_narrowed"); run.Stop(); }
    }

    /// <summary>Any thread, from the lease: the new holder becomes the run on the screen. Its drive is watched from the cursor's
    /// position now (another run may have moved it), and the last run's Esc pill comes down.</summary>
    private void OnLeaseChanged()
    {
        if (_lease.Holder is not { } holder) { Volatile.Read(ref _screen)?.MarkReleased(DateTimeOffset.UtcNow); return; }
        if (!_runHandles.TryGetValue(holder, out var run)) return;
        run.MarkHolding();
        if (run.Control.IsPaused) OnUi(RequestShellPush); // paused before it took the screen: its card says it holds it
        var previous = Interlocked.Exchange(ref _screen, run);
        if (ReferenceEquals(previous, run)) return;
        var p = System.Windows.Forms.Cursor.Position;
        run.Drive.Rebase(p.X, p.Y);
        if (previous is not null) OnUi(HideDrivingPill);
    }

    /// <summary>The paused runs a resume acts on: those the last pause paused that are still open and paused; else every open
    /// paused run, so a paused job is never left out of reach.</summary>
    private IReadOnlyList<RunHandle> ResumeTargets()
    {
        var last = Volatile.Read(ref _lastPaused).Where(run => run.Control.IsPaused && IsOpen(run)).ToList();
        return last.Count > 0 ? last : _runHandles.Values.Where(run => run.Control.IsPaused).ToList();
    }

    private bool IsOpen(RunHandle run) => _runHandles.TryGetValue(run.Holder, out var open) && ReferenceEquals(open, run);

    /// <summary>Takes the screen for <paramref name="run"/>; while it waits, its card (or Tasks row) says so.</summary>
    private async Task<IDisposable> LeaseScreenAsync(RunHandle run, CancellationToken token)
    {
        var pending = _lease.AcquireAsync(run.Holder, token);
        if (pending.IsCompleted) return await pending.ConfigureAwait(false);
        SetWaitingForScreen(run, true);
        try { return await pending.ConfigureAwait(false); }
        finally { SetWaitingForScreen(run, false); }
    }

    private void SetWaitingForScreen(RunHandle run, bool waiting)
    {
        if (run.Job is { } job) { job.WaitingForScreen = waiting; OnUi(RequestShellPush); }
        else if (waiting) RecordProgress(run.TaskId, new(0, "planning", DesktopLease.WaitingLine));
    }

    private Task<bool> RunJobAsync(string jobId, string? followUp) => Task.FromResult(StartJob(jobId, followUp, routineId: null));

    /// <summary>UI thread, for a Talk request on the OpenWork runtime: a job under the built-in companion, titled with the start of the
    /// request until its result names it, run as any job (so it shows in the deck).</summary>
    private void StartTalkJob(string request)
    {
        WorkJob job;
        try
        {
            var title = request.Trim().ReplaceLineEndings(" ");
            job = _work.CreateJob(WorkStore.DefaultCompanionSlug, title[..Math.Min(title.Length, 60)].Trim(), request.Trim());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("talk_job_error=" + ex.GetType().Name);
            _talk.ReportAssistantMessage("I couldn't start that as a job: " + ex.Message);
            return;
        }
        IndexWork();
        StartJob(job.Id, null, routineId: null, fromTalk: true);
    }

    /// <summary>
    /// Starts a saved job on the OpenWork runtime with its own folder as workspace, or queues it ("Waiting for a free slot") when five
    /// are running. Returns false when it could neither start nor queue. <paramref name="queued"/>: a queued job that now holds a slot.
    /// </summary>
    private bool StartJob(string jobId, string? followUp, string? routineId, bool fromTalk = false, QueuedStart? queued = null)
    {
        if (_settings.AuthProvider != AuthProvider.ChatGPT || !ProviderFactory.IsChatGptSignedIn(_secrets))
        {
            ClassicMessage("Jobs run on the bundled OpenWork runtime. Continue with ChatGPT in Settings first.");
            return AbandonSlot(jobId, queued);
        }
        if (queued is null)
        {
            // One step, before any database write: a run that finishes in between hands this job its slot instead of stranding it.
            switch (_admission.TryStartOrEnqueue(jobId))
            {
                case JobAdmission.Outcome.Refused:
                    ClassicMessage("That job is already running or waiting for a slot.");
                    return false;
                case JobAdmission.Outcome.Queued:
                    return Queue(jobId, followUp, routineId, fromTalk);
            }
        }
        string prompt;
        JobRun run;
        try
        {
            var saved = _work.GetJob(jobId);
            var companion = _work.GetCompanion(saved.CompanionSlug);
            var outputs = _work.Outputs(saved);
            prompt = JobPrompt.Build(companion, _work.ReadNotes(companion.Slug), saved, outputs, followUp, scheduled: routineId is not null);
            // The access mode is snapshotted here, once: a settings change never widens a running job.
            run = new JobRun(saved, string.IsNullOrWhiteSpace(followUp) ? saved.Prompt : followUp.Trim(), routineId,
                _sandboxDemo ? AgentAccessMode.ConfirmEach : _settings.AgentAccess, outputs, fromTalk);
            try { _work.RetireNote(run.Job); } // the agent starts a fresh note.md; the last one stays readable
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("job_note_error=" + ex.GetType().Name); }
            _work.SetJobState(run.Job.Id, WorkJobStates.Running, DateTimeOffset.UtcNow);
            _lastFollowUps[jobId] = string.IsNullOrWhiteSpace(followUp) ? null : followUp;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClassicMessage("Could not start the job: " + ex.Message);
            return AbandonSlot(jobId, queued, slotTaken: true);
        }
        IndexWork();
        _ = StartAgentTask(prompt, run);
        if (!_jobRuns.TryGetValue(run.Job.Id, out var admitted) || !ReferenceEquals(admitted, run))
        {
            run.Cts.Dispose();
            Restore(queued?.Before ?? run.Job);
            ReleaseSlot(run.Job.Id);
            return false;
        }
        if (routineId is not null) KeepAwake(true);
        _home.RefreshWork();
        RequestShellPush();
        return true;
    }

    /// <summary>UI thread, after <see cref="JobAdmission.TryStartOrEnqueue"/> queued the job: it waits its turn, its card saying so.
    /// A slot handed to it meanwhile starts it once this returns (<see cref="StartQueued"/> is posted to this thread).</summary>
    private bool Queue(string jobId, string? followUp, string? routineId, bool fromTalk)
    {
        WorkJob before;
        try { before = _work.GetJob(jobId); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClassicMessage("Could not queue the job: " + ex.Message);
            ReleaseSlot(jobId); // out of the queue, or on to the next job if it was just given a slot
            return false;
        }
        _queuedStarts[jobId] = new(followUp, routineId, fromTalk, before);
        try { _work.SetJobState(jobId, WorkJobStates.Queued, DateTimeOffset.UtcNow); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClassicMessage("Could not queue the job: " + ex.Message);
            _queuedStarts.Remove(jobId);
            ReleaseSlot(jobId);
            return false;
        }
        IndexWork();
        _home.RefreshWork();
        RequestShellPush();
        return true;
    }

    /// <summary>A start that failed: a queued job goes back to how it was, and a slot it held passes to the next queued job.</summary>
    private bool AbandonSlot(string jobId, QueuedStart? queued, bool slotTaken = false)
    {
        if (queued is not null) Restore(queued.Before);
        if (queued is not null || slotTaken) ReleaseSlot(jobId);
        return false;
    }

    /// <summary>Any thread: the job's slot is free; the queued job that <see cref="JobAdmission.Finish"/> moved into it starts.</summary>
    private void ReleaseSlot(string jobId)
    {
        if (_admission.Finish(jobId) is { } next) OnUi(() => StartQueued(next));
    }

    /// <summary>UI thread: a queued job's turn.</summary>
    private void StartQueued(string jobId)
    {
        if (_queuedStarts.Remove(jobId, out var start)) StartJob(jobId, start.FollowUp, start.RoutineId, start.FromTalk, start);
        else ReleaseSlot(jobId); // stopped from its card in the meantime: the slot goes on
    }

    /// <summary>Puts a job that never ran back as it was (state, summary and time).</summary>
    private void Restore(WorkJob job)
    {
        try { _work.RecordJobRun(job.Id, job.Status, job.Summary, job.LastRunAt ?? job.CreatedAt); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_restore_error=" + ex.GetType().Name); }
        IndexWork();
        _home.RefreshWork();
        RequestShellPush();
    }

    private void RunDueRoutine()
    {
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return;
        Routine? due;
        // Routines take the same slots as the user's jobs; a job already running or waiting is left alone.
        try { due = _work.Routines().FirstOrDefault(r => RoutineSchedule.IsDue(r, DateTimeOffset.UtcNow) && !_admission.Contains(r.JobId)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("routine_read_error=" + ex.GetType().Name); return; }
        if (due is null || StartJob(due.JobId, null, due.Id)) return;
        try { _work.SaveRoutine(RoutineSchedule.AfterRun(due, false, "Could not start; see Chat.", DateTimeOffset.UtcNow, TimeZoneInfo.Local)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("routine_save_error=" + ex.GetType().Name); }
    }

    /// <summary>Records a finished run. <paramref name="text"/> is the reply without its result block; <paramref name="result"/> is stored
    /// for the job card (a run without one clears the last run's). The routine, user step and lessons it names are only stored here.
    /// A Talk job takes its result's title.</summary>
    private void FinishJob(JobRun work, string status, string text, JobResult? result = null)
    {
        var summary = status == AgentProgressStates.Completed ? result?.Summary ?? JobPrompt.FirstSentence(text) : text;
        // The run's last checklist is stored with its result so the card keeps it after the run (even a stopped or failed one).
        if (work.Live.Plan.Count > 0) result = (result ?? JobResult.Parse("", "")) with { Plan = work.Live.Plan };
        try
        {
            _work.RecordJobRun(work.Job.Id, status, summary, DateTimeOffset.UtcNow);
            _work.SetJobResult(work.Job.Id, result?.ToJson() ?? "");
            if (work.FromTalk && result?.Title is { } title) _work.RenameJob(work.Job.Id, title);
            _work.AppendRunLog(work.Job, work.Ask, status, text);
            if (work.RoutineId is { } routineId && _work.Routines(work.Job.Id).FirstOrDefault(r => r.Id == routineId) is { } routine)
                _work.SaveRoutine(RoutineSchedule.AfterRun(routine, status == AgentProgressStates.Completed, summary[..Math.Min(summary.Length, 200)],
                    DateTimeOffset.UtcNow, TimeZoneInfo.Local));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_record_error=" + ex.GetType().Name); }
        IndexWork();
        OnUi(() =>
        {
            if (work.RoutineId is not null) KeepAwake(false);
            _home.RefreshWork();
            _jobOutputs.Remove(work.Job.Id);
            RequestShellPush();
        });
    }

    /// <summary>Rewrites WORK.md after a job store write; a locked or unwritable file never blocks the job.</summary>
    private void IndexWork()
    {
        try { _work.WriteIndex(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("work_index_error=" + ex.GetType().Name); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    /// <summary>UI thread: keeps the PC from sleeping (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) while any scheduled run is in flight.</summary>
    private void KeepAwake(bool on)
    {
        _routineRuns = Math.Max(0, _routineRuns + (on ? 1 : -1));
        SetThreadExecutionState(_routineRuns > 0 ? 0x80000001u : 0x80000000u);
    }

    /// <summary>UI thread, for the shell push: a job's output files as its card shows them (cached until it finishes or is opened).</summary>
    private IReadOnlyList<string> JobFiles(WorkJob job)
    {
        if (!_jobOutputs.TryGetValue(job.Id, out var files))
            _jobOutputs[job.Id] = files = ShellSource<IReadOnlyList<string>>("job outputs", () =>
            {
                var output = _work.OutputFolder(job);
                return _work.Outputs(job).Take(ShellStateBuilder.MaxFiles).Select(name => Path.Combine(output, name)).ToList();
            }, []);
        return files;
    }

    /// <summary>UI thread, for the shell push: what a job's card says it is doing right now (<paramref name="running"/>: its
    /// newest step phrase), or that it waits for the screen or for a slot. A job that is neither running nor queued has none.</summary>
    private string? JobLiveLine(WorkJob job, JobRun? run, string? running) =>
        run is { Control.IsPaused: true } && _lease.Holder == job.Id ? DesktopLease.PausedHolderLine
        : run is { WaitingForScreen: true } ? DesktopLease.WaitingLine
        : run is not null ? running
        : _queuedStarts.ContainsKey(job.Id) ? JobAdmission.WaitingLine
        : null;

    void IShellHost.OpenJob(string id) => _jobOutputs.Remove(id);

    /// <summary>Stops that job's run, wherever it is (running, paused or waiting for the screen); a queued job leaves the queue.</summary>
    void IShellHost.StopJob(string id)
    {
        if (_jobRuns.TryGetValue(id, out var run))
        {
            run.Cancel();
            run.Control.Resume();
            return;
        }
        if (_queuedStarts.Remove(id, out var queued))
        {
            ReleaseSlot(id); // leaves the queue; if it had just been given a slot, that slot passes on
            Restore(queued.Before);
            return;
        }
        Log("shell: job.stop ignored, that job is not running");
    }

    /// <summary>UI thread. A running job hears it at its next step; a queued one starts with it as part of its ask.
    /// The text is the user's own (untrusted) and is never logged.</summary>
    void IShellHost.SteerJob(string id, string text)
    {
        if (_jobRuns.TryGetValue(id, out var run)) { SteerRun(run, text); return; }
        if (_queuedStarts.TryGetValue(id, out var queued))
        {
            var ask = string.IsNullOrWhiteSpace(queued.FollowUp) ? text : queued.FollowUp.Trim() + "\n" + text;
            if (ask.Length > MaxQueuedAsk) { Log("shell: job.steer ignored, the queued ask is full"); return; }
            _queuedStarts[id] = queued with { FollowUp = ask };
            return;
        }
        Log("shell: job.steer ignored, that job is not running or queued");
    }

    /// <summary>A follow-up is a new run of the job (through admission: it may queue).</summary>
    void IShellHost.FollowUpJob(string id, string text) => StartJob(id, text, routineId: null);

    /// <summary>Try again: a new run asked the same as the last one.</summary>
    void IShellHost.RetryJob(string id) => StartJob(id, _lastFollowUps.GetValueOrDefault(id), routineId: null);

    /// <summary>Any thread: the steer is kept for the run's desktop observations and sent into its running session, if it has one yet.</summary>
    private void SteerRun(JobRun run, string text)
    {
        run.Steer(text);
        if (run.Server is not { } server) { Log("job_steer_kept_for_observations"); return; }
        _ = Task.Run(async () =>
        {
            try { if (!await server.SendToRunningAsync(text, run.Cts.Token).ConfigureAwait(false)) Log("job_steer_not_sent"); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_steer_error=" + ex.GetType().Name); }
        });
    }
}
