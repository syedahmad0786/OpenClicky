using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using OpenClicky.Core.Surfaces;
using static OpenClicky.App.Surfaces.ShellSurfaceNative;

namespace OpenClicky.App.Surfaces;

/// <summary>
/// A borderless, topmost, non-activating, per-pixel transparent window covering one monitor and hosting
/// WebView2 in visual hosting mode (DirectComposition, WS_EX_NOREDIRECTIONBITMAP).
/// Click-through: the window region is the union of the page's reported hit rects (<c>surface.regions</c>);
/// everywhere else the window does not exist for hit-testing, so input reaches whatever is underneath.
/// Security mirrors <see cref="VisualWorkspacePanel"/>: loopback origin only, pinned navigation, bootstrap token
/// over PostWebMessageAsJson, allow-listed bounded JSON messages, no devtools/context menus/host objects/new windows.
/// </summary>
internal sealed class ShellSurfaceWindow : Form
{
    private const int MaxMessageLength = 64_000;
    private const int MaxPendingMessages = 256;
    private static readonly JsonSerializerOptions MessageJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static readonly string[] BuiltInKinds = ["bootstrap.request", "ready", "surface.regions", "surface.focus"];

    private readonly Uri _origin;
    private readonly string _sessionToken;
    private readonly HashSet<string> _allowedKinds;
    private readonly Queue<string> _pendingMessages = new();
    private string _screenDeviceName;
    private CoreWebView2CompositionController? _controller;
    private IDCompositionDevice? _device;
    private IDCompositionTarget? _target;
    private IDCompositionVisual? _rootVisual;
    private IReadOnlyList<CssRegionRect> _cssRegions = [];
    private bool _ready;
    private bool _frontendReady;
    private bool _trackingLeave;
    private bool _excludeFromCapture;
    private IntPtr _previousForeground;

    /// <param name="origin">HTTP loopback origin without a path; the only origin the surface may navigate to or accept messages from.</param>
    /// <param name="sessionToken">Bootstrap token posted to the page after each trusted navigation (min 32 chars).</param>
    /// <param name="screen">Monitor to cover; defaults to the primary screen.</param>
    /// <param name="pageMessageKinds">Extra message kinds (besides the built-in surface ones) raised through <see cref="PageMessage"/>.</param>
    public ShellSurfaceWindow(Uri origin, string sessionToken, Screen? screen = null, IEnumerable<string>? pageMessageKinds = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsLoopback || origin.Scheme != Uri.UriSchemeHttp || origin.AbsolutePath != "/")
            throw new ArgumentException("The surface origin must be an HTTP loopback origin without a path.", nameof(origin));
        ArgumentNullException.ThrowIfNull(sessionToken);
        if (sessionToken.Length < 32) throw new ArgumentException("The surface session token is too short.", nameof(sessionToken));
        _origin = origin;
        _sessionToken = sessionToken;
        _allowedKinds = new HashSet<string>(BuiltInKinds, StringComparer.Ordinal);
        foreach (var kind in pageMessageKinds ?? []) _allowedKinds.Add(kind);
        _screenDeviceName = (screen ?? Screen.PrimaryScreen ?? Screen.AllScreens[0]).DeviceName;

        Text = "OpenClicky Surface";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = CurrentScreen().Bounds;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
    }

    /// <summary>Raised for allow-listed page messages (including <c>ready</c>), never for bootstrap or surface control messages.</summary>
    public event EventHandler<ShellSurfaceMessageEventArgs>? PageMessage;
    /// <summary>Raised once per navigation when the page posts <c>ready</c>; queued posts are flushed first.</summary>
    public event EventHandler? PageReady;

    public bool IsReady => _ready && _frontendReady;
    /// <summary>Current hit regions in client (device) pixels.</summary>
    public IReadOnlyList<DeviceRegionRect> HitRegions { get; private set; } = [];

    /// <summary>When true the window is excluded from screen capture (SetWindowDisplayAffinity WDA_EXCLUDEFROMCAPTURE).</summary>
    public bool ExcludeFromCapture
    {
        get => _excludeFromCapture;
        set { _excludeFromCapture = value; ApplyDisplayAffinity(); }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = WS_POPUP;
            cp.ExStyle |= WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    public async Task InitializeAsync(string? userDataFolder = null)
    {
        if (_controller is not null) return;
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        var controller = await environment.CreateCoreWebView2CompositionControllerAsync(Handle);
        _controller = controller;
        controller.DefaultBackgroundColor = Color.Transparent;
        controller.Bounds = new Rectangle(Point.Empty, ClientSize);
        controller.IsVisible = true;
        controller.CursorChanged += (_, _) => UpdateCursorIfOverUs();
        controller.RasterizationScaleChanged += (_, _) => ApplyRegions();

        _device = CreateCompositionDevice();
        Check(_device.CreateTargetForHwnd(Handle, true, out _target));
        Check(_device.CreateVisual(out _rootVisual));
        Check(_target.SetRoot(_rootVisual));
        controller.RootVisualTarget = _rootVisual;
        Check(_device.Commit());

        var core = controller.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.NavigationStarting += (_, e) => { if (!IsTrustedUri(e.Uri)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += ReceiveWebMessage;
        core.NavigationCompleted += NavigationCompleted;
        core.Navigate(_origin.AbsoluteUri);
    }

    /// <summary>Serializes and posts a message to the page; queued (bounded) until the page reports ready.</summary>
    public void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, MessageJson);
        if (!IsReady || _controller is null)
        {
            while (_pendingMessages.Count >= MaxPendingMessages) _pendingMessages.Dequeue();
            _pendingMessages.Enqueue(json);
            return;
        }
        PostNow(json);
    }

    /// <summary>Takes keyboard focus (foreground) so a page text input can receive typing. Remembers the previous foreground window.</summary>
    public void ActivateForInput()
    {
        var foreground = GetForegroundWindow();
        if (foreground != Handle) _previousForeground = foreground;
        SetForegroundWindow(Handle);
        _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
    }

    /// <summary>Gives the foreground back to the window that had it before <see cref="ActivateForInput"/>.</summary>
    public void ReleaseInput()
    {
        if (GetForegroundWindow() == Handle && _previousForeground != IntPtr.Zero && IsWindow(_previousForeground))
            SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    /// <summary>Moves the surface to cover another monitor.</summary>
    public void MoveToScreen(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _screenDeviceName = screen.DeviceName;
        FitToScreen();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRegions(); // empty until the page reports: fully click-through and invisible
        ApplyDisplayAffinity();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { } // no redirection bitmap: GDI output is never shown

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_controller is not null) _controller.Bounds = new Rectangle(Point.Empty, ClientSize);
        ApplyRegions();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;
            case WM_SETCURSOR when _controller is not null && (m.LParam.ToInt64() & 0xFFFF) == HTCLIENT:
                SetCursor(_controller.Cursor);
                m.Result = 1;
                return;
            case WM_DPICHANGED:
                FitToScreen(); // the surface always covers its monitor; ignore the suggested rect
                m.Result = IntPtr.Zero;
                return;
            case WM_DISPLAYCHANGE:
                FitToScreen();
                break;
            case >= WM_MOUSEMOVE and <= WM_MOUSEHWHEEL or WM_MOUSELEAVE when _controller is not null:
                ForwardMouse(ref m);
                return;
        }
        base.WndProc(ref m);
    }

    private void ForwardMouse(ref Message m)
    {
        var controller = _controller!;
        var wParam = m.WParam.ToInt64();
        var keys = (CoreWebView2MouseEventVirtualKeys)(wParam & 0xFFFF);
        uint mouseData = 0;
        var point = new POINT { X = (short)(m.LParam.ToInt64() & 0xFFFF), Y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF) };
        switch (m.Msg)
        {
            case WM_MOUSEWHEEL or WM_MOUSEHWHEEL:
                mouseData = unchecked((uint)(short)((wParam >> 16) & 0xFFFF));
                ScreenToClient(Handle, ref point); // wheel coordinates are in screen space
                break;
            case >= WM_XBUTTONDOWN and <= WM_XBUTTONDBLCLK:
                mouseData = (uint)((wParam >> 16) & 0xFFFF);
                break;
            case WM_MOUSELEAVE:
                _trackingLeave = false;
                point = default;
                break;
            case WM_MOUSEMOVE when !_trackingLeave:
                var tme = new TRACKMOUSEEVENT { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = Handle };
                _trackingLeave = TrackMouseEvent(ref tme);
                break;
        }
        // Keep receiving moves/ups while a button is held, even outside the hit region.
        if (m.Msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN) SetCapture(Handle);
        else if (m.Msg is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONDOWN + 1
                 && (keys & (CoreWebView2MouseEventVirtualKeys.LeftButton | CoreWebView2MouseEventVirtualKeys.RightButton
                     | CoreWebView2MouseEventVirtualKeys.MiddleButton | CoreWebView2MouseEventVirtualKeys.XButton1
                     | CoreWebView2MouseEventVirtualKeys.XButton2)) == 0
                 && GetCapture() == Handle) ReleaseCapture();
        controller.SendMouseInput((CoreWebView2MouseEventKind)m.Msg, keys, mouseData, new Point(point.X, point.Y));
        m.Result = IntPtr.Zero;
    }

    private void UpdateCursorIfOverUs()
    {
        if (_controller is not null && GetCursorPos(out var point) && WindowFromPoint(point) == Handle) SetCursor(_controller.Cursor);
    }

    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _controller is null || !IsTrustedUri(_controller.CoreWebView2.Source)) return;
        _ready = true;
        _frontendReady = false;
        SetRegions([]); // a fresh document starts click-through until it reports its own regions
        PostNow(JsonSerializer.Serialize(new { kind = "session.bootstrap", token = _sessionToken }));
    }

    private void ReceiveWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!_ready || !Uri.TryCreate(e.Source, UriKind.Absolute, out var source) || !SameOrigin(source, _origin)) return;
        var json = e.WebMessageAsJson;
        if (json.Length > MaxMessageLength) return;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kindValue)
                || kindValue.ValueKind != JsonValueKind.String
                || kindValue.GetString() is not { } kind
                || !_allowedKinds.Contains(kind)) return;
            switch (kind)
            {
                case "bootstrap.request":
                    PostNow(JsonSerializer.Serialize(new { kind = "session.bootstrap", token = _sessionToken }));
                    return;
                case "surface.regions":
                    SetRegions(SurfaceRegions.Parse(root));
                    return;
                case "surface.focus":
                    if (root.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True) ActivateForInput();
                    else ReleaseInput();
                    return;
                case "ready":
                    if (_frontendReady) return;
                    _frontendReady = true;
                    while (_pendingMessages.TryDequeue(out var pending)) PostNow(pending);
                    PageReady?.Invoke(this, EventArgs.Empty);
                    break;
            }
            PageMessage?.Invoke(this, new ShellSurfaceMessageEventArgs(kind, json));
        }
        catch (JsonException) { }
    }

    private void SetRegions(IReadOnlyList<CssRegionRect> regions)
    {
        _cssRegions = regions;
        ApplyRegions();
    }

    private void ApplyRegions()
    {
        if (!IsHandleCreated) return;
        var scale = _controller?.RasterizationScale ?? DeviceDpi / 96.0;
        var rects = SurfaceRegions.ToDevice(_cssRegions, scale, ClientSize.Width, ClientSize.Height);
        var region = CreateRectRgn(0, 0, 0, 0);
        foreach (var rect in rects)
        {
            var part = rect.Radius > 0
                ? CreateRoundRectRgn(rect.X, rect.Y, rect.X + rect.Width + 1, rect.Y + rect.Height + 1, rect.Radius * 2, rect.Radius * 2)
                : CreateRectRgn(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
            CombineRgn(region, region, part, RGN_OR);
            DeleteObject(part);
        }
        // On success the system owns the region handle.
        if (SetWindowRgn(Handle, region, true) == 0) DeleteObject(region);
        HitRegions = rects;
    }

    private void ApplyDisplayAffinity()
    {
        if (IsHandleCreated) SetWindowDisplayAffinity(Handle, _excludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    private Screen CurrentScreen() =>
        Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _screenDeviceName) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];

    private void FitToScreen()
    {
        var bounds = CurrentScreen().Bounds;
        if (Bounds != bounds) Bounds = bounds;
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated) BeginInvoke(FitToScreen);
    }

    private void PostNow(string json) => _controller?.CoreWebView2?.PostWebMessageAsJson(json);
    private bool IsTrustedUri(string raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && SameOrigin(uri, _origin);
    private static bool SameOrigin(Uri left, Uri right) => left.Scheme == right.Scheme && left.Host == right.Host && left.Port == right.Port;
    private static void Check(int hresult) => System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hresult);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            _controller?.Close();
            _controller = null;
            foreach (var com in new object?[] { _rootVisual, _target, _device })
                if (com is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(com);
            _rootVisual = null; _target = null; _device = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class ShellSurfaceMessageEventArgs(string kind, string json) : EventArgs
{
    public string Kind { get; } = kind;
    public string Json { get; } = json;
}
