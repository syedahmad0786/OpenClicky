using OpenClicky.Agent;
using OpenClicky.App.Shell;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Input;

namespace OpenClicky.App;

/// <summary>The on-screen companion: its look and presence follow the cursor settings, Pause and reduced motion.</summary>
internal sealed partial class TrayApplicationContext
{
    /// <summary>UI thread: at startup, after a cursor.* patch, Pause/Resume, the classic settings form, and when a turn starts
    /// (Windows' reduced-motion setting is read fresh each time).</summary>
    private void ApplyCompanionStyle() => _overlay.ApplyStyle(CompanionStyle.From(_settings, GeneralBehaviour.ReducedMotion()));

    /// <summary>UI thread, once at startup: clicking the detached companion talks, and any listening that no held press
    /// started still drives the listening bars.</summary>
    private void WireCompanion()
    {
        _overlay.DetachedClicked += ToggleConversation;
        // An agent task that is running (not paused) injects input: it must not detach the companion or have a click swallowed.
        _overlay.DetachSuppressed = () => _taskCts is not null && !_taskControl.IsPaused;
        _talk.StateChanged += state => OnUi(() => { if (state == CompanionVoiceState.Listening && _micWatch is null) WatchMicLevels(); });
        WatchForStaleDrawing();
    }

    /// <summary>
    /// The drawing is anchored to the window the reply was about: when another window comes to the front, or that one moves or
    /// resizes, the drawing no longer lines up and is cleared. OpenClicky's own windows (the shell, a card) never count.
    /// </summary>
    private void WatchForStaleDrawing()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) =>
        {
            if (!_overlay.HasDrawing || ForegroundWindowInfo.IsCurrentProcessForeground()) return;
            if (ForegroundWindowInfo.IsCurrentProcessWindow(_responseWindowHandle)) return; // the reply's window was ours: no anchor
            if (ForegroundWindowInfo.GetHandle() != _responseWindowHandle || ForegroundWindowInfo.GetBounds() != _responseWindowBounds)
                _talk.ClearDrawing();
        };
        timer.Start();
        _home.Disposed += (_, _) => timer.Dispose();
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
