using OpenClicky.Core;
using OpenClicky.Core.Onboarding;
using OpenClicky.Core.Shell;

namespace OpenClicky.App;

/// <summary>
/// The first-run tour: <see cref="OnboardingFlow"/> holds the position, this wires it to the page's actions and the app's events.
/// Demos start only on their own click and run offline; Skip or Finish stops a running demo. The drive demo reuses the classic
/// sandbox demo (<c>_sandboxDemo</c>), so it always runs Confirm-each with the drive consent and never reads or writes
/// <c>AgentAccess</c>. The tour touches no Windows privacy setting; it only opens the settings page.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private const string ScreenSettingsUri = "ms-settings:privacy-graphicscaptureprogrammatic";

    private readonly OnboardingFlow _onboarding = new();
    /// <summary>The talk client used while <see cref="_demoTalk"/> is set; null means the canned pointing demo client.</summary>
    private ITalkClient? _demoTalkClient;
    private bool _onboardingWired;
    /// <summary>UI thread: the tour demo still running ("draw" or "drive"), even after the tour ended; null when none runs.</summary>
    private string? _tourDemo;
    /// <summary>The draw demo ran in this tour, so its drawing may still be on screen.</summary>
    private bool _tourDrew;
    /// <summary>The test window the tour's practice run opened; closed once the tour leaves that step or ends.</summary>
    private OpenClicky.Sandbox.SandboxForm? _tourSandbox;

    /// <summary>Once the shell is up: the tour starts by itself until it has been skipped or finished once.</summary>
    private void MaybeStartOnboarding()
    {
        if (_settings.FirstRunCompleted || _shell is not { IsActive: true }) return;
        StartTour();
    }

    void IShellHost.OnboardingAction(string action) => OnUi(() =>
    {
        if (action != "start" && !_onboarding.Active) return; // a stale card
        switch (action)
        {
            case "start": if (!_onboarding.Active) StartTour(); break;
            case "next": MoveTour(_onboarding.Next); break;
            case "back": MoveTour(_onboarding.Back); break;
            case "recheck": SettingsProbe.Refresh(); break;
            case "openScreenSettings": OpenUrl(ScreenSettingsUri); break;
            case "drawDemo": if (CanStartTourDemo(OnboardingStep.DrawDemo)) _ = RunTourDrawDemoAsync(); break;
            case "driveDemo": if (CanStartTourDemo(OnboardingStep.DriveDemo)) _ = RunTourDriveDemoAsync(); break;
            case "skip": EndTour(false); break;
            case "finish": EndTour(true); break;
            default: Log("onboarding_action_ignored=" + action); break;
        }
    });

    private void StartTour()
    {
        WireOnboarding();
        _tourDrew = false;
        _onboarding.Start();
        PushTour();
    }

    /// <summary>Skip (openHome false) or Finish: stops a running demo first, then the tour never starts by itself again.</summary>
    private void EndTour(bool openHome)
    {
        if (_tourDemo == "drive") { _talk.CancelCurrentTurn(); CancelTask(); } // the turn too: the task may not have started yet
        else if (_tourDemo == "draw") _talk.CancelCurrentTurn();
        if (_tourDrew) _talk.ClearDrawing();
        StopMicTest();
        _onboarding.End();
        CloseTourSandbox();
        _settings.FirstRunCompleted = true;
        SettingsStore.Save(_settings);
        PushTour();
        if (openHome) _shell?.OpenNotch(NotchTab.Home);
    }

    private void PushTour() => _shell?.SetOnboarding(_onboarding.View);

    private void MoveTour(Func<bool> move)
    {
        var from = _onboarding.Step;
        if (!move()) return;
        if (from == OnboardingStep.MicCheck) StopMicTest();
        if (from == OnboardingStep.DriveDemo) CloseTourSandbox();
        if (_onboarding.Step == OnboardingStep.Microphone) SettingsProbe.Refresh();
        PushTour();
    }

    /// <summary>The tour's own events; subscribed once, whatever the number of tours.</summary>
    private void WireOnboarding()
    {
        if (_onboardingWired) return;
        _onboardingWired = true;
        // A finished model reply, history on or off. MessageReceived("OpenClicky") also carries errors such as "add an OpenRouter key".
        _talk.AssistantResponseChanged += (_, final) =>
        {
            if (final) OnUi(() => { if (_onboarding.MarkDone(OnboardingStep.VoiceHello) || _onboarding.MarkDone(OnboardingStep.CircleToAsk)) PushTour(); });
        };
        _hotkey.TextMode += (_, _) => TourStepDone(OnboardingStep.TextMode);
        _hotkey.DictateEnded += (_, e) => { if (!e.Cancelled) TourStepDone(OnboardingStep.Dictation); };
        _hotkey.HandsFreeToggled += (_, _) => TourStepDone(OnboardingStep.Dictation);
    }

    private void TourStepDone(OnboardingStep step) => OnUi(() => { if (_onboarding.MarkDone(step)) PushTour(); });

    /// <summary>On that step, with no demo, task or talk turn running. A running turn or task is refused out loud; the rest silently.</summary>
    private bool CanStartTourDemo(OnboardingStep step)
    {
        if (_onboarding.Step != step || _onboarding.Busy || _tourDemo is not null) return false;
        if (_talk.State == CompanionVoiceState.Idle && _taskCts is null) return true;
        _shell?.AddToast("Finish the current voice turn or task first.");
        return false;
    }

    /// <summary>Never while a sandbox demo runs; the form may already be gone (closed by the user, or replaced by the classic demo).</summary>
    private void CloseTourSandbox()
    {
        if (_sandboxDemo || _tourSandbox is null) return;
        if (ReferenceEquals(_sandbox, _tourSandbox)) _sandbox = null;
        _tourSandbox.Dispose();
        _tourSandbox = null;
    }

    /// <summary>A real talk turn with a canned reply: the screenshot stays on this PC and no model is called.</summary>
    private async Task RunTourDrawDemoAsync()
    {
        _tourDemo = "draw";
        _tourDrew = true;
        _onboarding.Busy = true;
        PushTour();
        _demoTalkClient = new OnboardingDrawTalkClient();
        _demoTalk = true;
        _talk.ClearDrawing(); // so HasDrawing below means this demo drew (the tour wipes any drawing when it ends anyway)
        try { await _talk.SubmitTextAsync(OnboardingDrawDemo.Request, remember: false, displayAs: OnboardingDrawDemo.Prompt); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("onboarding_draw_error=" + ex.GetType().Name); }
        finally
        {
            _demoTalk = false;
            _demoTalkClient = null;
            _tourDemo = null;
            if (!_onboarding.Active) _talk.ClearDrawing(); // ended while the reply was still drawing
            _onboarding.Busy = false;
            // Only when something was drawn: a failed capture ("Bring the app ... to the front") draws nothing.
            if (_overlay.HasDrawing) _onboarding.MarkDone(OnboardingStep.DrawDemo);
            PushTour();
        }
    }

    /// <summary>The sandbox demo in the test window: consent, then Confirm-each for every step.</summary>
    private async Task RunTourDriveDemoAsync()
    {
        _tourDemo = "drive";
        _onboarding.Busy = true;
        PushTour();
        var ok = false;
        try { ok = await RunSandboxDemoAsync(showClassic: false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("onboarding_drive_error=" + ex.GetType().Name); }
        finally
        {
            _tourDemo = null;
            _tourSandbox = _sandbox;
            if (!_onboarding.Active) CloseTourSandbox(); // ended while the run was going
            _onboarding.Busy = false;
            if (ok) _onboarding.MarkDone(OnboardingStep.DriveDemo);
            PushTour();
        }
    }
}
