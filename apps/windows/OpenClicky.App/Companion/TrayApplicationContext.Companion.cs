using OpenClicky.Agent;
using OpenClicky.App.Shell;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Input;

namespace OpenClicky.App;

/// <summary>The on-screen companion: its look and presence follow the cursor settings, Pause and reduced motion.</summary>
internal sealed partial class TrayApplicationContext
{
    private readonly OpenClicky.Core.Drawing.DrawingAnchor _drawingAnchor = new();

    /// <summary>UI thread: at startup, after a cursor.* patch, Pause/Resume, the classic settings form, and when a turn starts
    /// (Windows' reduced-motion setting is read fresh each time).</summary>
    private void ApplyCompanionStyle() => _overlay.ApplyStyle(CompanionStyle.From(_settings, GeneralBehaviour.ReducedMotion()));

    /// <summary>UI thread, once at startup: clicking the detached companion talks, and any listening that no held press
    /// started still drives the listening bars.</summary>
    private void WireCompanion()
    {
        _overlay.DetachedClicked += ToggleConversation;
        _overlay.ButtonClicked += OnDrawingButtonClicked;
        // An agent task that is running (not paused) injects input: it must not detach the companion or have a click swallowed.
        _overlay.DetachSuppressed = () => _taskCts is not null && !_taskControl.IsPaused;
        // The invisible topmost click targets over drawn buttons come down while a confirm dialog is open, so they cannot take a click meant for Yes/No.
        _overlay.DrawingButtonsSuppressed = () => _confirm.IsDialogOpen;
        _talk.StateChanged += state => OnUi(() => { if (state == CompanionVoiceState.Listening && _micWatch is null) WatchMicLevels(); });
        WatchForStaleDrawing();
    }

    /// <summary>
    /// UI thread: a button the model drew was clicked. Its prompt is model-authored, untrusted text: it runs as an ordinary talk
    /// turn (quoted as the user's request, the label shown as their message), so any [OPEN]/[TYPE] it provokes still follows
    /// AgentAccessPolicy. No agent task is running then: the overlay takes the click targets down while one does. Ignored while
    /// dictating (the wrapped prompt would be typed into the user's field) or while a confirm dialog is up; otherwise it gets the
    /// same pre-steps as any submit (<see cref="PrepareSubmission"/>).
    /// </summary>
    private async void OnDrawingButtonClicked(string label, string prompt)
    {
        if (_dictationActive || _confirm.IsDialogOpen || (_taskCts is not null && !_taskControl.IsPaused)) return;
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
