using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OpenClicky.App;

/// <summary>Hosts the packaged companion workspace and exposes its bounded native message contract.</summary>
internal sealed class VisualWorkspacePanel : UserControl
{
    private const int MaxMessageLength = 128_000;
    private const int MaxPendingMessages = 256;
    private static readonly JsonSerializerOptions MessageJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static readonly HashSet<string> UiMessageKinds =
    [
        "bootstrap.request", "ready", "run.cancel", "artifact.changed", "artifact.save", "approval.request", "desktop.action"
    ];

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, AccessibleName = "OpenClicky interactive workspace" };
    private readonly Uri _origin;
    private readonly string _sessionToken;
    private bool _ready;
    private bool _frontendReady;
    private readonly Queue<string> _pendingMessages = new();

    public VisualWorkspacePanel(Uri origin, string sessionToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsLoopback || origin.Scheme != Uri.UriSchemeHttp || origin.AbsolutePath != "/")
            throw new ArgumentException("The workspace origin must be an HTTP loopback origin without a path.", nameof(origin));
        if (sessionToken.Length < 32) throw new ArgumentException("The workspace session token is too short.", nameof(sessionToken));
        _origin = origin;
        _sessionToken = sessionToken;
        BackColor = Color.White;
        Controls.Add(_webView);
    }

    public event EventHandler<VisualWorkspaceMessageEventArgs>? WorkspaceMessage;
    public event EventHandler? WorkspaceReady;
    public bool IsReady => _ready;

    public async Task InitializeAsync(string? userDataFolder = null)
    {
        if (_webView.CoreWebView2 is not null) return;
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);
        var core = _webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not initialize.");
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.NavigationStarting += (_, eventArgs) =>
        {
            if (!IsTrustedWorkspaceUri(eventArgs.Uri)) eventArgs.Cancel = true;
        };
        core.NewWindowRequested += (_, eventArgs) => eventArgs.Handled = true;
        core.WebMessageReceived += ReceiveWebMessage;
        core.NavigationCompleted += NavigationCompleted;
        core.Navigate(_origin.AbsoluteUri);
    }

    public void PostSessionSnapshot(string sessionId, object[] artifacts, string? progress = null) =>
        Post(new { kind = "session.snapshot", sessionId, artifacts, progress });

    public void PostProgress(string text) => Post(new { kind = "progress", text });
    public void PostMessageDelta(string messageId, string text) => Post(new { kind = "message.delta", messageId, text });
    public void PostMessageComplete(string messageId, string role, string text) => Post(new { kind = "message.complete", messageId, role, text });
    public void ShowArtifact(object artifact) => Post(new { kind = "artifact.show", artifact });
    public void PostApprovalResult(string requestId, bool approved, string? note = null) => Post(new { kind = "approval.result", requestId, approved, note });
    public void PostRuntimeError(string message) => Post(new { kind = "runtime.error", message });
    public void ClearConversation() => Post(new { kind = "message.clear" });
    public void SetPrompt(string text) => Post(new { kind = "composer.prefill", text });

    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess || !IsTrustedWorkspaceUri(_webView.Source.AbsoluteUri)) return;
        _ready = true;
        _frontendReady = false;
        PostNow(JsonSerializer.Serialize(new { kind = "session.bootstrap", token = _sessionToken }));
    }

    private void ReceiveWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!_ready || !Uri.TryCreate(eventArgs.Source, UriKind.Absolute, out var source) || !SameOrigin(source, _origin)) return;
        var json = eventArgs.WebMessageAsJson;
        if (json.Length > MaxMessageLength) return;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 20 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("kind", out var kindValue)
                || kindValue.ValueKind != JsonValueKind.String
                || kindValue.GetString() is not { } kind
                || !UiMessageKinds.Contains(kind)) return;
            if (kind == "bootstrap.request")
            {
                PostNow(JsonSerializer.Serialize(new { kind = "session.bootstrap", token = _sessionToken }));
                return;
            }
            if (kind == "ready")
            {
                if (_frontendReady) return;
                _frontendReady = true;
                FlushPendingMessages();
                WorkspaceReady?.Invoke(this, EventArgs.Empty);
            }
            WorkspaceMessage?.Invoke(this, new VisualWorkspaceMessageEventArgs(kind, json));
        }
        catch (JsonException) { }
    }

    private void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, MessageJson);
        if (!_ready || !_frontendReady || _webView.CoreWebView2 is null)
        {
            while (_pendingMessages.Count >= MaxPendingMessages) _pendingMessages.Dequeue();
            _pendingMessages.Enqueue(json);
            return;
        }
        PostNow(json);
    }

    private void FlushPendingMessages()
    {
        while (_pendingMessages.TryDequeue(out var json)) PostNow(json);
    }

    private void PostNow(string json) => _webView.CoreWebView2?.PostWebMessageAsJson(json);

    private bool IsTrustedWorkspaceUri(string raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && SameOrigin(uri, _origin);
    private static bool SameOrigin(Uri left, Uri right) => left.Scheme == right.Scheme && left.Host == right.Host && left.Port == right.Port;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _webView.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class VisualWorkspaceMessageEventArgs(string kind, string json) : EventArgs
{
    public string Kind { get; } = kind;
    public string Json { get; } = json;
}
