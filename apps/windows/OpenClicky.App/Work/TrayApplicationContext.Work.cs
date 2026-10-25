using System.Collections.Concurrent;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>
/// Work: saved jobs run on the OpenWork runtime, each with its own <see cref="JobRun"/>. A native task (Talk "do X") keeps the
/// single slot (<c>_taskCts</c>, <c>_taskControl</c>, ...). Admission is still one at a time: a native task or one job run.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private readonly WorkStore _work = new();
    private readonly System.Windows.Forms.Timer _routineTimer = new() { Interval = 60_000 };
    /// <summary>Running job runs by job id. Added on admission, removed when the run ends (on the run's thread).</summary>
    private readonly ConcurrentDictionary<string, JobRun> _jobRuns = new(StringComparer.Ordinal);
    /// <summary>Makes "nothing open, so admit this run" one step for native tasks and job runs alike.</summary>
    private readonly object _runGate = new();
    /// <summary>UI thread: each job card's output files, so a push does not hit the disk; dropped when a job finishes or is opened (also mid-run).</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _jobOutputs = new(StringComparer.Ordinal);

    /// <summary>A native task or a job run is open (running or paused).</summary>
    private bool TaskOpen => _taskCts is not null || !_jobRuns.IsEmpty;

    /// <summary>The open job run, if any (at most one until runs can overlap). Enumerates without the snapshot <c>Values</c> takes.</summary>
    private JobRun? OpenJobRun
    {
        get
        {
            foreach (var entry in _jobRuns) return entry.Value;
            return null;
        }
    }

    /// <summary>The open run's token source: the job run's, else the native task's (null when nothing is open).</summary>
    private CancellationTokenSource? OpenCts => OpenJobRun?.Cts ?? _taskCts;

    /// <summary>The open run's pause control: the job run's, else the native task's.</summary>
    private AgentTaskControl OpenControl => OpenJobRun?.Control ?? _taskControl;

    /// <summary>Admits a native task (<paramref name="work"/> null, taking the single slot) or a job run when nothing is open.</summary>
    private bool TryAdmit(CancellationTokenSource native, JobRun? work)
    {
        lock (_runGate)
        {
            if (TaskOpen) return false;
            if (work is null) _taskCts = native;
            else _jobRuns[work.Job.Id] = work;
            return true;
        }
    }

    /// <summary>The run's thread, as it ends: only this run leaves the table.</summary>
    private void EndJobRun(JobRun run) => _jobRuns.TryRemove(new KeyValuePair<string, JobRun>(run.Job.Id, run));

    private Task<bool> RunJobAsync(string jobId, string? followUp) => Task.FromResult(StartJob(jobId, followUp, routineId: null));

    /// <summary>Starts a saved job on the OpenWork runtime with its own folder as workspace. Returns false when it could not start.</summary>
    private bool StartJob(string jobId, string? followUp, string? routineId)
    {
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
                _sandboxDemo ? AgentAccessMode.ConfirmEach : _settings.AgentAccess, outputs);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClassicMessage("Could not start the job: " + ex.Message);
            return false;
        }
        if (_settings.AuthProvider != AuthProvider.ChatGPT || !ProviderFactory.IsChatGptSignedIn(_secrets))
        {
            ClassicMessage("Jobs run on the bundled OpenWork runtime. Continue with ChatGPT in Settings first.");
            return false;
        }
        if (TaskOpen)
        {
            ClassicMessage("A task is already open. Let it finish or stop it before running a job.");
            return false;
        }
        try { _work.RetireNote(run.Job); } // the agent starts a fresh note.md; the last one stays readable
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("job_note_error=" + ex.GetType().Name); }
        _work.SetJobState(run.Job.Id, WorkJobStates.Running, DateTimeOffset.UtcNow);
        IndexWork();
        _ = StartAgentTask(prompt, run);
        if (!_jobRuns.TryGetValue(run.Job.Id, out var admitted) || !ReferenceEquals(admitted, run))
        {
            run.Cts.Dispose();
            _work.RecordJobRun(run.Job.Id, run.Job.Status, run.Job.Summary, run.Job.LastRunAt ?? run.Job.CreatedAt);
            IndexWork();
            return false;
        }
        if (routineId is not null) KeepAwake(true);
        _home.RefreshWork();
        RequestShellPush();
        return true;
    }

    private void RunDueRoutine()
    {
        if (TaskOpen || !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return;
        Routine? due;
        try { due = _work.Routines().FirstOrDefault(r => RoutineSchedule.IsDue(r, DateTimeOffset.UtcNow)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("routine_read_error=" + ex.GetType().Name); return; }
        if (due is null || StartJob(due.JobId, null, due.Id)) return;
        try { _work.SaveRoutine(RoutineSchedule.AfterRun(due, false, "Could not start; see Chat.", DateTimeOffset.UtcNow, TimeZoneInfo.Local)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("routine_save_error=" + ex.GetType().Name); }
    }

    /// <summary>Records a finished run. <paramref name="text"/> is the reply without its result block; <paramref name="result"/> is stored
    /// for the job card (a run without one clears the last run's). The routine, user step and lessons it names are only stored here.</summary>
    private void FinishJob(JobRun work, string status, string text, JobResult? result = null)
    {
        var summary = status == AgentProgressStates.Completed ? result?.Summary ?? JobPrompt.FirstSentence(text) : text;
        // The run's last checklist is stored with its result so the card keeps it after the run (even a stopped or failed one).
        if (work.Live.Plan.Count > 0) result = (result ?? JobResult.Parse("", "")) with { Plan = work.Live.Plan };
        try
        {
            _work.RecordJobRun(work.Job.Id, status, summary, DateTimeOffset.UtcNow);
            _work.SetJobResult(work.Job.Id, result?.ToJson() ?? "");
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

    /// <summary>Keeps the PC from sleeping (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) while a scheduled run is in flight; called on the UI thread.</summary>
    private static void KeepAwake(bool on) => SetThreadExecutionState(on ? 0x80000001u : 0x80000000u);

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

    void IShellHost.OpenJob(string id) => _jobOutputs.Remove(id);

    /// <summary>Stops that job's run, wherever it is (running or paused).</summary>
    void IShellHost.StopJob(string id)
    {
        if (!_jobRuns.TryGetValue(id, out var run)) { Log("shell: job.stop ignored, that job is not running"); return; }
        run.Cancel();
        run.Control.Resume();
    }
}
