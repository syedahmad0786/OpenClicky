using OpenClicky.Agent;
using OpenClicky.App.Shell;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;
using OpenClicky.Input;

namespace OpenClicky.App;

/// <summary>The on-screen companion: its look and presence follow the cursor settings, Pause and reduced motion.</summary>
internal sealed partial class TrayApplicationContext
{
    private readonly OpenClicky.Core.Drawing.DrawingAnchor _drawingAnchor = new();

    // The screen highlight: what the user draws with the pointer while holding talk, for that turn's screenshot.
    private static readonly TimeSpan HighlightHarvestBudget = TimeSpan.FromMilliseconds(400);
    private readonly HighlightRecorder _highlight = new();
    private readonly System.Diagnostics.Stopwatch _highlightClock = new();
    /// <summary>
    /// Set (UI thread) by raw input when a physical mouse event arrives; each highlight tick takes it. Injected input (an agent
    /// action) never sets it, so injected pointer moves never draw.
    /// </summary>
    private bool _physicalMouseSinceTick;

    /// <summary>UI thread: at startup, after a cursor.* patch, Pause/Resume, the classic settings form, and when a turn starts
    /// (Windows' reduced-motion setting is read fresh each time).</summary>
    private void ApplyCompanionStyle()
    {
        var style = CompanionStyle.From(_settings, GeneralBehaviour.ReducedMotion());
        _overlay.ApplyStyle(style);
        if (!style.ScreenHighlight || style.Presence == CursorPresence.Off) ClearHighlight(); // turned off: nothing drawn is kept or sent
    }

    /// <summary>UI thread, once at startup: clicking the detached companion talks, and any listening that no held press
    /// started still drives the listening bars.</summary>
    private void WireCompanion()
    {
        _overlay.DetachedClicked += ToggleConversation;
        _overlay.ButtonClicked += OnDrawingButtonClicked;
        // A foreground run that is not paused may inject input: it must not detach the companion or have a click swallowed.
        _overlay.DetachSuppressed = () => ForegroundMayAct;
        // The invisible topmost click targets over drawn buttons come down while a confirm dialog is open, so they cannot take a click meant for Yes/No.
        _overlay.DrawingButtonsSuppressed = () => _confirm.IsDialogOpen;
        _talk.StateChanged += state => OnUi(() => { if (state == CompanionVoiceState.Listening && _micWatch is null) WatchMicLevels(); });
        WatchForStaleDrawing();
        WireWalkthroughWatch();
    }

    // Walkthrough targets: the user's own click on the pointed control (or a 2 s hover) moves the guide on.
    private const int AdvanceDelayMs = 400;
    private readonly WalkthroughTargetWatch _walkthroughWatch = new();
    /// <summary>The app being guided (0 until the first check): the foreground moving to another one ends the walkthrough.</summary>
    private uint _guidedProcessId;
    /// <summary>The step the shell's walkthrough card was last added for; -1 while no walkthrough card is up.</summary>
    private int _walkthroughCardStep = -1;

    /// <summary>UI thread, once: arms the watch from each step that pointed, and feeds it physical clicks and a 100 ms tick.</summary>
    private void WireWalkthroughWatch()
    {
        _talk.WalkthroughTargeted += target => OnUi(() =>
        {
            if (_talk.Walkthrough.View is { Active: true, Busy: false } && !RunInForeground)
                _walkthroughWatch.Arm(target, Environment.TickCount64);
        });
        // Raw input: injected input (an agent's or any other app's) never arrives here, so only the user's own clicks count.
        _physicalInput.UserActivity += activity => { if (activity.MouseButtonDown) OnWalkthroughClick(); };
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => WalkthroughWatchTick();
        timer.Start();
        _home.Disposed += (_, _) => timer.Dispose();
    }

    /// <summary>UI thread: the walkthrough changed. It ended: the watch forgets everything. A step is loading: no target until it arrives.</summary>
    private void UpdateWalkthroughWatch(WalkthroughView view)
    {
        if (!view.Active)
        {
            _walkthroughWatch.Reset();
            _guidedProcessId = 0;
            return;
        }
        _walkthroughWatch.Activate(Environment.TickCount64);
        if (view.Busy) _walkthroughWatch.Disarm();
    }

    /// <summary>
    /// UI thread, a physical button press. A press on any OpenClicky window (a card, the shell, a drawn button, the overlay's
    /// catchers) or while a confirm dialog is up never ends or advances the guide; nor does anything while an agent task runs.
    /// </summary>
    private void OnWalkthroughClick()
    {
        if (_walkthroughWatch.Target is null || RunInForeground || !_talk.Walkthrough.View.Active) return;
        var p = System.Windows.Forms.Cursor.Position;
        var own = _confirm.IsDialogOpen || ForegroundWindowInfo.IsCurrentProcessWindowAt(p.X, p.Y);
        OnWalkthroughEvent(_walkthroughWatch.OnClick(new Pt(p.X, p.Y), own, Environment.TickCount64));
    }

    /// <summary>UI thread, every 100 ms while a walkthrough runs: hover, an app switch, expiry and the idle timeout.</summary>
    private void WalkthroughWatchTick()
    {
        if (!_talk.Walkthrough.View.Active) return;
        var now = Environment.TickCount64;
        // While a task runs nothing is armed, and its time does not count as the user's idleness: the clock starts over after it.
        if (RunInForeground) { _walkthroughWatch.Disarm(); _walkthroughWatch.RestartIdle(now); return; }
        if (_walkthroughWatch.Target is { IsHover: true })
        {
            var p = System.Windows.Forms.Cursor.Position;
            OnWalkthroughEvent(_walkthroughWatch.OnPointer(new Pt(p.X, p.Y), now));
        }
        if (SwitchedAwayFromGuidedApp(now)) { StopWalkthrough(WalkthroughTargetWatch.SwitchedAppsReason); return; }
        OnWalkthroughEvent(_walkthroughWatch.Tick(now));
    }

    /// <summary>
    /// True when another app (not OpenClicky) is in front and no advance happened in the last 3 s. Right after an advance the
    /// click itself may have opened that app: it becomes the guided app.
    /// </summary>
    private bool SwitchedAwayFromGuidedApp(long now)
    {
        var foreground = ForegroundWindowInfo.GetHandle();
        if (foreground == 0 || ForegroundWindowInfo.IsCurrentProcessWindow(foreground)) return false;
        var pid = ForegroundWindowInfo.ProcessIdOf(foreground);
        if (pid == 0) return false;
        if (_guidedProcessId == 0 || pid == _guidedProcessId || _walkthroughWatch.InSwitchGrace(now))
        {
            _guidedProcessId = pid;
            return false;
        }
        return true;
    }

    private void OnWalkthroughEvent(TargetEvent e)
    {
        switch (e)
        {
            case TargetEvent.Advance: _ = AdvanceWalkthroughAsync(); break;
            case TargetEvent.LeftGuide: StopWalkthrough(WalkthroughTargetWatch.ClickedElsewhereReason); break;
            case TargetEvent.GuideTimedOut: StopWalkthrough(WalkthroughTargetWatch.TimedOutReason); break;
            case TargetEvent.Expired:
                _talk.SetWalkthroughStatus(WalkthroughTargetWatch.StillThereStatus);
                if (_shell is { IsActive: true } shell) shell.AddToast(WalkthroughTargetWatch.StillThereStatus); // the card shows the instruction
                break;
        }
    }

    /// <summary>The click landed on the target: give the app 400 ms to react, then look at the result for the next step.</summary>
    private async Task AdvanceWalkthroughAsync()
    {
        await Task.Delay(AdvanceDelayMs);
        if (_talk.Walkthrough.View is { Active: true, Busy: false } && !RunInForeground) await MoveWalkthroughAsync(WalkthroughMove.Next);
    }

    /// <summary>
    /// UI thread: in shell mode the step card stands in for the classic walkthrough window. It is added (to the front) when the
    /// walkthrough starts and when a new step arrives; any other change updates it in place, so a card the user dismissed
    /// stays dismissed until the next step.
    /// </summary>
    private bool ShowWalkthroughCard(WalkthroughView view)
    {
        if (_shell is not { IsActive: true } shell) return false;
        _walkthroughForm.Hide();
        if (!view.Active)
        {
            shell.RemoveCard(CompanionCards.WalkthroughId);
            _walkthroughCardStep = -1;
        }
        else if (WalkthroughCardPolicy.AddsCard(_walkthroughCardStep, view))
        {
            shell.AddCard(CompanionCards.Walkthrough(view));
            _walkthroughCardStep = view.Step;
        }
        else shell.UpdateCard(CompanionCards.Walkthrough(view));
        return true;
    }

    /// <summary>
    /// The frame of the control under a POINT, read with UI Automation within 300 ms (off the UI thread: Talk's and the agent's
    /// turns call it on the thread pool). Only its bounds are used, never its name or value.
    /// </summary>
    private Box? SnapControl(int x, int y) =>
        UiaControlSnapper.Snap(x, y, UiaControlSnapper.Budget, AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra));

    /// <summary>
    /// UI thread: a button the model drew was clicked. Its prompt is model-authored, untrusted text: it runs as an ordinary talk
    /// turn (quoted as the user's request, the label shown as their message), so any [OPEN]/[TYPE] it provokes still follows
    /// AgentAccessPolicy. No agent task is running then: the overlay takes the click targets down while one does. Ignored while
    /// dictating (the wrapped prompt would be typed into the user's field) or while a confirm dialog is up; otherwise it gets the
    /// same pre-steps as any submit (<see cref="PrepareSubmission"/>).
    /// </summary>
    private async void OnDrawingButtonClicked(string label, string prompt)
    {
        if (_dictationActive || _confirm.IsDialogOpen || ForegroundMayAct) return;
        PrepareSubmission();
        try { await _talk.SubmitTextAsync(OpenClicky.Core.Drawing.DrawingButtons.Request(label, prompt), displayAs: label); }
        catch (Exception ex) { Log($"drawing_button_failed={ex.GetType().Name}"); }
    }

    /// <summary>
    /// The drawing is anchored to the last non-OpenClicky window in front when the turn captured the screen (or when the drawing
    /// appeared): when a different such window comes to the front, or the anchor moves or resizes, the drawing no longer lines
    /// up and is cleared. OpenClicky's own windows (the shell, a card) never become the anchor or count as a switch.
    /// </summary>
    private void WatchForStaleDrawing()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) =>
        {
            if (_drawingAnchor.IsStale(ForegroundWindowInfo.GetHandle(), ForegroundWindowInfo.GetBounds(),
                    ForegroundWindowInfo.IsCurrentProcessForeground(), _overlay.HasDrawing))
                _talk.ClearDrawing();
        };
        timer.Start();
        _home.Disposed += (_, _) => timer.Dispose();
    }

    /// <summary>UI thread, when a turn that needs the screen starts processing.</summary>
    private void CaptureDrawingAnchor() =>
        _drawingAnchor.Capture(ForegroundWindowInfo.GetHandle(), ForegroundWindowInfo.GetBounds(), ForegroundWindowInfo.IsCurrentProcessForeground());

    /// <summary>
    /// UI thread, in the constructor before Talk subscribes to the shortcut: our release handler then runs first, so the
    /// gesture is stored before the turn the release starts takes it.
    /// </summary>
    private void WireHighlight()
    {
        _talk.HeldTalkStarted += StartHighlight;
        _hotkey.Released += (_, e) => EndHighlight(keep: e is not ShortcutEndedEventArgs { Cancelled: true });
        _physicalInput.UserActivity += activity => { if (activity.Category == PhysicalUserActivityCategory.Mouse) _physicalMouseSinceTick = true; };
    }

    /// <summary>UI thread: a visible talk hold began. Records the pointer every 16 ms while Screen highlight is on and the companion is not off.</summary>
    private void StartHighlight()
    {
        if (!_settings.ScreenHighlight || CompanionStyle.From(_settings, reducedMotion: false).Presence == CursorPresence.Off) return;
        var p = System.Windows.Forms.Cursor.Position;
        _highlightClock.Restart();
        _physicalMouseSinceTick = false;
        _highlight.Start(new Pt(p.X, p.Y), 0);
        _overlay.ClearHighlight(); // the last hold's fading trail must not join this one
        _overlay.AddHighlightPoint(p.X, p.Y);
        var timer = new System.Windows.Forms.Timer { Interval = 16 };
        timer.Tick += (_, _) => { if (!SampleHighlight()) timer.Dispose(); };
        timer.Start();
    }

    /// <summary>
    /// UI thread: one GetCursorPos sample, kept only when physical mouse input arrived since the last tick (otherwise the
    /// pointer counts as unmoved). False once the hold is over; a listening turn that ended without a release drops it.
    /// </summary>
    private bool SampleHighlight()
    {
        if (_talk.State != CompanionVoiceState.Listening)
        {
            if (_highlight.Recording) EndHighlight(keep: false);
            return false;
        }
        Pt? physical = null;
        if (_physicalMouseSinceTick)
        {
            var p = System.Windows.Forms.Cursor.Position;
            physical = new Pt(p.X, p.Y);
        }
        _physicalMouseSinceTick = false;
        if (!_highlight.Sample(physical, _highlightClock.ElapsedMilliseconds)) return false;
        if (physical is { } moved) _overlay.AddHighlightPoint((int)moved.X, (int)moved.Y);
        return true;
    }

    /// <summary>
    /// UI thread: the hold ended. A cancelled one clears the trail and leaves nothing; otherwise the text under the strokes is
    /// read off the UI thread within 400 ms in all.
    /// </summary>
    private void EndHighlight(bool keep)
    {
        var exclusions = AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra);
        if (_highlight.End(keep, DateTimeOffset.UtcNow, HighlightHarvestBudget,
                (samples, budget) => UiaTextHarvester.Harvest(samples, Environment.ProcessId, budget, exclusions)) && !keep)
            _overlay.ClearHighlight();
    }

    /// <summary>Talk's turn, on a thread-pool thread: the last hold's gesture, once. Waits at most a second for its text.</summary>
    private HighlightGesture? TakeHighlight() => _highlight.Take(TimeSpan.FromSeconds(1));

    /// <summary>Esc, or Screen highlight turned off: the trail comes off the screen and the recording and gesture are dropped.</summary>
    private void ClearHighlight()
    {
        _highlight.Clear();
        _overlay.ClearHighlight();
    }

    /// <summary>The surface covers the monitor it follows; the companion docks at that monitor's top centre.</summary>
    private void UpdateNotchPoint()
    {
        if (_shellSurface is not { Window.IsDisposed: false } surface) return;
        var bounds = surface.Window.Bounds;
        _overlay.SetNotchPoint(bounds.Left + bounds.Width / 2, bounds.Top + 4);
    }

    /// <summary>The round classic dock is only for the classic fallback: the notch is the dock while the shell is up.</summary>
    private void UpdateCompanionDock()
    {
        if (_shell is { IsActive: true }) _companionDock.Hide();
        else _companionDock.Show();
    }

    /// <summary>Feeds the companion's listening bars until listening ends (a held press has its own watch, which also checks for a silent mic).</summary>
    private void WatchMicLevels()
    {
        var timer = _micWatch = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            if (_talk.State == CompanionVoiceState.Listening)
            {
                if (RecordingMic() is { } mic) _overlay.ShowMicLevel(mic.PeakLevel);
                return;
            }
            timer.Dispose();
            if (ReferenceEquals(_micWatch, timer)) _micWatch = null;
        };
        timer.Start();
    }
}
