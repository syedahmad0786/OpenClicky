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
    private readonly object _highlightGate = new();
    private readonly System.Diagnostics.Stopwatch _highlightClock = new();
    /// <summary>The hold being recorded; null when none is.</summary>
    private StrokeSegmenter? _highlightStrokes;
    /// <summary>The last hold's gesture, its screen text still being read; taken (once) by the turn that hold started.</summary>
    private Task<HighlightGesture?>? _pendingHighlight;

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

    /// <summary>
    /// UI thread, in the constructor before Talk subscribes to the shortcut: our release handler then runs first, so the
    /// gesture is stored before the turn the release starts looks for it.
    /// </summary>
    private void WireHighlight()
    {
        _talk.HeldTalkStarted += StartHighlight;
        _hotkey.Released += (_, e) => EndHighlight(keep: e is not ShortcutEndedEventArgs { Cancelled: true });
    }

    /// <summary>UI thread: a visible talk hold began. Records the pointer every 16 ms while Screen highlight is on and the companion is not off.</summary>
    private void StartHighlight()
    {
        if (!_settings.ScreenHighlight || CompanionStyle.From(_settings, reducedMotion: false).Presence == CursorPresence.Off) return;
        lock (_highlightGate)
        {
            _pendingHighlight = null;
            _highlightStrokes = new StrokeSegmenter();
            _highlightClock.Restart();
        }
        _overlay.ClearHighlight(); // the last hold's fading trail must not join this one
        var timer = new System.Windows.Forms.Timer { Interval = 16 };
        timer.Tick += (_, _) => { if (!SampleHighlight()) timer.Dispose(); };
        if (SampleHighlight()) timer.Start();
        else timer.Dispose();
    }

    /// <summary>UI thread: one GetCursorPos sample; false once the hold is over (a listening turn that ended without a release drops it).</summary>
    private bool SampleHighlight()
    {
        if (_talk.State != CompanionVoiceState.Listening) { EndHighlight(keep: false); return false; }
        var p = System.Windows.Forms.Cursor.Position;
        lock (_highlightGate)
        {
            if (_highlightStrokes is not { } strokes) return false;
            strokes.Add(new Pt(p.X, p.Y), _highlightClock.ElapsedMilliseconds);
        }
        _overlay.AddHighlightPoint(p.X, p.Y);
        return true;
    }

    /// <summary>
    /// The hold ended: a cancelled one leaves nothing. Otherwise the strokes are classified (too-short ones dropped) and the
    /// text under them is read off the UI thread within 400 ms in all; a hold that never moved stores nothing.
    /// </summary>
    private void EndHighlight(bool keep)
    {
        StrokeSegmenter? strokes;
        lock (_highlightGate) (strokes, _highlightStrokes) = (_highlightStrokes, null);
        if (strokes is null) return;
        if (!keep) { _overlay.ClearHighlight(); return; }
        var ended = DateTimeOffset.UtcNow;
        var classified = strokes.Finish()
            .Select(points => (Kind: StrokeClassifier.Classify(points), Points: points))
            .Where(s => s.Kind is not null).Select(s => (Kind: s.Kind!.Value, s.Points)).ToList();
        var dwells = strokes.Dwells.ToArray();
        if (classified.Count == 0 && dwells.Length == 0) return;
        var exclusions = AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra);
        var pending = Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var built = classified.Select(s => new HighlightStroke(s.Kind, s.Points, StrokeClassifier.BoundsOf(s.Points),
                UiaTextHarvester.Harvest(StrokeClassifier.SamplePoints(s.Kind, s.Points), Environment.ProcessId,
                    HighlightHarvestBudget - clock.Elapsed, exclusions))).ToList();
            return (HighlightGesture?)new HighlightGesture(built, dwells, ended);
        });
        lock (_highlightGate) _pendingHighlight = pending;
    }

    /// <summary>Talk's turn, any thread (not the UI thread): the last hold's gesture, once. Waits at most a second for its text.</summary>
    private HighlightGesture? TakeHighlight()
    {
        Task<HighlightGesture?>? pending;
        lock (_highlightGate) (pending, _pendingHighlight) = (_pendingHighlight, null);
        try { return pending is not null && pending.Wait(TimeSpan.FromSeconds(1)) ? pending.Result : null; }
        catch (AggregateException ex) { Log("highlight_error=" + ex.InnerException?.GetType().Name); return null; }
    }

    /// <summary>Esc: the trail comes off the screen and the gesture is dropped.</summary>
    private void ClearHighlight()
    {
        lock (_highlightGate) (_highlightStrokes, _pendingHighlight) = (null, null);
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
