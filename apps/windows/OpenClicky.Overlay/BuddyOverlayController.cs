using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Overlay;

internal static class ResponseBubblePlacement
{
    private const float Gap = 12f;

    public static RectangleF Place(
        RectangleF desired,
        RectangleF workingArea,
        IReadOnlyList<RectangleF> annotationBounds)
    {
        var clamped = Clamp(desired, workingArea);
        var obstacles = annotationBounds
            .Where(bounds => bounds.Width > 0 && bounds.Height > 0 && bounds.IntersectsWith(workingArea))
            .ToArray();
        if (obstacles.Length == 0 || obstacles.All(bounds => !bounds.IntersectsWith(clamped)))
        {
            return clamped;
        }

        var union = obstacles[0];
        foreach (var bounds in obstacles.Skip(1))
        {
            union = RectangleF.Union(union, bounds);
        }

        var candidates = new[]
        {
            new RectangleF(clamped.X, union.Bottom + Gap, clamped.Width, clamped.Height),
            new RectangleF(clamped.X, union.Top - Gap - clamped.Height, clamped.Width, clamped.Height),
            new RectangleF(union.Right + Gap, clamped.Y, clamped.Width, clamped.Height),
            new RectangleF(union.Left - Gap - clamped.Width, clamped.Y, clamped.Width, clamped.Height),
        };
        foreach (var candidate in candidates)
        {
            if (Contains(workingArea, candidate)
                && obstacles.All(bounds => !bounds.IntersectsWith(candidate)))
            {
                return candidate;
            }
        }

        return clamped;
    }

    private static RectangleF Clamp(RectangleF rect, RectangleF bounds)
    {
        rect.X = Math.Clamp(rect.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - rect.Width));
        rect.Y = Math.Clamp(rect.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - rect.Height));
        return rect;
    }

    private static bool Contains(RectangleF outer, RectangleF inner) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top
        && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}

/// <summary>
/// The companion overlay: a small per-pixel-alpha cursor window that follows the pointer on a spring (the glowing accent
/// triangle with its listening bars, thinking dots and bubbles), a virtual-screen drawing window for marks, the ink window,
/// and one WPF window per 3D figure. All are topmost, click-through and never activate. Public members may be called from
/// any thread.
/// </summary>
public sealed class BuddyOverlayController : IOverlayController
{
    private const double WordSeconds = 0.06, HoldSeconds = 4, FadeSeconds = 0.4, ReducedFadeSeconds = 0.12, MarkSeconds = 0.35;
    private const float BarEase = 0.35f, ListeningBubbleShiftDip = 16;
    private const int MaxLabelChars = 120;
    private const float CatcherDip = 36;
    private const int ButtonPollMilliseconds = 100;
    /// <summary>At most this many figures are on screen (the newest): each is a software-rendered WPF window.</summary>
    // ponytail: older figures stay in the document (described, editable) but unshown; raise it if people stack more.
    private const int MaxFigures = 4;
    private const string DockBubbleText = "i'll wait up here";
    private const float RingStartScale = 0.85f, RingStiffness = 300, RingDamping = 26;

    /// <summary>Marshals calls onto the thread that owns the windows (the one that constructed the controller).</summary>
    private readonly Control _ui = new();
    private readonly LayeredWindow _cursorWindow, _drawingWindow;
    /// <summary>The model's persistent drawing, in its own window (<see cref="_drawingWindow"/> keeps the click ring and legacy marks).</summary>
    private readonly DrawingWindow _drawing;
    private volatile bool _hasDrawing;
    private int _drawingGeneration;
    private readonly ClickCatcher _catcher;
    /// <summary>One invisible click target over each drawn button (at most <see cref="DrawingRenderer.MaxButtons"/>).</summary>
    private readonly ClickCatcher[] _buttonCatchers = new ClickCatcher[DrawingRenderer.MaxButtons];
    private readonly Rectangle?[] _buttonRects = new Rectangle?[DrawingRenderer.MaxButtons];
    private readonly (string Label, string Prompt)[] _buttonActions = new (string, string)[DrawingRenderer.MaxButtons];
    private readonly List<(string Label, string Prompt, Rectangle Rect)> _liveButtons = new(DrawingRenderer.MaxButtons);
    private double _lastButtonSync = double.NegativeInfinity;
    /// <summary>
    /// Polls the click targets (about 10 Hz) while the drawing has a button, up or held down, so a starting task or an open
    /// dialog takes them down, and its end brings them back, even when the frame loop is stopped.
    /// </summary>
    private readonly System.Windows.Forms.Timer _buttonTimer = new() { Interval = ButtonPollMilliseconds };
    /// <summary>One window per figure part, by part id. A changed part (an edit, an undo) gets a new window; a gone one closes.</summary>
    private readonly Dictionary<string, (DrawPart Part, FigureWindow Window)> _figures = [];
    private nint[] _figureHandles = [];
    /// <summary>"Show in screen recordings" off: every overlay window, a figure window from when it opens, is excluded from capture.</summary>
    private bool _excludeFromCapture;
    private readonly Action<string>? _log;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CancellationTokenSource? _selectionCancellation;
    private volatile bool _disposed;

    // UI-thread state.
    private Mode _mode = Mode.Idle;
    private bool _hidden;
    private CompanionStyle _style = CompanionStyle.From(new AppSettings(), reducedMotion: false);
    private Color _accent;
    private CursorSpring? _spring;
    private bool _cursorShown;
    private double _lastTick;
    private Point? _pointTarget;
    private string? _pointLabel;
    private Box? _pointFrame;
    /// <summary>The curved flight in progress (a POINT's, or the return to the cursor); the tip is placed on it each frame.</summary>
    private FlightPath? _flight;
    private double _flightStart;
    private bool _returning;
    private Point _returnFrom;
    /// <summary>A POINT whose flight starts on the next visible frame, from wherever the tip is by then (e.g. the notch).</summary>
    private bool _flightPending;
    /// <summary>The tip has landed: the click ring is revealed from then (spring from 0.85 to 1, then the breathing pulse).</summary>
    private bool _pointArrived;
    private double _arrivedAt, _ringLastStep;
    private CursorSpring? _ringSpring;
    private string? _line;
    private bool _lineInstant;
    private double _lineStart;
    /// <summary>When the companion went idle while a line showed: the line fades from then instead of holding.</summary>
    private double? _lineIdleAt;
    private CursorRenderer.MessageLayout? _lineLayout;
    private double _micLevel;
    private readonly float[] _bars = new float[CursorRenderer.BarCount];
    private IReadOnlyList<VirtualAnnotation> _marks = [];
    private double _marksStart;
    private bool _drawingDirty;
    private Bitmap? _cursorBitmap, _drawingBitmap;
    /// <summary>What the last still cursor frame showed; an unchanged still frame is not redrawn.</summary>
    private (Pt Tip, float Scale, Mode Mode, Color Accent, float DockScale, float DockAlpha) _lastStill;

    // Dock into the notch, and shake to detach.
    private readonly ShakeDetector _shake = new();
    private readonly DetachState _detach = new();
    private Pt? _notchPoint;
    private readonly DockMachine _dock = new();
    private double _lastFullscreenCheck = double.NegativeInfinity;
    private bool _foregroundFullscreen;
    private Rectangle? _catcherRect;

    /// <param name="log">Diagnostics (window names and Win32 codes only).</param>
    public BuddyOverlayController(Action<string>? log = null)
    {
        _ = _ui.Handle;
        _log = log;
        _cursorWindow = new("OpenClicky Companion", log);
        _drawingWindow = new("OpenClicky Drawing", log);
        _drawing = new(log);
        _catcher = new("OpenClicky Companion Click");
        _catcher.Clicked += OnCatcherClicked;
        for (var i = 0; i < _buttonCatchers.Length; i++)
        {
            var index = i;
            _buttonCatchers[i] = new($"OpenClicky Drawing Button {i + 1}");
            _buttonCatchers[i].Clicked += () => OnButtonClicked(index);
        }
        _accent = ParseAccent(_style.AccentHex);
        _timer.Tick += (_, _) => Frame();
        _buttonTimer.Tick += (_, _) => SyncButtons();
        // A spotlight reaching its end of life needs frames to fade out, even if the loop had stopped.
        _drawing.Wake = () =>
        {
            if (_disposed || _hidden || _timer.Enabled) return;
            _lastTick = Now;
            _timer.Start();
        };
    }

    /// <summary>The cursor window. Smoke / diagnostics only.</summary>
    public IntPtr WindowHandle => _cursorWindow.Handle;

    /// <summary>The cursor window (it carries the companion tip).</summary>
    public IntPtr BuddyTipHandle => _cursorWindow.Handle;

    /// <summary>Raised (UI thread) when the detached companion is clicked; it has already reattached.</summary>
    public event Action? DetachedClicked;

    /// <summary>
    /// Asked every frame (UI thread): true while an agent task is running unpaused or driving. Then shake detection is off
    /// and any detached state and click target are dropped, so injected zig-zags and clicks cannot detach or be swallowed.
    /// </summary>
    public Func<bool>? DetachSuppressed { get; set; }

    /// <summary>
    /// Asked about every 100 ms (UI thread): true while the host has a dialog of its own open. The drawing-button click targets
    /// come down then (they are topmost and invisible, and must not take a click meant for the dialog). Not used for the
    /// detach target, which only <see cref="DetachSuppressed"/> governs.
    /// </summary>
    public Func<bool>? DrawingButtonsSuppressed { get; set; }

    private bool ButtonsSuppressed => DetachSuppressed?.Invoke() == true || DrawingButtonsSuppressed?.Invoke() == true;

    /// <summary>
    /// Raised (UI thread) with the label and the model-authored prompt when a drawn button is clicked. The prompt is untrusted
    /// text: the subscriber runs it as an ordinary talk request, never with extra permission.
    /// </summary>
    public event Action<string, string>? ButtonClicked;

    /// <summary>
    /// Every overlay window exists from construction until disposal: the cursor, the marks and ring, the detach click target,
    /// the ink, then the three drawing-button click targets (shown only over a live button). Then one window per figure on
    /// screen, from when its part is drawn until it goes (read on the UI thread).
    /// </summary>
    public IReadOnlyList<nint> WindowHandles => _disposed ? []
        : [_cursorWindow.Handle, _drawingWindow.Handle, _catcher.Handle, _drawing.Handle, .. _buttonCatchers.Select(c => c.Handle), .. _figureHandles];

    /// <summary>
    /// Excludes every overlay window from screen capture (WDA_EXCLUDEFROMCAPTURE), or includes them again, now and for each
    /// figure window opened later. A window Windows refuses is logged.
    /// </summary>
    public void SetCaptureExclusion(bool exclude) => Post(() =>
    {
        _excludeFromCapture = exclude;
        foreach (var hwnd in WindowHandles) ApplyCaptureExclusion(hwnd);
    });

    private void ApplyCaptureExclusion(nint hwnd)
    {
        if (hwnd != 0 && !SetWindowDisplayAffinity(hwnd, _excludeFromCapture ? WdaExcludeFromCapture : WdaNone)) _log?.Invoke("overlay_capture_affinity_failed");
    }

    /// <summary>The model's drawing window. Smoke / diagnostics only.</summary>
    public IntPtr DrawingHandle => _drawing.Handle;

    /// <summary>True once a POINT has brought the tip to rest on its target (still pointing).</summary>
    public bool IsPointFlyComplete => InvokeRead(() =>
        _mode == Mode.Pointing && _pointTarget is { } target && _spring is { } spring && _flight is null && !_flightPending
        && Distance(spring.Position, target) <= 0.5f && MathF.Sqrt(spring.Velocity.X * spring.Velocity.X + spring.Velocity.Y * spring.Velocity.Y) < 5);

    /// <summary>Companion tip in virtual-screen pixels during / after a POINT; false when not pointing.</summary>
    public bool TryGetBuddyVirtualPoint(out int virtualX, out int virtualY)
    {
        var (ok, x, y) = InvokeRead(() => (_mode != Mode.Pointing && _pointTarget is null) || _spring is null
            ? (false, 0, 0)
            : (true, (int)MathF.Round(_spring.Position.X), (int)MathF.Round(_spring.Position.Y)));
        virtualX = x;
        virtualY = y;
        return ok;
    }

    public void ShowListening() => Post(() =>
    {
        ClearPointing();
        SetLine(null, false);
        ClearMarks(); // a new question starts a clean screen
        _mode = Mode.Listening;
        _micLevel = 0;
        Array.Fill(_bars, CursorRenderer.BarTarget(0, 0));
        Show();
    });

    public void ShowProcessing() => Post(() =>
    {
        ClearPointing();
        SetLine(null, false);
        _mode = Mode.Processing;
        Show();
    });

    public void ShowTranscript(string text) => Post(() =>
    {
        if (_mode == Mode.Listening) SetLine(text, instant: true);
    });

    public void ShowAnnotations(IReadOnlyList<VirtualAnnotation> marks) => Post(() =>
    {
        _marks = marks.ToArray();
        _marksStart = Now;
        if (_marks.Count == 0) ClearMarks();
        if (!_hidden) Show();
    });

    public bool HasDrawing => _hasDrawing;

    public void ShowDrawing(IReadOnlyList<DrawPart> parts, IReadOnlyList<PartAnimation> animations)
    {
        _hasDrawing = parts.Count > 0;
        // A call queued from another thread must not land after a newer one that ran inline (Esc on the UI thread would
        // otherwise be undone by a pending paint). Each call carries the full drawing, so only the newest matters.
        // ponytail: a skipped call's reveal is lost (those parts snap in); that needs two calls queued within one UI tick.
        var generation = Interlocked.Increment(ref _drawingGeneration);
        Post(() =>
        {
            if (generation != Volatile.Read(ref _drawingGeneration)) return;
            _drawing.Show(parts, animations, Now);
            SyncFigures(parts, animations);
            // A part being drawn brings a docked companion out and lets it ride the pen (thinking dots give way to the ink).
            if (animations.Any(a => !a.Erase) && _mode is Mode.Idle or Mode.Processing) _mode = Mode.Responding;
            if (!_hidden) Show();
        });
    }

    public void FinishDrawing() => Post(() =>
    {
        _drawing.Finish();
        foreach (var (_, window) in _figures.Values) window.Finish();
        if (!_hidden) Show();
    });

    public void ShowResponding(string spokenText) => Post(() =>
    {
        SetLine(spokenText, false);
        // Keep an in-flight / held POINT; otherwise the companion follows the cursor while it speaks.
        _mode = OverlayRespondingMode.Resolve(_mode == Mode.Pointing || _pointTarget is not null) switch
        {
            OverlaySpokenVisualMode.Pointing => Mode.Pointing,
            OverlaySpokenVisualMode.Responding => Mode.Responding,
            var other => throw new InvalidOperationException($"Unexpected spoken visual mode: {other}"),
        };
        Show();
    });

    public void PointAt(int virtualX, int virtualY, string? label, string? spokenText = null) =>
        PointAt(virtualX, virtualY, label, spokenText, null);

    public void PointAt(int virtualX, int virtualY, string? label, string? spokenText, Box? controlFrame) => Post(() =>
    {
        _pointTarget = new Point(virtualX, virtualY);
        _pointLabel = string.IsNullOrWhiteSpace(label) ? null : label.Length > MaxLabelChars ? label[..MaxLabelChars] + "…" : label;
        _pointFrame = controlFrame;
        // The flight starts on the next visible frame; the ring is drawn once the tip has landed.
        _flight = null;
        _returning = false;
        _flightPending = true;
        _pointArrived = false;
        if (spokenText is not null) SetLine(spokenText, false);
        _mode = Mode.Pointing;
        Show();
    });

    public void ShowIdle() => Post(() =>
    {
        var wasPointing = _pointTarget is not null;
        ClearPointing();
        if (wasPointing) BeginReturn();
        if (_line is not null) _lineIdleAt ??= Now; // the last spoken line fades out rather than vanishing
        _mode = Mode.Idle;
        Show();
    });

    public void SetNotchPoint(int virtualX, int virtualY) => Post(() => _notchPoint = new Pt(virtualX, virtualY));

    public void ApplyStyle(CompanionStyle style) => Post(() =>
    {
        // A visible companion that is told to dock says so before it goes; one that was never shown just stays away.
        _dock.PresenceChanged(_style.Presence, style.Presence, _cursorShown);
        var figuresChange = style.ReducedMotion != _style.ReducedMotion || ParseAccent(style.AccentHex) != _accent;
        _style = style;
        _accent = ParseAccent(style.AccentHex);
        _drawingDirty = true;
        _drawing.Invalidate();
        if (figuresChange && _figures.Count > 0)
        {
            // Figures take the accent and motion setting when built: build them again, whole (no second reveal).
            var parts = _figures.Values.Select(f => f.Part).ToArray();
            CloseFigures();
            SyncFigures(parts, []);
        }
        if (!_hidden) Show();
    });

    public void ShowMicLevel(double level) => Post(() => _micLevel = level);

    public Task<Rectangle?> SelectRegionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromException<Rectangle?>(new ObjectDisposedException(nameof(BuddyOverlayController)));
        }

        if (!_ui.InvokeRequired)
        {
            return SelectRegionOnUiThreadAsync(cancellationToken);
        }

        var completion = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.BeginInvoke(async () =>
        {
            try
            {
                completion.SetResult(await SelectRegionOnUiThreadAsync(cancellationToken));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private async Task<Rectangle?> SelectRegionOnUiThreadAsync(CancellationToken cancellationToken)
    {
        Hide();
        var selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _selectionCancellation = selectionCancellation;
        try
        {
            return await ScreenRegionSelector.SelectAsync(selectionCancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, selectionCancellation))
            {
                _selectionCancellation = null;
            }
            selectionCancellation.Dispose();
        }
    }

    /// <summary>Synchronous: every overlay window is hidden when this returns (it runs before captures).</summary>
    public void Hide()
    {
        void HideNow()
        {
            if (_disposed) return;
            _selectionCancellation?.Cancel();
            _hidden = true;
            _timer.Stop();
            _cursorWindow.HideWindow();
            _drawingWindow.HideWindow(); // marks and the click ring
            _drawing.HideWindow(); // kept: the next Show* brings it back
            foreach (var (_, window) in _figures.Values) window.Hide(); // so are the figures
            // A POINT that is still held flies in again, and its ring is drawn again, when the overlay next appears.
            _flight = null;
            _returning = false;
            _flightPending = _pointTarget is not null;
            _pointArrived = false;
            HideCatcher();
            HideButtons(); // synchronous like the windows: nothing of ours is left to take a click before a capture
            _cursorShown = false;
            ApplyDockEffect(_dock.Interrupt()); // docking is abandoned; the next appearance grows out of the notch
            DropDetach();
        }
        if (_ui.InvokeRequired) _ui.Invoke(HideNow);
        else HideNow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _selectionCancellation?.Cancel();
        void DisposeNow()
        {
            _timer.Dispose();
            _buttonTimer.Dispose();
            _cursorWindow.Dispose();
            _drawingWindow.Dispose();
            _drawing.Dispose();
            CloseFigures();
            _catcher.Dispose();
            foreach (var button in _buttonCatchers) button.Dispose();
            _cursorBitmap?.Dispose();
            _drawingBitmap?.Dispose();
            _ui.Dispose();
        }
        if (_ui.IsHandleCreated && _ui.InvokeRequired) _ui.BeginInvoke(DisposeNow);
        else DisposeNow();
    }

    private void Post(Action action)
    {
        if (_disposed) return;
        if (_ui.InvokeRequired) _ui.BeginInvoke(() => { if (!_disposed) action(); });
        else action();
    }

    private T InvokeRead<T>(Func<T> read) => _ui.InvokeRequired ? (T)_ui.Invoke(read)! : read();

    private double Now => _clock.Elapsed.TotalSeconds;

    private bool CursorVisible => !_hidden && _style.Presence != CursorPresence.Off && _dock.Visible;

    /// <summary>While docked, only a POINT (or marks or a drawing during a reply) brings the companion out of the notch.</summary>
    private bool WantsCursorOut => _mode == Mode.Pointing || (_mode == Mode.Responding && (_marks.Count > 0 || _drawing.HasParts));

    /// <summary>The stored notch point if a live monitor holds it, else the nearest live monitor's top centre.</summary>
    private Pt Notch => NotchPoint.Resolve(_notchPoint,
        Screen.AllScreens.Select(screen => new Box(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)).ToArray());

    /// <summary>Ends any <see cref="Hide"/> and renders a frame now, so visibility changes before the call returns.</summary>
    private void Show()
    {
        _hidden = false;
        _drawingDirty = true;
        _drawing.Invalidate();
        foreach (var (_, window) in _figures.Values) if (!window.IsVisible) window.ShowAt();
        if (!_timer.Enabled) _lastTick = Now;
        _timer.Start();
        Frame();
    }

    private void ClearPointing()
    {
        _pointTarget = null;
        _pointLabel = null;
        _pointFrame = null;
        _flight = null;
        _returning = false;
        _flightPending = false;
        _pointArrived = false;
        _ringSpring = null;
    }

    /// <summary>
    /// After a POINT the tip flies back to the real cursor's resting spot. With reduced motion (or when the companion is not on
    /// screen) there is no flight: the spring just follows. <see cref="DrawCursorFrame"/> cancels the flight when the cursor moves.
    /// </summary>
    private void BeginReturn()
    {
        if (_style.ReducedMotion || _spring is not { } spring || !_cursorShown) return;
        var cursor = Cursor.Position;
        var scale = DpiScale.ForPoint(cursor);
        _flight = new FlightPath(spring.Position, new Pt(cursor.X + CursorConstants.TipOffsetX * scale, cursor.Y + CursorConstants.TipOffsetY * scale));
        _flightStart = Now;
        _returning = true;
        _returnFrom = cursor;
    }

    /// <summary>
    /// A return flight ends when the real cursor has moved more than <see cref="CursorConstants.ReturnCancelDistance"/> DIP since
    /// it began; the spring then follows the cursor. Cancelling is final.
    /// </summary>
    private bool ReturnCancelled(Point cursor, float cursorScale)
    {
        if (!_returning || !ReturnFlight.Cancelled(new Pt(_returnFrom.X, _returnFrom.Y), new Pt(cursor.X, cursor.Y), cursorScale)) return false;
        _flight = null;
        _returning = false;
        return true;
    }

    private void SetLine(string? text, bool instant)
    {
        _line = string.IsNullOrWhiteSpace(text) ? null : text;
        _lineInstant = instant;
        _lineStart = Now;
        _lineIdleAt = null;
        _lineLayout = null;
    }

    private void ClearMarks()
    {
        _marks = [];
        _drawingWindow.HideWindow();
        _drawingBitmap?.Dispose();
        _drawingBitmap = null;
        _drawingDirty = true; // a held click ring is drawn again on its own
    }

    private void Frame()
    {
        if (_disposed) return;
        var now = Now;
        var dt = now - _lastTick;
        _lastTick = now;
        if (_hidden)
        {
            _timer.Stop();
            return;
        }

        DrawCursorFrame(now, dt);
        var drawingAnimating = DrawDrawingFrame(now);
        var inkAnimating = _drawing.Render(now, _accent, _style.ReducedMotion);
        var idle = !CursorVisible && !drawingAnimating && !inkAnimating;
        // While frames run the targets are re-synced at the poll rate; the last frame before the loop stops always syncs, and
        // the button timer carries on from there for as long as a target is up.
        if (idle || now - _lastButtonSync >= ButtonPollMilliseconds / 1000.0) SyncButtons();
        if (idle) _timer.Stop();
    }

    /// <summary>
    /// Puts a click target over each fully drawn button and takes the rest down. None is up while an agent task runs (it injects
    /// clicks of its own, and a target on top of a button would swallow one) or while the host has a dialog open. Runs from the
    /// frame loop and, while a target is up, from <see cref="_buttonTimer"/>.
    /// </summary>
    private void SyncButtons()
    {
        _lastButtonSync = Now;
        var live = _liveButtons;
        if (_hidden || ButtonsSuppressed) live.Clear();
        else _drawing.LiveButtons(live);
        // Polled while any button is drawn, even one held down, so it comes back when the task or dialog ends.
        var poll = !_hidden && _drawing.HasButtons;
        if (_buttonTimer.Enabled != poll) _buttonTimer.Enabled = poll;
        for (var i = 0; i < _buttonCatchers.Length; i++)
        {
            if (i >= live.Count)
            {
                HideButton(i);
                continue;
            }
            _buttonActions[i] = (live[i].Label, live[i].Prompt);
            if (_buttonRects[i] == live[i].Rect && _buttonCatchers[i].IsShown) continue;
            _buttonCatchers[i].Show(live[i].Rect);
            _buttonRects[i] = live[i].Rect;
        }
    }

    private void HideButton(int index)
    {
        _buttonCatchers[index].Hide();
        _buttonRects[index] = null;
    }

    private void HideButtons()
    {
        for (var i = 0; i < _buttonCatchers.Length; i++) HideButton(i);
        _buttonTimer.Stop();
        _lastButtonSync = double.NegativeInfinity; // the next frame syncs at once
    }

    private void OnButtonClicked(int index)
    {
        if (_disposed || _hidden || ButtonsSuppressed || _buttonRects[index] is null) return;
        var (label, prompt) = _buttonActions[index];
        ButtonClicked?.Invoke(label, prompt);
    }

    /// <summary>
    /// Keeps one window per figure part (the newest <see cref="MaxFigures"/>): a gone or changed part's window closes, a new one
    /// opens (shown at once unless the overlay is hidden), revealing with its part's animation or whole when it has none.
    /// </summary>
    private void SyncFigures(IReadOnlyList<DrawPart> parts, IReadOnlyList<PartAnimation> animations)
    {
        var wanted = parts.Where(p => p.Op is { Kind: DrawOpKind.Figure, Figure: not null })
            .OrderByDescending(p => p.Seq).Take(MaxFigures).ToDictionary(p => p.Id);
        foreach (var (id, (part, window)) in _figures.ToArray())
        {
            if (wanted.TryGetValue(id, out var keep) && Equals(keep, part)) continue;
            window.Close();
            _figures.Remove(id);
        }
        foreach (var part in wanted.Values)
        {
            if (_figures.ContainsKey(part.Id) || FigureWindow.BoundsOf(part.Op) is not { } bounds) continue;
            var reveal = animations.FirstOrDefault(a => !a.Erase && a.Part.Id == part.Id && a.Part.Seq == part.Seq);
            var window = new FigureWindow(part.Op.Figure!, bounds, _accent, _style.ReducedMotion, reveal?.Start);
            _figures[part.Id] = (part, window);
            if (_excludeFromCapture) ApplyCaptureExclusion(window.Handle); // before it is first shown
            if (!_hidden) window.ShowAt();
        }
        _figureHandles = _figures.Values.Select(f => f.Window.Handle).ToArray();
    }

    private void CloseFigures()
    {
        foreach (var (_, window) in _figures.Values) window.Close();
        _figures.Clear();
        _figureHandles = [];
    }

    private void DrawCursorFrame(double now, double dt)
    {
        var cursor = Cursor.Position;
        var cursorScale = DpiScale.ForPoint(cursor);
        var follow = new Pt(cursor.X + CursorConstants.TipOffsetX * cursorScale, cursor.Y + CursorConstants.TipOffsetY * cursorScale);
        var spring = _spring ??= new CursorSpring(follow);
        var notch = _style.Presence == CursorPresence.Docked || _dock.Phase != DockPhase.None ? Notch : default;
        ApplyDockEffect(_dock.Step(now, new DockInput(_style.Presence, WantsCursorOut, _cursorShown, _style.ReducedMotion, spring.Position, notch)), spring);
        if (!CursorVisible)
        {
            _cursorWindow.HideWindow();
            HideCatcher();
            _cursorShown = false;
            return;
        }

        StepDetach(cursor, now, spring.Position);
        // While a part is being drawn the companion rides the pen tip; afterwards it follows the cursor again.
        var pen = _drawing.PenTip(now, _style.ReducedMotion);
        var target = _mode == Mode.Pointing && _pointTarget is { } point ? new Pt(point.X, point.Y) : _detach.Detached ? _detach.At : pen ?? follow;
        if (!_cursorShown && _dock.Phase != DockPhase.Grow) spring.Snap(follow); // reappearing: start beside the real cursor, not where it was last seen
        if (_flightPending && _pointTarget is { } landing)
        {
            _flightPending = false;
            if (!_style.ReducedMotion)
            {
                _flight = new FlightPath(spring.Position, new Pt(landing.X, landing.Y));
                _flightStart = now;
                _returning = false;
            }
        }
        if (_dock.FlightAt(now) is { } flying) { _flight = null; spring.Snap(flying); }
        else if (_dock.Phase == DockPhase.Shrink) { _flight = null; if (!_style.ReducedMotion) spring.Snap(Notch); }
        else if (_style.ReducedMotion) { _flight = null; spring.Snap(target); }
        else if (_flight is { } flight && !ReturnCancelled(cursor, cursorScale))
        {
            var elapsed = now - _flightStart;
            spring.Snap(flight.At(elapsed)); // the last point of a flight is the target itself
            if (elapsed >= flight.DurationSeconds) _flight = null;
        }
        else spring.Step(target, dt);
        if (_flight is null) _returning = false;
        if (_pointTarget is not null && !_pointArrived && _flight is null && !_flightPending)
        {
            _pointArrived = true;
            _arrivedAt = _ringLastStep = now;
            _ringSpring = new CursorSpring(new Pt(RingStartScale, 0), RingStiffness, RingDamping);
        }

        var (dockScale, dockAlpha) = _dock.Look(now, _style.ReducedMotion);
        var tip = new PointF(spring.Position.X, spring.Position.Y);
        var s = DpiScale.ForPoint(Point.Round(tip));
        UpdateCatcher(tip, s);
        var still = (spring.Position, s, _mode, _accent, dockScale, dockAlpha);
        if (_mode is not (Mode.Listening or Mode.Processing) && _line is null && _cursorShown && still == _lastStill) return;
        _lastStill = still;
        var chrome = _dock.Chrome; // bars, dots and bubbles stay behind as it flies into the notch
        var cs = s * dockScale;
        var triangle = CursorRenderer.Triangle(tip, cs);
        var bounds = CursorRenderer.CompanionBounds(triangle, cs);
        var listening = chrome && _mode == Mode.Listening;
        var processing = chrome && _mode == Mode.Processing;
        if (listening)
        {
            for (var i = 0; i < _bars.Length; i++)
            {
                var goal = CursorRenderer.BarTarget(i, _micLevel);
                _bars[i] = _style.ReducedMotion ? goal : _bars[i] + (goal - _bars[i]) * BarEase;
            }
            bounds = RectangleF.Union(bounds, CursorRenderer.BarsBounds(tip, s));
        }
        var centroid = CursorRenderer.Centroid(triangle);
        if (processing) bounds = RectangleF.Union(bounds, CursorRenderer.DotsBounds(centroid, s));

        var workArea = Screen.FromPoint(Point.Round(tip)).WorkingArea;
        var work = new Box(workArea.X, workArea.Y, workArea.Width, workArea.Height);
        RectangleF? messageBox = null;
        var (revealed, alpha) = MessageState(now, s);
        if (chrome && _lineLayout is { } layout && alpha > 0)
        {
            var anchor = new Pt(tip.X + (_mode == Mode.Listening ? ListeningBubbleShiftDip * s : 0), tip.Y);
            var desired = ToRect(BubbleLayout.Message(anchor, layout.Size.Width, layout.Size.Height, work));
            messageBox = ResponseBubblePlacement.Place(desired, workArea, AnnotationBounds(workArea));
            bounds = RectangleF.Union(bounds, messageBox.Value);
        }

        var frame = Rectangle.FromLTRB((int)MathF.Floor(bounds.Left), (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right), (int)MathF.Ceiling(bounds.Bottom));
        var bitmap = EnsureBitmap(ref _cursorBitmap, frame.Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            CursorRenderer.Prepare(g);
            g.TranslateTransform(-frame.X, -frame.Y);
            CursorRenderer.DrawCompanion(g, triangle, cs, _accent);
            if (listening) CursorRenderer.DrawBars(g, tip, s, _bars, _accent);
            if (processing) CursorRenderer.DrawDots(g, centroid, s, now, _style.ReducedMotion, _accent);
            if (messageBox is { } mb) CursorRenderer.DrawMessage(g, mb, _lineLayout!, revealed, alpha);
        }
        _cursorWindow.Present(bitmap, frame.Location, (byte)Math.Round(255 * dockAlpha));
        _cursorShown = true;
    }

    /// <summary>Acts on what the dock machine changed: the bubble, and growing out of the notch.</summary>
    private void ApplyDockEffect(DockEffect effect, CursorSpring? spring = null)
    {
        if (effect.HasFlag(DockEffect.BubbleEnded) && _line == DockBubbleText) SetLine(null, false); // a POINT's own line is not ours to clear
        if (effect.HasFlag(DockEffect.BubbleStarted)) SetLine(DockBubbleText, false);
        if (effect.HasFlag(DockEffect.GrowStarted)) spring?.Snap(Notch);
    }

    /// <summary>
    /// Feeds the shake detector while the companion could detach. A held mouse button (a drag or scribble), a full-screen
    /// foreground window and a running agent task all keep it from firing; a detached companion that no longer may stay put
    /// follows again.
    /// </summary>
    private void StepDetach(Point cursor, double now, Pt tip)
    {
        _detach.Presence = _style.Presence;
        var ms = (long)(now * 1000);
        var eligible = _style.ShakeToDetach && _dock.Phase == DockPhase.None && (_mode is Mode.Idle or Mode.Listening)
            && DetachSuppressed?.Invoke() != true;
        if (_detach.Detached && !eligible) DropDetach();
        else _detach.Tick(ms); // after 3 s the spring carries it back to the cursor
        if (_detach.Detached || !eligible) return;
        if (Control.MouseButtons != MouseButtons.None || ForegroundIsFullscreen(now)) _shake.Reset();
        else if (_shake.Add(new Pt(cursor.X, cursor.Y), ms)) _detach.Shake(tip, ms);
    }

    private void DropDetach()
    {
        _detach.Clicked();
        _shake.Reset();
        HideCatcher();
    }

    /// <summary>Checked at most every 250 ms: it is a window and monitor query.</summary>
    private bool ForegroundIsFullscreen(double now)
    {
        if (now - _lastFullscreenCheck < 0.25) return _foregroundFullscreen;
        _lastFullscreenCheck = now;
        return _foregroundFullscreen = ForegroundFullscreen.IsActive();
    }

    /// <summary>A 36 DIP click target over the detached companion, wherever it rests.</summary>
    private void UpdateCatcher(PointF tip, float s)
    {
        if (!_detach.Detached)
        {
            HideCatcher();
            return;
        }

        var centre = CursorRenderer.Centroid(CursorRenderer.Triangle(tip, s));
        var half = CatcherDip * s / 2;
        var rect = Rectangle.Round(new RectangleF(centre.X - half, centre.Y - half, half * 2, half * 2));
        if (_catcherRect == rect && _catcher.IsShown) return;
        _catcher.Show(rect);
        _catcherRect = rect;
    }

    private void HideCatcher()
    {
        _catcher.Hide();
        _catcherRect = null;
    }

    private void OnCatcherClicked()
    {
        if (_disposed || !_detach.Detached) return;
        _detach.Clicked();
        HideCatcher();
        DetachedClicked?.Invoke();
    }

    /// <summary>Words revealed (60 ms each; all at once with reduced motion) and the fade: 4 s after the last word, then 400 ms.</summary>
    private (int Revealed, float Alpha) MessageState(double now, float s)
    {
        if (_line is null) return (0, 0);
        if (_lineLayout is null || _lineLayout.Scale != s) _lineLayout = CursorRenderer.LayoutMessage(_line, s);
        var words = _lineLayout.WordCount;
        var instant = _lineInstant || _style.ReducedMotion;
        var revealedBy = Math.Min(now, _lineIdleAt ?? now);
        var revealed = instant ? words : Math.Min(words, 1 + (int)((revealedBy - _lineStart) / WordSeconds));
        var lastWord = _lineStart + (instant ? 0 : Math.Max(0, words - 1) * WordSeconds);
        var alpha = MessageAlpha(now, lastWord, _lineIdleAt, _style.ReducedMotion);
        if (alpha <= 0) SetLine(null, false);
        return (revealed, alpha);
    }

    /// <summary>
    /// Message bubble opacity: 1 until <see cref="HoldSeconds"/> after the last word (or until the companion went idle, if
    /// sooner), then a linear fade over 400 ms (120 ms with reduced motion).
    /// </summary>
    internal static float MessageAlpha(double now, double lastWordAt, double? idleAt, bool reducedMotion)
    {
        var fadeStart = Math.Min(lastWordAt + HoldSeconds, idleAt ?? double.PositiveInfinity);
        var fade = reducedMotion ? ReducedFadeSeconds : FadeSeconds;
        return (float)Math.Clamp(1 - (now - fadeStart) / fade, 0, 1);
    }

    /// <summary>
    /// Repaints the marks while their pen reveal runs (350 ms each, one after another) and the click ring while it reveals and
    /// breathes; false once everything is still. The window covers the whole virtual screen while marks show, and only the ring
    /// (and its label) otherwise.
    /// </summary>
    // ponytail: with marks showing, a full virtual-screen bitmap is re-sent each ring frame; C6's drawing layer owns damage tracking.
    private bool DrawDrawingFrame(double now)
    {
        var ring = CurrentRing(now);
        if (_marks.Count == 0 && ring is null)
        {
            _drawingWindow.HideWindow();
            return false;
        }
        var ringAnimating = ring is { Animating: true };
        if (!_drawingDirty && !ringAnimating) return false;

        var area = _marks.Count > 0 ? SystemInformation.VirtualScreen : Rectangle.Ceiling(ring!.Value.Paint);
        var bitmap = EnsureBitmap(ref _drawingBitmap, area.Size);
        var animating = false;
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            CursorRenderer.Prepare(g);
            g.TranslateTransform(-area.X, -area.Y);
            for (var index = 0; index < _marks.Count; index++)
            {
                var progress = _style.ReducedMotion ? 1f : (float)Math.Clamp((now - _marksStart - index * MarkSeconds) / MarkSeconds, 0, 1);
                if (progress < 1) animating = true;
                if (progress > 0) DrawMark(g, _marks[index], progress);
            }
            if (ring is { } r) DrawRing(g, r);
        }
        _drawingWindow.Present(bitmap, area.Location);
        _drawingDirty = animating;
        return animating || ringAnimating;
    }

    /// <summary>Everything the click ring needs for one frame.</summary>
    private readonly record struct RingFrame(RectangleF Ring, float Corner, float Scale, float Alpha, float Dip, RectangleF? Label, RectangleF Paint, bool Animating);

    /// <summary>
    /// The ring round the POINT target once the tip has landed: its box (the control's frame, or a 28 DIP circle), the spring
    /// reveal (scale 0.85 to 1) and the 1.6 s breathing pulse (opacity 0.75 to 1, up to 3 DIP bigger). Reduced motion: no spring
    /// and no pulse, just a 120 ms fade. Null when there is nothing to draw.
    /// </summary>
    private RingFrame? CurrentRing(double now)
    {
        if (_hidden || !_pointArrived || _pointTarget is not { } target || _ringSpring is not { } spring) return null;
        var dip = DpiScale.ForPoint(target);
        var workArea = Screen.FromPoint(target).WorkingArea;
        var layout = ClickRingLayout.From(_pointFrame, new Pt(target.X, target.Y), dip,
            new Box(workArea.X, workArea.Y, workArea.Width, workArea.Height));

        var elapsed = now - _arrivedAt;
        var reduced = _style.ReducedMotion;
        var scale = 1f;
        if (!reduced)
        {
            scale = spring.Step(new Pt(1, 0), now - _ringLastStep).X;
            _ringLastStep = now;
        }
        var (alpha, inflateDip) = ClickRingLayout.Pulse(elapsed, reduced);

        var box = ToRect(layout.Ring);
        box.Inflate(inflateDip * dip, inflateDip * dip);
        var centre = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
        var ring = new RectangleF(centre.X - box.Width * scale / 2, centre.Y - box.Height * scale / 2, box.Width * scale, box.Height * scale);

        // What can ever be painted, so the window (and its bitmap) keeps one size: the biggest pulse, a spring overshoot, the glow.
        var reach = Math.Max(layout.Ring.W, layout.Ring.H) * 0.15f + CursorRenderer.RingPaintMarginDip * dip;
        var paint = ToRect(layout.Ring);
        paint.Inflate(reach, reach);

        RectangleF? label = null;
        if (_pointLabel is { } text)
        {
            var size = CursorRenderer.LabelSize(text, dip);
            var x = Math.Clamp(layout.LabelAnchor.X - size.Width / 2, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width));
            var y = layout.LabelBelow ? layout.LabelAnchor.Y : layout.LabelAnchor.Y - size.Height;
            label = new RectangleF(x, y, size.Width, size.Height);
            paint = RectangleF.Union(paint, label.Value);
        }
        return new RingFrame(ring, layout.CornerRadius * scale, scale, alpha, dip, label, paint, Animating: !reduced || elapsed < ClickRingLayout.FadeInSeconds);
    }

    private void DrawRing(Graphics g, RingFrame ring)
    {
        CursorRenderer.DrawClickRing(g, ring.Ring, ring.Corner, ring.Dip, _accent, ring.Alpha);
        if (ring.Label is { } box && _pointLabel is { } text) CursorRenderer.DrawLabel(g, box, text, ring.Dip, _accent);
    }

    private void DrawMark(Graphics g, VirtualAnnotation mark, float progress)
    {
        var s = DpiScale.ForPoint(new Point(mark.X, mark.Y));
        if (mark.Shape == "text")
        {
            if (progress >= 1) DrawMarkLabel(g, mark.X, mark.Y, mark.Label, s);
            return;
        }

        using var path = new GraphicsPath();
        if (mark.Shape is "polygon" or "freehand" or "curve" && mark.Points is { Count: >= 2 } points)
        {
            var pts = points.Select(point => new PointF(point.X, point.Y)).ToArray();
            if (mark.Shape == "curve" && pts.Length == 4) path.AddBezier(pts[0], pts[1], pts[2], pts[3]);
            else if (mark.Shape == "polygon") path.AddPolygon(pts);
            else path.AddLines(pts);
        }
        else if (mark.Shape == "arrow")
        {
            path.AddLine(mark.X, mark.Y, mark.A, mark.B);
        }
        else
        {
            var rect = new Rectangle(mark.X, mark.Y, mark.A, mark.B);
            if (mark.Shape == "circle") path.AddEllipse(rect);
            else path.AddRectangle(rect);
        }

        using var visible = PartialStroke(path, progress);
        if (visible.PointCount > 1)
        {
            using var halo = new Pen(Color.FromArgb(150, CursorRenderer.MessageFill), 8 * s) { LineJoin = LineJoin.Round };
            using var outline = new Pen(_accent, 4 * s) { LineJoin = LineJoin.Round };
            using var head = new AdjustableArrowCap(4, 5, true);
            if (mark.Shape == "arrow" && progress >= 1) outline.CustomEndCap = head;
            g.DrawPath(halo, visible);
            g.DrawPath(outline, visible);
        }
        if (progress >= 1 && !string.IsNullOrWhiteSpace(mark.Label)) DrawMarkLabel(g, mark.X, mark.Y - 36 * s, mark.Label, s);
    }

    private static void DrawMarkLabel(Graphics g, float x, float y, string label, float s)
    {
        var layout = CursorRenderer.LayoutMessage(label, s);
        var workArea = Screen.FromPoint(Point.Round(new PointF(x, y))).WorkingArea;
        var box = ResponseBubblePlacement.Place(new RectangleF(new PointF(x, y), layout.Size), workArea, []);
        CursorRenderer.DrawMessage(g, box, layout, int.MaxValue, 1);
    }

    /// <summary>Painted mark bounds (with halo and breathing room) on this work area, for the message bubble to avoid.</summary>
    private IReadOnlyList<RectangleF> AnnotationBounds(Rectangle workingArea)
    {
        var bounds = new List<RectangleF>(_marks.Count);
        foreach (var mark in _marks)
        {
            RectangleF markBounds;
            if (mark.Shape is "polygon" or "freehand" or "curve" && mark.Points is { Count: >= 2 } points)
            {
                markBounds = RectangleF.FromLTRB(
                    points.Min(point => point.X), points.Min(point => point.Y),
                    points.Max(point => point.X), points.Max(point => point.Y));
            }
            else if (mark.Shape == "text")
            {
                markBounds = new RectangleF(mark.X, mark.Y, 320, 60);
            }
            else if (mark.Shape == "arrow")
            {
                markBounds = RectangleF.FromLTRB(
                    Math.Min(mark.X, mark.A), Math.Min(mark.Y, mark.B),
                    Math.Max(mark.X, mark.A), Math.Max(mark.Y, mark.B));
            }
            else
            {
                markBounds = new RectangleF(mark.X, mark.Y, mark.A, mark.B);
            }

            // Include the widest painted halo and leave breathing room around the annotation.
            markBounds.Inflate(6f, 6f);
            if (markBounds.IntersectsWith(workingArea))
            {
                bounds.Add(markBounds);
            }
        }

        return bounds;
    }

    private static Bitmap EnsureBitmap(ref Bitmap? bitmap, Size size)
    {
        size = new Size(Math.Max(1, size.Width), Math.Max(1, size.Height));
        if (bitmap is not null && bitmap.Size == size) return bitmap;
        bitmap?.Dispose();
        return bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
    }

    private static RectangleF ToRect(Box box) => new(box.X, box.Y, box.W, box.H);

    private static float Distance(Pt a, Point b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Color ParseAccent(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return Color.FromArgb(0x33, 0x80, 0xFF); }
    }

    internal static GraphicsPath PartialStroke(GraphicsPath source, float progress)
    {
        if (progress >= 1) return (GraphicsPath)source.Clone();
        var visible = new GraphicsPath();
        if (progress <= 0 || source.PointCount < 2) return visible;
        using var flat = (GraphicsPath)source.Clone();
        flat.Flatten(null, .5f);
        var points = flat.PathPoints.ToList();
        if ((flat.PathTypes[^1] & (byte)PathPointType.CloseSubpath) != 0) points.Add(points[0]);
        static float Distance(PointF a, PointF b) => MathF.Sqrt(MathF.Pow(b.X - a.X, 2) + MathF.Pow(b.Y - a.Y, 2));
        var remaining = Enumerable.Range(1, points.Count - 1).Sum(i => Distance(points[i - 1], points[i])) * progress;
        for (var i = 1; i < points.Count && remaining > 0; i++)
        {
            var a = points[i - 1]; var b = points[i]; var length = Distance(a, b);
            if (length <= 0) continue;
            var portion = Math.Min(1, remaining / length);
            visible.AddLine(a, new PointF(a.X + (b.X - a.X) * portion, a.Y + (b.Y - a.Y) * portion));
            remaining -= length;
        }
        return visible;
    }

    private const uint WdaNone = 0, WdaExcludeFromCapture = 0x11;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    private enum Mode
    {
        Idle,
        Listening,
        Processing,
        Responding,
        Pointing,
    }
}
