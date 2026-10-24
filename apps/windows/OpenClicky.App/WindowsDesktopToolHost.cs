using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;
using OpenClicky.Agent;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>Windows binding for the private desktop session; no model has direct input access.</summary>
internal sealed class WindowsDesktopToolHost : IDesktopToolHost
{
    private readonly IScreenCaptureService _capture;
    private readonly IAgentConfirm _confirm;
    private readonly Func<ActionRequest, bool, CancellationToken, Task<ActionResult>> _execute;
    private readonly Func<CancellationToken, Task> _beforeTool;
    private readonly Func<string> _steering;
    private readonly Action<AgentProgress> _progress;
    private readonly Action _hideOverlay;
    private readonly IReadOnlyList<string> _excluded;
    private readonly UiaSnapshotService _uia = new();
    private string? _fingerprint;
    private long _revision;
    private int _step;

    public WindowsDesktopToolHost(IScreenCaptureService capture, IAgentConfirm confirm,
        Func<ActionRequest, bool, CancellationToken, Task<ActionResult>> execute,
        Func<CancellationToken, Task> beforeTool, Func<string> steering, Action<AgentProgress> progress,
        Action hideOverlay, IReadOnlyList<string> excluded)
    {
        _capture = capture; _confirm = confirm; _execute = execute; _beforeTool = beforeTool;
        _steering = steering; _progress = progress; _hideOverlay = hideOverlay; _excluded = excluded;
    }

    public async Task<DesktopToolContext> GetCurrentContextAsync(CancellationToken token)
    {
        await _beforeTool(token).ConfigureAwait(false);
        var state = ReadContext();
        return new(state.Revision, state.Title);
    }

    public async Task<DesktopToolObservation> ObserveAsync(bool includeScreens, bool includeUia, CancellationToken token)
    {
        _progress(new(_step, "observing", includeScreens ? "Inspecting the current app" : "Reading current app controls", includeScreens ? "observe_screen" : "uia"));
        await _beforeTool(token).ConfigureAwait(false);
        _hideOverlay(); // synchronous: the companion and any drawing are hidden when it returns
        if (includeScreens) await Task.Delay(IOverlayController.HideSettleMilliseconds, token).ConfigureAwait(false);
        var state = ReadContext();
        IReadOnlyList<ScreenCaptureResult> screens = [];
        if (includeScreens)
        {
            var image = await _capture.CaptureRegionJpegAsync(state.Bounds, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The current window could not be captured. No broader screen was shared.");
            screens = [image];
        }
        token.ThrowIfCancellationRequested();
        if (ReadContext().Revision != state.Revision)
            throw new InvalidOperationException("The foreground changed during inspection. Observe again before acting.");
        var steering = _steering();
        var observation = new DesktopToolObservation(Guid.NewGuid().ToString("N"), state.Revision, state.Title,
            (includeUia ? state.Uia : "Use uia for current control names, values and virtual-screen coordinates.")
            + (steering.Length == 0 ? "" : "\nUser task steering: " + steering), screens);
        _progress(new(_step, "observing", "Current app inspection completed", includeScreens ? "observe_screen" : "uia"));
        return observation;
    }

    public Task<MemorySearchResponse> SearchMemoryAsync(string query, int limit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _progress(new(_step, "observing", "Searching local memory", "search_memory"));
        var results = LocalSearchStore.Search(query, limit: limit);
        token.ThrowIfCancellationRequested();
        var sources = string.Join(", ", results.Take(10).Select(result => result.Kind + "#" + result.RecordId));
        _progress(new(_step, "observing", results.Count == 0 ? "Local memory search found no matches"
            : $"Local memory search found {results.Count} match(es): {sources}", "search_memory"));
        return Task.FromResult(new MemorySearchResponse(true, results));
    }

    public async Task<bool> ConfirmAsync(ActionRequest request, CancellationToken token)
    {
        _progress(new(_step, "awaiting-approval", request.Rationale ?? request.ToolName, request.ToolName));
        var summary = request.ToolName == "open_app" && !string.IsNullOrWhiteSpace(request.TrustedTargetName)
            ? "Open " + request.TrustedTargetName + Environment.NewLine + request.Rationale
            : request.ToolName + ": " + string.Join(", ", request.Arguments.Select(p => p.Key + "=" + p.Value))
                + Environment.NewLine + request.Rationale;
        return await (request.ToolName is "open_app" or "connector_call" ? _confirm.ConfirmNonInputAsync(summary, token)
            : _confirm.ConfirmAsync(summary, token)).ConfigureAwait(false);
    }

    public async Task<ActionResult> ExecuteAsync(ActionRequest request, bool confirmed, CancellationToken token)
    {
        await _beforeTool(token).ConfigureAwait(false);
        if (request.ToolName == "open_app")
        {
            if (AgentDenyList.IsDenied(ForegroundWindowInfo.GetCompanionTitle(), _excluded))
                return new(false, "The active window is excluded. No app was opened.", false);
            _progress(new(++_step, "executing", request.Rationale ?? request.ToolName, request.ToolName));
            var launch = await _execute(request, confirmed, token).ConfigureAwait(false);
            _progress(new(_step, "observing", launch.Message, request.ToolName));
            await Task.Delay(700, token).ConfigureAwait(false);
            return launch;
        }
        var state = ReadContext();
        if (request.ToolName == "click" && (!int.TryParse(request.Arguments.GetValueOrDefault("x"), out var x)
            || !int.TryParse(request.Arguments.GetValueOrDefault("y"), out var y)
            || x < state.Bounds.X || y < state.Bounds.Y || x >= state.Bounds.X + state.Bounds.Width || y >= state.Bounds.Y + state.Bounds.Height))
            return new(false, "The click is outside the observed foreground window. Inspect the intended window first.", false);
        _progress(new(++_step, "executing", request.Rationale ?? request.ToolName, request.ToolName));
        var result = await _execute(request, confirmed, token).ConfigureAwait(false);
        _progress(new(_step, "observing", result.Message, request.ToolName));
        return result;
    }

    private (long Revision, string Title, string Uia, (int X, int Y, int Width, int Height) Bounds) ReadContext()
    {
        var window = ForegroundWindowInfo.GetHandle();
        var title = ForegroundWindowInfo.GetCompanionTitle();
        if (window == nint.Zero || string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Select the app you want OpenClicky to work in first.");
        if (AgentDenyList.IsDenied(title, _excluded))
            throw new InvalidOperationException("This window is excluded. No screen or accessibility content was read.");
        var focused = AutomationElement.FocusedElement;
        if (focused is not null && focused.Current.IsPassword)
            throw new InvalidOperationException("A password field is focused. No screen or text was read.");
        var bounds = ForegroundWindowInfo.GetBounds();
        if (bounds is not { Width: > 0, Height: > 0 } rectangle)
            throw new InvalidOperationException("The foreground window has no visible area.");
        var uia = _uia.CaptureWindow(window);
        if (ForegroundWindowInfo.GetHandle() != window)
            throw new InvalidOperationException("The foreground changed during inspection. Observe again.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{window}|{rectangle}|{title}|{uia}|{_steering()}")));
        if (_fingerprint != fingerprint) { _fingerprint = fingerprint; _revision++; }
        return (_revision, title, uia, rectangle);
    }
}
