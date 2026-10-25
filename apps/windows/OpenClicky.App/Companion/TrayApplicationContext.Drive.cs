using OpenClicky.Agent;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Input;

namespace OpenClicky.App;

/// <summary>
/// Drive: an agent task asks once before its first mouse or keyboard input, shows the Esc pill while it drives, and gives control
/// back on the user's physical input. A window running above OpenClicky's integrity (an admin app) is refused, never worked around.
/// The task's access snapshot is never read from or written to settings here; consent never changes it.
/// Each run has its own drive session; the consent card, the Esc pill and take-back belong to the run on the screen (the desktop
/// lease's holder), and input is only ever sent while the sending run holds the lease.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private const string DriveDeclinedMessage = "You chose not to let me drive.";
    private const string DriveElevatedMessage = "That window runs as administrator, so Windows won't let me control it. Do this step yourself, then say resume task.";
    private const string DriveTakenBackMessage = "You took over, so I didn't do that step. Say resume task when you want me to continue.";
    private const string ClassicDrivingText = "driving · esc stops";

    /// <summary>The drive session of the run on the screen; null when no run has used the screen.</summary>
    private DriveSession? ScreenDrive => ScreenRun?.Drive;
    /// <summary>UI thread: the consent card's pending answer, if the card is up.</summary>
    private TaskCompletionSource<bool>? _driveConsentAnswer;
    /// <summary>1 once the Esc pill is up for this stretch of driving (it comes down when the user takes over).</summary>
    private int _drivingPillShown;

    /// <summary>The run ended: the first end reason sticks (a decline stays a decline). If it was the run on the screen, both cards
    /// come down; another run's cards are left alone.</summary>
    private void EndDrive(DriveSession drive, DriveEnd reason, bool wasScreen)
    {
        drive.End(reason);
        Log("drive_end=" + drive.EndedBy);
        if (!wasScreen) return;
        _drivingPillShown = 0;
        OnUi(() =>
        {
            _driveConsentAnswer?.TrySetResult(false);
            _driveConsentAnswer = null;
            _shell?.RemoveCard(CompanionCards.DriveConsentId);
            _shell?.RemoveCard(CompanionCards.DrivingId);
        });
    }

    private static bool IsDriveInput(string tool) =>
        tool is AgentToolNames.Click or AgentToolNames.TypeText or AgentToolNames.Hotkey or AgentToolNames.Scroll;

    /// <summary>Every agent action of one run (native and OpenWork). Mouse and keyboard input goes through that run's drive gate first.</summary>
    private Func<ActionRequest, bool, CancellationToken, Task<ActionResult>> ExecuteFor(RunHandle run) => (request, confirmed, ct) =>
        IsDriveInput(request.ToolName) ? ExecuteDriveInputAsync(run, request, confirmed, ct) : ExecuteLegacyAsync(request, confirmed, ct);

    /// <summary>
    /// The consent prompt (the session asks at most once, and cancels this after 60 s). Shell: the consent card, answered only by a
    /// real click on it (<see cref="AnswerDriveConsent"/>). Classic: the confirm dialog with the same wording.
    /// </summary>
    private async Task<bool> AskDriveConsentAsync(CancellationToken ct)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shellShown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            if (_shell is not { IsActive: true } shell) { shellShown.TrySetResult(false); return; }
            _driveConsentAnswer?.TrySetResult(false);
            _driveConsentAnswer = answer;
            shell.AddCard(CompanionCards.DriveConsent());
            shellShown.TrySetResult(true);
        });
        if (!await shellShown.Task.WaitAsync(ct).ConfigureAwait(false))
        {
            var card = CompanionCards.DriveConsent();
            return await _confirm.ConfirmDriveAsync(card.Title, card.Body!, ct).ConfigureAwait(false);
        }
        try { return await answer.Task.WaitAsync(ct).ConfigureAwait(false); }
        finally
        {
            OnUi(() =>
            {
                if (!ReferenceEquals(_driveConsentAnswer, answer)) return;
                _driveConsentAnswer = null;
                _shell?.RemoveCard(CompanionCards.DriveConsentId);
            });
        }
    }

    /// <summary>UI thread, from <see cref="Shell.IShellHost"/>'s card action: the user's own click on the consent card.</summary>
    private void AnswerDriveConsent(bool yes)
    {
        var answer = _driveConsentAnswer;
        _driveConsentAnswer = null;
        answer?.TrySetResult(yes);
    }

    /// <summary>
    /// The gate before a click, type_text, hotkey or scroll: consent, then the integrity check, then the Esc pill; the agent's own
    /// cursor moves during the action are not the user's.
    /// </summary>
    private async Task<ActionResult> ExecuteDriveInputAsync(RunHandle run, ActionRequest request, bool confirmed, CancellationToken ct)
    {
        // The hard guard: no run sends input unless it holds the screen (a job holds it for the whole tool call, a native task always).
        if (_lease.Holder != run.Holder) return new(false, "Another task is using the screen, so no input was sent.", false, ObserveAgain: true);
        var drive = run.Drive;
        if (!await drive.EnsureConsentAsync(ct).ConfigureAwait(false))
        {
            run.Stop(); // a "no" ends the run that asked (Auto/Full on OpenWork would otherwise get this answer on every call)
            return new(false, DriveDeclinedMessage, false);
        }
        // Taken back after the step boundary was passed: nothing is sent once the user has the mouse. Paused; look again after.
        if (drive.Phase != DrivePhase.Driving) return new(false, DriveTakenBackMessage, false, ObserveAgain: true);
        if (RefusesIntegrity(request))
        {
            Log("drive_refused integrity");
            drive.Suspend(); // not driving while held: the pill comes down and the companion may detach again
            PauseRun(run);
            OnUi(HideDrivingPill);
            return new(false, DriveElevatedMessage, false, ObserveAgain: true);
        }
        if (Interlocked.Exchange(ref _drivingPillShown, 1) == 0) OnUi(ShowDrivingCard);
        drive.BeginAgentAction();
        try { return await ExecuteLegacyAsync(request, confirmed, ct).ConfigureAwait(false); }
        finally
        {
            var p = System.Windows.Forms.Cursor.Position;
            drive.NoteAgentCursor(p.X, p.Y);
            OnUi(ShowClassicDrivingBubble); // after the action: a click's pointer label is never cut short
        }
    }

    /// <summary>
    /// The foreground window, and for a click or scroll the top-level window where it lands, must not run above OpenClicky's
    /// integrity (or be unreadable). Clicks carry their target; a scroll lands under the cursor.
    /// </summary>
    private static bool RefusesIntegrity(ActionRequest request)
    {
        var own = ProcessIntegrity.Own();
        if (DriveGuard.Refuses(ProcessIntegrity.Of(ForegroundWindowInfo.GetHandle()), own)) return true;
        if (request.ToolName == AgentToolNames.Click)
            return !int.TryParse(request.Arguments.GetValueOrDefault("x"), out var x) || !int.TryParse(request.Arguments.GetValueOrDefault("y"), out var y)
                || DriveGuard.Refuses(ProcessIntegrity.AtPoint(x, y), own);
        if (request.ToolName == AgentToolNames.Scroll)
        {
            var p = System.Windows.Forms.Cursor.Position;
            return DriveGuard.Refuses(ProcessIntegrity.AtPoint(p.X, p.Y), own);
        }
        return false;
    }

    /// <summary>UI thread: the shell's Esc pill, once per stretch of driving.</summary>
    private void ShowDrivingCard()
    {
        if (ScreenDrive is { Phase: DrivePhase.Driving } && _shell is { IsActive: true } shell) shell.AddCard(CompanionCards.Driving());
    }

    /// <summary>UI thread: classic mode has no pill; the bubble says it instead, again after each action (each capture hides it).</summary>
    private void ShowClassicDrivingBubble()
    {
        if (ScreenDrive is { Phase: DrivePhase.Driving } && _shell is not { IsActive: true }) _overlay.ShowResponding(ClassicDrivingText);
    }

    /// <summary>UI thread: the user took over, or the window refused input. The paused progress line takes the pill's place.</summary>
    private void HideDrivingPill()
    {
        _drivingPillShown = 0;
        _shell?.RemoveCard(CompanionCards.DrivingId);
    }

    /// <summary>
    /// UI thread, physical input only (raw input never sees injected events). True when the drive session handled it: ignored while
    /// consent is pending; while driving, a key, a click or a 40 px move takes control back. On OpenClicky's own windows (the cards)
    /// or while a confirm dialog is up the pointer only moves the baseline, so answering OpenClicky never takes back the task.
    /// </summary>
    private bool HandleDriveInput(PhysicalUserActivity activity)
    {
        if (ScreenDrive is not { } drive) return false; // the run on the screen's
        var phase = drive.Phase;
        if (phase == DrivePhase.AwaitingConsent) return true;
        if (phase != DrivePhase.Driving) return false;
        var p = System.Windows.Forms.Cursor.Position;
        var kind = activity.Category == PhysicalUserActivityCategory.Keyboard ? PhysicalKind.Key
            : activity.MouseButtonDown || activity.MayChangeScreen ? PhysicalKind.MouseButton : PhysicalKind.MouseMove;
        if (_confirm.IsDialogOpen || (kind != PhysicalKind.Key && ForegroundWindowInfo.IsCurrentProcessWindowAt(p.X, p.Y)))
        {
            drive.Rebase(p.X, p.Y);
            return true;
        }
        if (!drive.OnPhysicalInput(kind, p.X, p.Y)) return true;
        PauseTask(); // first: the orchestrator's next beforeStep must already see the pause
        Log("drive_taken_back kind=" + kind);
        HideDrivingPill();
        return true;
    }

    /// <summary>Before a resume of <paramref name="run"/> is attempted: driving again, watched from the cursor's position now.</summary>
    private static void ResumeDrive(RunHandle run)
    {
        var p = System.Windows.Forms.Cursor.Position;
        run.Drive.Resume(p.X, p.Y);
    }
}
