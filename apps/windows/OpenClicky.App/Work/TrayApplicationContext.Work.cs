using System.Collections.Concurrent;
using OpenClicky.Agent;
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
    /// <summary>Lessons from finished runs, fed into later job prompts. Kept at &lt;agent root&gt;\howtos.json; <see cref="ApplyAgentFolder"/> swaps it when the root changes.</summary>
    private HowToStore _howTos = null!; // set by ApplyAgentFolder in the constructor, before anything can finish a job
    private string _howTosPath = "";
    /// <summary>The chosen agent folder could not be used and the shell was not up to say so yet.</summary>
    private bool _agentFolderNotice;
    private const string AgentFolderUnusable = "Can't use that folder — new jobs stay in the default folder.";
    private readonly System.Windows.Forms.Timer _routineTimer = new() { Interval = (int)RoutineScheduler.Tick.TotalMilliseconds };
    /// <summary>UI thread: a routine tick is in progress (a start may pump messages; the next tick waits for this one).</summary>
    private bool _routineTicking;
    /// <summary>UI thread: no routine starts before this, set for three minutes after the PC wakes.</summary>
    private DateTimeOffset? _routineHoldUntil;
    /// <summary>UI thread: when the last routine tick ran; a long gap since means the PC slept (<see cref="RoutineScheduler.HoldAfterGap"/>).</summary>
    private DateTimeOffset? _lastRoutineTick;
    /// <summary>UI thread: routines the last tick could not start (the network or sign-in), by id, with the line their row shows. In memory only.</summary>
    private IReadOnlyDictionary<string, string> _routinesWaiting = new Dictionary<string, string>();
    /// <summary>UI thread: the user has been told routines wait for sign-in; told again only after they stopped waiting.</summary>
    private bool _routinesSignInNoted;
    /// <summary>UI thread: see <see cref="RoutineScheduler.KeepAwake"/>.</summary>
    private readonly PowerKeepAwake _keepAwake = new();
    /// <summary>UI thread: Windows refused the power request; logged once until the wanted state changes.</summary>
    private bool _keepAwakeRefused;
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
    /// <summary>UI thread: each needs-you job's open step and the token that ends its address watch.</summary>
    private readonly Dictionary<string, (UserStep Step, CancellationTokenSource Watch, NotchCard Card)> _userSteps = new(StringComparer.Ordinal);
    /// <summary>What one address read may take; a step watch reads every <see cref="UserStepWatch.Poll"/>, off the UI thread.</summary>
    private static readonly TimeSpan AddressReadBudget = TimeSpan.FromMilliseconds(300);
    /// <summary>Automatic (address-match) continues: three per job per day, then only "I'm done" ends a step.</summary>
    private readonly AutoContinueBudget _autoContinues = new();
    private const string SteerTooLate = "That job has already finished — ask a follow-up instead.";

    /// <param name="Ask">The ask it will start with once steered (its follow-up or request plus each steer); null: not steered.</param>
    private sealed record QueuedStart(string? FollowUp, string? RoutineId, bool FromTalk, WorkJob Before, string? Ask = null);

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

    /// <summary>A run is in the foreground (<see cref="DesktopLease.InForeground"/>): a native task is open or a run is on the screen.
    /// Esc, the classic window's running state, dictation, walkthroughs, replays and guidance treat OpenClicky as busy only then;
    /// a background job never blocks them, and only its card stops it.</summary>
    private bool RunInForeground => DesktopLease.InForeground(_taskCts is not null,
        Volatile.Read(ref _screen) is { } run ? (_lease.Holder == run.Holder, run.Drive.Phase, run.ReleasedAt) : null, DateTimeOffset.UtcNow);

    /// <summary>UI thread: the classic window shows a run as running (Esc, pausing on focus) only while it is in the foreground.</summary>
    private void RefreshHomeRunning() { if (!_home.IsDisposed) _home.RefreshTaskRunning(RunInForeground); }

    /// <summary>A foreground run is not paused, so it may send input at any moment (talk typing, drawn buttons and detaching hold back).</summary>
    private bool ForegroundMayAct => ForegroundRuns().Any(run => !run.Control.IsPaused) || _taskCts is not null && NativeRun is null;

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
        if (_lease.Holder is not { } holder)
        {
            // Only the run on the screen giving it back starts its grace; talk typing letting go must not restart it.
            if (Volatile.Read(ref _screen) is { ReleasedAt: null } screen) screen.MarkReleased(DateTimeOffset.UtcNow);
            OnUi(RefreshHomeRunning);
            return;
        }
        if (!_runHandles.TryGetValue(holder, out var run)) return;
        run.MarkHolding();
        OnUi(RefreshHomeRunning);
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
            prompt = JobPrompt.Build(companion, _work.ReadNotes(companion.Slug), saved, outputs, followUp, scheduled: routineId is not null,
                howTos: HowToChooser.Choose(_howTos.All, string.Join(' ', saved.Title, saved.Prompt, followUp)));
            // The access mode is snapshotted here, once: a settings change never widens a running job.
            run = new JobRun(saved, string.IsNullOrWhiteSpace(followUp) ? saved.Prompt : followUp.Trim(), routineId,
                _sandboxDemo ? AgentAccessMode.ConfirmEach : _settings.AgentAccess, outputs, fromTalk);
            try { _work.RetireNote(run.Job); } // the agent starts a fresh note.md; the last one stays readable
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("job_note_error=" + ex.GetType().Name); }
            _work.SetJobState(run.Job.Id, WorkJobStates.Running, DateTimeOffset.UtcNow);
            // Try again replays the user's own follow-up, never a queued steer.
            var asked = queued is null ? followUp : queued.FollowUp;
            _lastFollowUps[jobId] = string.IsNullOrWhiteSpace(asked) ? null : asked;
            // Only now is the run on its way: a run the user started by hand answers a step still waiting. Had anything above thrown,
            // the step would still be waiting; if the start is undone later, Restore puts the step back from the job as it was read.
            EndUserStep(jobId, UserStepEnd.Superseded);
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
        if (routineId is not null) UpdateKeepAwake();
        _home.RefreshWork();
        RequestShellPush();
        AnnounceJob(JobEvent.Started, run.Job.Title, run.Job.Id, scheduled: routineId is not null);
        return true;
    }

    /// <summary>UI thread: says a job event as its settings and the user's focus allow (see <see cref="JobAnnouncer"/>). The card always shows.
    /// The title is untrusted and the announcement is never logged. A job that needs the user (E8) calls this with <see cref="JobEvent.NeedsYou"/>.</summary>
    private void AnnounceJob(JobEvent e, string title, string jobId, bool replyDelivered = false, bool scheduled = false)
    {
        try
        {
            var talkBusy = _talk.State != CompanionVoiceState.Idle || _tts.IsSpeaking;
            var said = JobAnnouncer.For(e, title, jobId, _settings.SpeakJobAnnouncements, _settings.CursorJobUpdates, QuietDetector.Current(), talkBusy, replyDelivered, scheduled);
            if (said.Card is { } card) _shell?.AddCard(card);
            else _shell?.RemoveCard(JobAnnouncer.CardId(jobId)); // a new run or a stop: the last run's card is out of date
            if (said.Bubble is { } bubble) _overlay.ShowUpdate(bubble);
            if (said.Speak is { } line) _ = SpeakAnnouncementAsync(line);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_announce_error=" + ex.GetType().Name); }
    }

    private async Task SpeakAnnouncementAsync(string line)
    {
        try { await _tts.AnnounceAsync(line).ConfigureAwait(false); }
        catch (OperationCanceledException) { } // Stop or Esc cut it off
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_announce_speech_error=" + ex.GetType().Name); }
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
        EndUserStep(jobId, UserStepEnd.Superseded); // queued: `before` above still holds the step, so stopping it from the queue puts the step back
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
        if (_queuedStarts.Remove(jobId, out var start)) StartJob(jobId, start.Ask ?? start.FollowUp, start.RoutineId, start.FromTalk, start);
        else ReleaseSlot(jobId); // stopped from its card in the meantime: the slot goes on
    }

    /// <summary>Puts a job that never ran back as it was (state, summary and time).</summary>
    private void Restore(WorkJob job)
    {
        try { _work.RecordJobRun(job.Id, job.Status, job.Summary, job.LastRunAt ?? job.CreatedAt); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_restore_error=" + ex.GetType().Name); }
        if (job.Status == WorkJobStates.NeedsYou) PickUpUserStep(job); // it was waiting on the user when this start superseded the step
        IndexWork();
        _home.RefreshWork();
        RequestShellPush();
    }

    /// <summary>UI thread: schedules the model asked for and the user has not answered yet, oldest first. In memory only: a restart drops
    /// them. <c>JobId</c> is the job whose result asked (null for Talk, which gets a new job when the user presses Schedule).</summary>
    private readonly List<(string Id, RoutineRequest Request, string? JobId)> _routineRequests = [];
    private const int MaxRoutineRequests = 5;

    /// <summary>UI thread: shows the Schedule card for a request. Nothing is scheduled until the user presses Schedule; a newer request
    /// from the same job replaces its older one, and past five the oldest unanswered one goes.</summary>
    private void OfferRoutine(RoutineRequest request, string? jobId)
    {
        if (_shell is null) return;
        if (jobId is not null) DiscardRoutineRequest(_routineRequests.FirstOrDefault(r => r.JobId == jobId).Id);
        while (_routineRequests.Count >= MaxRoutineRequests) DiscardRoutineRequest(_routineRequests[0].Id);
        var id = Guid.NewGuid().ToString("N");
        _routineRequests.Add((id, request, jobId));
        _shell.AddCard(RoutineRequests.ConfirmCard(id, request));
    }

    private void DiscardRoutineRequest(string? requestId)
    {
        if (requestId is null || _routineRequests.RemoveAll(r => r.Id == requestId) == 0) return;
        _shell?.RemoveCard(RoutineRequests.CardId(requestId));
    }

    /// <summary>UI thread, the Schedule card's actions. Schedule makes the routine (for Talk, on a new job under the built-in companion);
    /// Not now drops the request. Returns false: the card goes either way. Request text is untrusted and never logged.</summary>
    private bool RoutineConfirmAction(string requestId, string actionId)
    {
        var index = _routineRequests.FindIndex(r => r.Id == requestId);
        if (index < 0) { _shell?.AddToast("That schedule request is gone."); return false; }
        var (_, request, jobId) = _routineRequests[index];
        _routineRequests.RemoveAt(index);
        if (actionId != RoutineRequests.ScheduleAction) return false;
        string? created = null;
        try
        {
            if (jobId is null) jobId = created = _work.CreateJob(WorkStore.DefaultCompanionSlug, request.Title, request.Task).Id;
            var routine = RoutineRequests.ToRoutine(request, jobId, DateTimeOffset.UtcNow);
            _work.AddRoutine(jobId, routine.Cadence, routine.MinuteOfDay, routine.WeekdayMask, routine.IntervalMinutes, routine.NextRunAt);
            _shell?.AddToast(RoutineRequests.ScheduledToast(request));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("routine_schedule_error=" + ex.GetType().Name);
            if (created is not null) try { _work.DeleteJob(created); } catch (Exception cleanup) when (cleanup is not OutOfMemoryException) { }
            _shell?.AddToast(ex is KeyNotFoundException ? "That job no longer exists, so I couldn't schedule it." : "I couldn't schedule that. Try again.");
        }
        IndexWork();
        _home.RefreshWork();
        RequestShellPush(); // the Settings Routines list
        return false;
    }

    /// <summary>UI thread, at start-up: the routine tick, the wake hold and the keep-awake re-check on a power change.</summary>
    private void StartRoutines()
    {
        _talk.RoutineRequested += request => OnUi(() => OfferRoutine(request, null));
        _routineTimer.Tick += (_, _) => { RunDueRoutines(); RequestShellPush(); };
        _routineTimer.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _home.Disposed += (_, _) => CloseRoutines();
        UpdateKeepAwake();
    }

    /// <summary>Quitting: the static SystemEvents handler goes (it would keep this alive) and the power request is cleared.</summary>
    private void CloseRoutines()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _routineTimer.Dispose();
        _keepAwake.Dispose();
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e) => OnUi(() =>
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) _routineHoldUntil = DateTimeOffset.UtcNow + RoutineScheduler.WakeHold;
        else if (e.Mode == Microsoft.Win32.PowerModes.StatusChange) UpdateKeepAwake();
    });

    /// <summary>UI thread, every <see cref="RoutineScheduler.Tick"/>: stops stale routine runs, re-checks keep-awake and starts the due
    /// routines (through admission, so past five they queue). A routine that cannot start counts as a failed run.</summary>
    private void RunDueRoutines()
    {
        if (_routineTicking) return;
        _routineTicking = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            _routineHoldUntil = RoutineScheduler.HoldAfterGap(_lastRoutineTick, now, _routineHoldUntil);
            _lastRoutineTick = now;
            StopStaleRoutineRuns(now);
            IReadOnlyList<Routine> routines;
            try { routines = _work.Routines(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("routine_read_error=" + ex.GetType().Name); return; }
            UpdateKeepAwake(routines);
            // Routines take the same slots as the user's jobs; a job already running or waiting is left alone.
            // A job waiting on the user's own step is not idle either: a routine must not supersede that step.
            var active = RoutineScheduler.ActiveJobIds(routines, _admission.Contains, _userSteps.Keys);
            var signedIn = _settings.AuthProvider == AuthProvider.ChatGPT && ProviderFactory.IsChatGptSignedIn(_secrets);
            var decision = RoutineScheduler.Decide(routines,
                new(now, System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(), _routineHoldUntil, active, signedIn, _settings.Paused));
            _routinesWaiting = RoutineScheduler.WaitingNotes(decision);
            // Not signed in: the routines wait (no failure counted) and the user hears it once, not once per routine or tick.
            if (decision.WaitingForSignIn.Count > 0 && !_routinesSignInNoted) ClassicMessage(RoutineScheduler.SignInLine + ".");
            _routinesSignInNoted = decision.WaitingForSignIn.Count > 0;
            foreach (var due in decision.Start)
            {
                if (StartJob(due.JobId, null, due.Id)) continue;
                try { _work.SaveRoutine(RoutineSchedule.AfterRun(due, RoutineRunEnd.Failed, "Could not start; see Chat.", DateTimeOffset.UtcNow, TimeZoneInfo.Local)); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Log("routine_save_error=" + ex.GetType().Name); }
            }
        }
        finally { _routineTicking = false; }
    }

    /// <summary>A routine-started run past <see cref="RoutineScheduler.StaleAfter"/> is cancelled; <see cref="FinishJob"/> records it as failed.</summary>
    private void StopStaleRoutineRuns(DateTimeOffset now)
    {
        foreach (var run in _jobRuns.Values)
        {
            if (run.RoutineId is null || run.TimedOut || !RoutineScheduler.IsStale(run.StartedAt, now)) continue;
            Log("routine_run_stale");
            run.TimedOut = true;
            run.Cancel();
            run.Control.Resume();
        }
    }

    /// <summary>UI thread: holds or clears the power request (<see cref="RoutineScheduler.KeepAwake"/>). <paramref name="ending"/>: a run being
    /// recorded as finished, which no longer counts even if it has not left <see cref="_jobRuns"/> yet.</summary>
    private void UpdateKeepAwake(IReadOnlyList<Routine>? routines = null, JobRun? ending = null)
    {
        try { routines ??= _work.Routines(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("routine_read_error=" + ex.GetType().Name); routines = []; }
        var running = _jobRuns.Values.Any(r => r.RoutineId is not null && !ReferenceEquals(r, ending));
        var on = RoutineScheduler.KeepAwake(routines, SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online, running);
        if (on == _keepAwake.IsOn) { if (!on) _keepAwakeRefused = false; return; }
        if (_keepAwake.Set(on)) { _keepAwakeRefused = false; return; }
        if (!_keepAwakeRefused) Log("keep_awake_refused");
        _keepAwakeRefused = true;
    }

    /// <summary>Records a finished run. <paramref name="text"/> is the reply without its result block; <paramref name="result"/> is stored
    /// for the job card (a run without one clears the last run's). The routine, user step and lessons it names are only stored here.
    /// A Talk job takes its result's title. <paramref name="replyDelivered"/>: the run's reply was spoken aloud (nothing does that yet, so it stays false).</summary>
    private void FinishJob(JobRun work, string status, string text, JobResult? result = null, bool replyDelivered = false)
    {
        // Cancelled for running too long (routine runs only): a failed run, whatever ended it.
        if (work.TimedOut && status != AgentProgressStates.Completed) (status, text) = (AgentProgressStates.Failed, RoutineScheduler.StaleOutcome);
        var summary = status == AgentProgressStates.Completed ? result?.Summary ?? JobPrompt.FirstSentence(text) : text;
        // A run that ended by asking for the user's own step waits for them (needs-you) instead of being done.
        var step = status == AgentProgressStates.Completed ? result?.YourTurn : null;
        // The card keeps the last good result when this run produced none (a stop or a failure), with this run's checklist if it had one.
        var stored = JobResult.ForStore(result, JobResult.FromJson(work.Job.ResultJson), work.Live.Plan);
        try
        {
            _work.RecordJobRun(work.Job.Id, step is null ? status : WorkJobStates.NeedsYou, summary, DateTimeOffset.UtcNow);
            _work.SetJobResult(work.Job.Id, stored?.ToJson() ?? "");
            _work.SetUserStep(work.Job.Id, step is null ? null : UserStepWatch.ToJson(step));
            if (work.FromTalk && result?.Title is { } title) _work.RenameJob(work.Job.Id, title);
            SaveHowTos(work.Job.Id, status == AgentProgressStates.Completed ? result?.Learned : null);
            _work.AppendRunLog(work.Job, work.Ask, status, text);
            if (work.RoutineId is { } routineId && _work.Routines(work.Job.Id).FirstOrDefault(r => r.Id == routineId) is { } routine)
                _work.SaveRoutine(RoutineSchedule.AfterRun(routine, RoutineScheduler.RunEnd(status, work.TimedOut), summary[..Math.Min(summary.Length, 200)],
                    DateTimeOffset.UtcNow, TimeZoneInfo.Local));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_record_error=" + ex.GetType().Name); }
        IndexWork();
        var shownTitle = work.FromTalk && result?.Title is { } named ? named : work.Job.Title;
        OnUi(() =>
        {
            if (work.RoutineId is not null) UpdateKeepAwake(ending: work);
            _home.RefreshWork();
            _jobOutputs.Remove(work.Job.Id);
            RequestShellPush();
            // A scheduled run does not ask to be scheduled again; for any other, the user is asked once the result is in.
            if (status == AgentProgressStates.Completed && work.RoutineId is null && result?.Routine is { } asked) OfferRoutine(asked, work.Job.Id);
            if (step is not null) { BeginUserStep(work.Job with { Title = shownTitle }, step); return; }
            AnnounceJob(status == AgentProgressStates.Completed ? JobEvent.Done : status == AgentProgressStates.Failed ? JobEvent.Failed : JobEvent.Stopped,
                shownTitle, work.Job.Id, replyDelivered: work.FromTalk && replyDelivered);
        });
    }

    /// <summary>
    /// Points new companions and jobs at <see cref="AppSettings.AgentFolder"/> (blank: the default) and keeps the how-tos at its root.
    /// Saved jobs and existing companions stay where they are; nothing moves. A new root with no howtos.json starts from a copy of the
    /// previous one (the old file is never moved or changed). An unusable folder falls back to the default with a toast.
    /// </summary>
    private void ApplyAgentFolder()
    {
        var usable = _work.SetRoot(_settings.AgentFolder);
        var path = Path.Combine(_work.AgentRoot, "howtos.json");
        if (!string.Equals(path, _howTosPath, StringComparison.OrdinalIgnoreCase))
        {
            FileCarryOver.CopyIfMissing(_howTosPath.Length > 0 ? _howTosPath : Path.Combine(_work.DefaultAgentRoot, "howtos.json"), path);
            _howTos = new HowToStore(path);
            _howTosPath = path;
        }
        if (usable) return;
        Log("agent_folder_unusable");
        if (_shell is null) _agentFolderNotice = true; else _shell.AddToast(AgentFolderUnusable);
    }

    /// <summary>Keeps what a finished run says it learned (model text: stored, never logged). A failed save never blocks the job.</summary>
    private void SaveHowTos(string jobId, IReadOnlyList<string>? learned)
    {
        if (learned is not { Count: > 0 }) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var howTos = _howTos; // one store for the whole save, even if the agent folder changes meanwhile
            foreach (var lesson in learned) howTos.Add(lesson, jobId, now);
            howTos.Save();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("howto_save_error=" + ex.GetType().Name); }
    }

    /// <summary>Rewrites WORK.md after a job store write; a locked or unwritable file never blocks the job.</summary>
    private void IndexWork()
    {
        try { _work.WriteIndex(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("work_index_error=" + ex.GetType().Name); }
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
        if (_userSteps.ContainsKey(id)) { EndUserStep(id, UserStepEnd.Stopped); return; }
        Log("shell: job.stop ignored, that job is not running");
    }

    /// <summary>UI thread. A running job hears it at its next step; a queued one starts with it as part of its ask.
    /// The text is the user's own (untrusted) and is never logged.</summary>
    void IShellHost.SteerJob(string id, string text)
    {
        if (_jobRuns.TryGetValue(id, out var run)) { _ = SteerFromShellAsync(run, text); return; }
        if (_queuedStarts.TryGetValue(id, out var queued))
        {
            // Added to the request (or follow-up) it will start with, so its card's ask reads request + steer.
            if (JobSteer.Append(queued.Ask ?? queued.FollowUp ?? queued.Before.Prompt, text) is { } ask) _queuedStarts[id] = queued with { Ask = ask };
            else ClassicMessage("That job's request is already as long as it can be.");
            return;
        }
        ClassicMessage(SteerTooLate); // it ended between the last push and Enter
    }

    /// <summary>A steer from the job card; the user sees a line when it reached nothing.</summary>
    private async Task SteerFromShellAsync(JobRun run, string text)
    {
        var taken = await SteerRunAsync(run, text).ConfigureAwait(false);
        if (taken != true) OnUi(() => ClassicMessage(taken == false ? SteerTooLate : "I couldn't pass that on to the job. Try again in a moment."));
    }

    /// <summary>A follow-up is a new run of the job (through admission: it may queue).</summary>
    void IShellHost.FollowUpJob(string id, string text) => StartJob(id, text, routineId: null);

    /// <summary>Try again: a new run asked the same as the last one.</summary>
    void IShellHost.RetryJob(string id) => StartJob(id, _lastFollowUps.GetValueOrDefault(id), routineId: null);

    /// <summary>Superseded: another run of the job started (a follow-up or retry), so the step no longer waits.</summary>
    private enum UserStepEnd { Done, TimedOut, Stopped, Superseded }

    /// <summary>UI thread: a job is waiting for the user's own step (already stored). Its card says what to do, the announcement is made as
    /// for any job that needs the user (not again when <paramref name="resumed"/> after a restart), and the address watch starts, its
    /// six hours counted from when the step was asked. The step text is untrusted and never logged.</summary>
    private void BeginUserStep(WorkJob job, UserStep step, bool resumed = false)
    {
        if (_userSteps.Remove(job.Id, out var previous)) CloseWatch(previous.Watch);
        var watch = new CancellationTokenSource();
        var card = JobCards.YourTurn(job, step);
        _userSteps[job.Id] = (step, watch, card);
        if (!resumed) AnnounceJob(JobEvent.NeedsYou, job.Title, job.Id);
        _shell?.RemoveCard(JobAnnouncer.CardId(job.Id)); // the your-turn card says it better than the plain "Needs you" one
        _shell?.AddCard(card); // at start-up there is no shell yet: ShowUserStepCards adds it once the shell is up
        _ = WatchUserStepAsync(job.Id, new UserStepWatch(step, step.AskedAt, _autoContinues, job.Id), watch.Token);
    }

    /// <summary>Off the UI thread: every <see cref="UserStepWatch.Poll"/>, reads the browser address (only when the step names text to wait
    /// for) and asks the watch. The address is compared and dropped; it is never logged or stored.</summary>
    private async Task WatchUserStepAsync(string jobId, UserStepWatch watch, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(UserStepWatch.Poll);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var address = string.IsNullOrWhiteSpace(watch.Step.DoneWhenUrlContains) ? null
                    : await BrowserAddressReader.ReadAsync(AddressReadBudget, token).ConfigureAwait(false);
                var status = watch.Check(DateTimeOffset.UtcNow, address);
                if (status == UserStepStatus.Waiting) continue;
                OnUi(() => EndUserStep(jobId, status == UserStepStatus.Done ? UserStepEnd.Done : UserStepEnd.TimedOut, token));
                return;
            }
        }
        catch (OperationCanceledException) { } // the step ended another way
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("user_step_watch_error=" + ex.GetType().Name); }
    }

    /// <summary>UI thread: the user's step is over. Done carries the job on with a follow-up run (through admission); a timeout or a stop
    /// ends the job as Stopped. <paramref name="watch"/>: the watch that decided, ignored when the step has since been replaced.</summary>
    private void EndUserStep(string jobId, UserStepEnd end, CancellationToken? watch = null)
    {
        if (!_userSteps.TryGetValue(jobId, out var open) || watch is { } decided && open.Watch.Token != decided) return;
        _userSteps.Remove(jobId);
        CloseWatch(open.Watch);
        _shell?.RemoveCard(JobCards.YourTurnId(jobId));
        try
        {
            _work.SetUserStep(jobId, null);
            if (end is UserStepEnd.TimedOut or UserStepEnd.Stopped)
                _work.RecordJobRun(jobId, WorkJobStates.Stopped, end == UserStepEnd.TimedOut ? UserStepWatch.TimedOutSummary : "Stopped by you.", DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("user_step_end_error=" + ex.GetType().Name); }
        if (end == UserStepEnd.Done && !StartJob(jobId, $"The user finished this step: {open.Step.Step}. Carry on with the job.", routineId: null))
        {
            // It could not start (not signed in, job removed): the job must not stay waiting on a step that is over.
            try
            {
                if (_work.GetJob(jobId).Status == WorkJobStates.NeedsYou)
                    _work.RecordJobRun(jobId, WorkJobStates.Stopped, "Couldn't carry on after your step; try again.", DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("user_step_end_error=" + ex.GetType().Name); }
        }
        IndexWork();
        _home.RefreshWork();
        RequestShellPush();
    }

    /// <summary>The your-turn card's actions. Open page opens only the stored http(s) page, and only on this click. Returns whether the card stays.</summary>
    private bool UserStepCardAction(string jobId, string actionId)
    {
        if (!_userSteps.TryGetValue(jobId, out var open)) return false;
        if (actionId == JobCards.DoneAction) { EndUserStep(jobId, UserStepEnd.Done); return false; }
        if (actionId == JobCards.StopAction) { ((IShellHost)this).StopJob(jobId); return false; } // the card Stop's path: EndUserStep(Stopped)
        if (actionId == JobCards.OpenPageAction && UserStepWatch.PageToOpen(open.Step) is { } page) OpenUrl(page.AbsoluteUri);
        return true;
    }

    /// <summary>UI thread: a needs-you job is waiting on its stored step again (after a restart, or a start that superseded it was undone).
    /// A step with time left is watched, its six hours counted from when it was asked, with no announcement. One already past the limit is
    /// stopped here, with the timeout summary and no card; one that cannot be read is stopped, for nothing could end it.</summary>
    private void PickUpUserStep(WorkJob job)
    {
        var (outcome, step) = UserStepWatch.Resume(job.UserStepJson, DateTimeOffset.UtcNow);
        try
        {
            if (outcome == UserStepResume.Watch) _work.SetUserStep(job.Id, job.UserStepJson);
            else
            {
                _work.SetUserStep(job.Id, null);
                _work.RecordJobRun(job.Id, WorkJobStates.Stopped, outcome == UserStepResume.TimedOut ? UserStepWatch.TimedOutSummary : WorkStore.InterruptedSummary, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("user_step_resume_error=" + ex.GetType().Name); return; }
        if (outcome == UserStepResume.Watch) BeginUserStep(job, step!, resumed: true);
    }

    /// <summary>UI thread, at start-up after <see cref="WorkStore.RecoverInterrupted"/>. Watches restart here, in the classic window too;
    /// their cards wait for the shell (<see cref="ShowUserStepCards"/>).</summary>
    private void ResumeUserSteps()
    {
        try
        {
            foreach (var job in _work.Jobs().Where(j => j.Status == WorkJobStates.NeedsYou)) PickUpUserStep(job);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("user_step_resume_error=" + ex.GetType().Name); }
        IndexWork();
    }

    /// <summary>UI thread, once the shell is up: the your-turn cards of the steps resumed before it existed.</summary>
    private void ShowUserStepCards()
    {
        foreach (var open in _userSteps.Values) _shell?.AddCard(open.Card);
    }

    private static void CloseWatch(CancellationTokenSource watch)
    {
        try { watch.Cancel(); }
        catch (ObjectDisposedException) { }
        watch.Dispose();
    }

    /// <summary>Quitting: no watch outlives the app.</summary>
    private void CloseUserSteps()
    {
        foreach (var open in _userSteps.Values) CloseWatch(open.Watch);
        _userSteps.Clear();
    }

    /// <summary>The steer is kept for the run's desktop observations and goes into its session (held until the session exists).
    /// True: sent or held; false: the run has ended; null: the engine could not be reached. The text is never logged.</summary>
    private async Task<bool?> SteerRunAsync(JobRun run, string text)
    {
        run.Steer(text);
        if (run.TryHoldSteer(text)) return true;
        try { return await run.Server!.SendToRunningAsync(text, run.Cts.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException) { return false; } // stopped or ended meanwhile
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("job_steer_error=" + ex.GetType().Name); return null; }
    }
}
