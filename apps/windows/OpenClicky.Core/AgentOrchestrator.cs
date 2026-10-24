using OpenClicky.Core.Agent;

namespace OpenClicky.Core;

/// <summary>
/// Bounded computer-use loop: plan → confirm mutating steps → execute → re-capture.
/// </summary>
public sealed class AgentOrchestrator
{
    private readonly IScreenCaptureService _capture;
    private readonly IAgentPlanner _planner;
    private readonly Func<ActionRequest, bool, CancellationToken, Task<ActionResult>> _execute;
    private readonly IAgentConfirm _confirm;
    private readonly IOverlayController _overlay;
    private readonly ITtsClient _tts;
    private readonly Action<string> _log;
    private readonly Func<string?>? _foregroundTitle;
    private readonly Func<IEnumerable<string>?>? _denyListExtra;
    private readonly int _maxSteps;
    private readonly Action<string>? _onReply;
    private readonly Action<AgentProgress>? _onProgress;
    private readonly Func<CancellationToken, Task>? _beforeStep;
    private readonly AgentAccessMode _access;
    private readonly Func<nint>? _foregroundWindow;
    private readonly Func<long>? _revision;
    private readonly Func<(int X, int Y, int Width, int Height)?>? _foregroundBounds;
    private readonly Func<int, int, Drawing.Box?>? _snapTarget;

    public AgentOrchestrator(
        IScreenCaptureService capture,
        IAgentPlanner planner,
        Func<ActionRequest, bool, CancellationToken, Task<ActionResult>> execute,
        IAgentConfirm confirm,
        IOverlayController overlay,
        ITtsClient tts,
        Action<string>? log = null,
        int maxSteps = 8,
        Func<string?>? foregroundTitle = null,
        Func<IEnumerable<string>?>? denyListExtra = null,
        Action<string>? onReply = null,
        Action<AgentProgress>? onProgress = null,
        Func<CancellationToken, Task>? beforeStep = null,
        AgentAccessMode access = AgentAccessMode.ConfirmEach,
        Func<nint>? foregroundWindow = null, Func<long>? revision = null,
        Func<(int X, int Y, int Width, int Height)?>? foregroundBounds = null,
        Func<int, int, Drawing.Box?>? snapTarget = null)
    {
        _capture = capture;
        _planner = planner;
        _execute = execute;
        _confirm = confirm;
        _overlay = overlay;
        _tts = tts;
        _log = log ?? Console.WriteLine;
        _maxSteps = maxSteps;
        _foregroundTitle = foregroundTitle;
        _denyListExtra = denyListExtra;
        _onReply = onReply;
        _onProgress = onProgress;
        _beforeStep = beforeStep;
        _access = Enum.IsDefined(access) ? access : AgentAccessMode.ConfirmEach;
        _foregroundWindow = foregroundWindow;
        _revision = revision;
        _foregroundBounds = foregroundBounds;
        _snapTarget = snapTarget;
    }

    public async Task RunAsync(string task, string model, CancellationToken cancellationToken = default)
    {
        var prior = new List<string>();
        _overlay.ShowProcessing();
        _log($"agent_start task_chars={task.Length}");
        Progress(0, "started", "Task started");

        for (var step = 1; step <= _maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_beforeStep is not null) await _beforeStep(cancellationToken).ConfigureAwait(false);
            var capturedTitle = _foregroundTitle?.Invoke();
            if (_foregroundTitle is not null)
            {
                var extras = _denyListExtra?.Invoke();
                if (AgentDenyList.IsDenied(capturedTitle, extras))
                {
                    var reason = AgentDenyList.MatchReason(capturedTitle, extras) ?? "sensitive window";
                    var blocked = $"i stopped before looking because the foreground window is protected ({reason}). switch away and try again.";
                    _log("agent_denied protected_window_before_capture");
                    _overlay.ShowResponding(blocked);
                    _onReply?.Invoke(blocked);
                    Progress(step, "stopped", blocked);
                    await SpeakReplyAsync(blocked, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            Progress(step, "planning", "Looking at the current screen");
            // Hide the companion (and any drawing) so it does not pollute the vision / UIA capture; let DWM drop it first.
            _overlay.Hide();
            await Task.Delay(IOverlayController.HideSettleMilliseconds, cancellationToken).ConfigureAwait(false);
            var capturedWindow = _foregroundWindow?.Invoke();
            var capturedRevision = _revision?.Invoke();
            var capturedBounds = _foregroundBounds?.Invoke();
            IReadOnlyList<ScreenCaptureResult> screens;
            if (_foregroundBounds is not null)
            {
                if (capturedBounds is not { Width: > 0, Height: > 0 } region)
                    throw new InvalidOperationException("The foreground window has no visible area.");
                var screen = await Task.Run(() => _capture.CaptureRegionJpegAsync(region, cancellationToken), cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The current window could not be captured.");
                screens = [screen];
            }
            else
            {
                screens = await Task.Run(() => _capture.CaptureAllMonitorsJpegAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            _overlay.ShowProcessing();
            var afterCaptureTitle = _foregroundTitle?.Invoke();
            var afterCaptureExclusions = _denyListExtra?.Invoke();
            if (_foregroundTitle is not null && AgentDenyList.IsDenied(afterCaptureTitle, afterCaptureExclusions))
            {
                var reason = AgentDenyList.MatchReason(afterCaptureTitle, afterCaptureExclusions) ?? "sensitive window";
                var blocked = $"i stopped because the foreground changed to a protected window ({reason}). captured pixels were discarded.";
                _log("agent_denied protected_window_after_capture");
                _overlay.ShowResponding(blocked);
                _onReply?.Invoke(blocked);
                Progress(step, "stopped", blocked);
                await SpeakReplyAsync(blocked, cancellationToken).ConfigureAwait(false);
                return;
            }
            if ((_foregroundTitle is not null && capturedTitle != afterCaptureTitle)
                || (capturedWindow is { } windowAfterCapture && _foregroundWindow?.Invoke() != windowAfterCapture)
                || (capturedRevision is { } revisionAfterCapture && _revision?.Invoke() != revisionAfterCapture)
                || (_foregroundBounds is not null && capturedBounds != _foregroundBounds()))
            {
                prior.Add("The foreground window changed during capture. Discard the captured pixels and inspect again.");
                Progress(step, "observing", "Context changed during capture; discarding it");
                continue;
            }
            var plan = await _planner.NextStepAsync(task, screens, prior, model, cancellationToken).ConfigureAwait(false);
            plan = plan with { ToolName = plan.ToolName.Trim().ToLowerInvariant() };
            if (plan.ToolName is not ("click" or "type_text" or "hotkey" or "scroll" or "wait" or "uia_snapshot" or "screenshot" or "list_apps" or "open_app" or "done"))
                throw new FormatException("The agent selected an unsupported tool.");
            _log($"agent_step={step} tool={plan.ToolName}");
            if (_beforeStep is not null) await _beforeStep(cancellationToken).ConfigureAwait(false);
            var afterPlanningTitle = _foregroundTitle?.Invoke();
            var afterPlanningExclusions = _denyListExtra?.Invoke();
            if (_foregroundTitle is not null && AgentDenyList.IsDenied(afterPlanningTitle, afterPlanningExclusions))
            {
                var reason = AgentDenyList.MatchReason(afterPlanningTitle, afterPlanningExclusions) ?? "sensitive window";
                var blocked = $"i stopped because the foreground changed to a protected window ({reason}).";
                _log("agent_denied protected_window_after_planning");
                _overlay.ShowResponding(blocked);
                _onReply?.Invoke(blocked);
                Progress(step, "stopped", blocked, plan.ToolName);
                await SpeakReplyAsync(blocked, cancellationToken).ConfigureAwait(false);
                return;
            }
            if ((_foregroundTitle is not null && capturedTitle != afterPlanningTitle)
                || (capturedWindow is { } plannedWindow && _foregroundWindow?.Invoke() != plannedWindow)
                || (capturedRevision is { } plannedRevision && _revision?.Invoke() != plannedRevision)
                || (_foregroundBounds is not null && capturedBounds != _foregroundBounds()))
            {
                _log($"agent_context_changed planned_window={capturedWindow} current_window={_foregroundWindow?.Invoke()} planned_revision={capturedRevision} current_revision={_revision?.Invoke()}");
                prior.Add("The foreground window or user guidance changed during planning. Discard the old plan and inspect again.");
                Progress(step, "observing", "Context changed; making a fresh plan", plan.ToolName);
                continue;
            }

            if (plan.Done || plan.ToolName.Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                var summary = plan.Arguments.TryGetValue("summary", out var s) && !string.IsNullOrWhiteSpace(s)
                    ? s
                    : (string.IsNullOrWhiteSpace(plan.Rationale) ? "done." : plan.Rationale);
                _overlay.ShowResponding(summary);
                _onReply?.Invoke(summary);
                Progress(step, "completed", summary, "done");
                await SpeakReplyAsync(summary, cancellationToken).ConfigureAwait(false);
                return;
            }

            var args = ResolveArguments(plan, screens);
            var mutating = plan.ToolName is "click" or "type_text" or "hotkey" or "scroll" or "open_app";
            var sendsForegroundInput = plan.ToolName is "click" or "type_text" or "hotkey" or "scroll";
            if (_foregroundTitle is not null)
            {
                var title = _foregroundTitle();
                var extras = _denyListExtra?.Invoke();
                if (AgentDenyList.IsDenied(title, extras))
                {
                    var reason = AgentDenyList.MatchReason(title, extras) ?? "sensitive window";
                    var blocked =
                        $"i stopped because the foreground window looks sensitive ({reason}). switch away and try again.";
                    _log("agent_denied protected_window");
                    _overlay.ShowResponding(blocked);
                    _onReply?.Invoke(blocked);
                    Progress(step, "stopped", blocked, plan.ToolName);
                    await SpeakReplyAsync(blocked, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var confirmed = !mutating;
            var request = new ActionRequest(plan.ToolName, args, plan.Rationale);
            Progress(step, "proposed", plan.Rationale, plan.ToolName);
            if (mutating && _access == AgentAccessMode.Preview)
            {
                const string preview = "Preview only: no desktop input was performed. Choose an execution mode to run this task.";
                _onReply?.Invoke(preview);
                Progress(step, "stopped", preview, plan.ToolName);
                return;
            }
            if (mutating)
            {
                var summary = $"{plan.ToolName}: {FormatArgs(args)} — {plan.Rationale}";
                if (AgentAccessPolicy.RequiresConfirmation(_access, request))
                {
                    Progress(step, "awaiting-approval", plan.Rationale, plan.ToolName);
                    confirmed = await (plan.ToolName == "open_app"
                        ? _confirm.ConfirmNonInputAsync(summary, cancellationToken)
                        : _confirm.ConfirmAsync(summary, cancellationToken)).ConfigureAwait(false);
                }
                else confirmed = true;
                if (!confirmed)
                {
                    const string denied = "okay, i stopped the agent.";
                    _overlay.ShowResponding(denied);
                    _onReply?.Invoke(denied);
                    Progress(step, "stopped", denied, plan.ToolName);
                    await SpeakReplyAsync(denied, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (plan.ToolName == "click")
            {
                int x = int.Parse(args["x"]), y = int.Parse(args["y"]);
                Drawing.Box? frame = null;
                try { frame = _snapTarget?.Invoke(x, y); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { _log($"snap_error={ex.GetType().Name}"); }
                _overlay.PointAt(x, y, plan.Rationale, null, frame);
                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            }
            if (_beforeStep is not null) await _beforeStep(cancellationToken).ConfigureAwait(false);
            if (mutating && ((sendsForegroundInput && capturedWindow is { } expectedWindow && _foregroundWindow?.Invoke() != expectedWindow)
                || (capturedRevision is { } expectedRevision && _revision?.Invoke() != expectedRevision)))
            {
                prior.Add("The foreground window changed before input. No action was performed; inspect the new screen.");
                Progress(step, "observing", "Window changed; discarding the old action", plan.ToolName);
                continue;
            }
            if (mutating && AgentDenyList.IsDenied(_foregroundTitle?.Invoke(), _denyListExtra?.Invoke()))
            {
                const string changed = "The active window changed to a protected app. I stopped without performing the action.";
                _onReply?.Invoke(changed);
                _overlay.ShowResponding(changed);
                Progress(step, "stopped", changed, plan.ToolName);
                return;
            }

            Progress(step, "executing", plan.Rationale, plan.ToolName);
            var result = await _execute(request, confirmed, cancellationToken).ConfigureAwait(false);
            prior.Add($"step {step}: {plan.ToolName} → success={result.Success} {result.Message}");
            _log($"agent_result success={result.Success} tool={plan.ToolName}");

            if (!result.Success)
            {
                var msg = $"I stopped because {plan.ToolName} failed: {result.Message}";
                _overlay.ShowResponding(msg);
                _onReply?.Invoke(msg);
                Progress(step, "failed", msg, plan.ToolName);
                await SpeakReplyAsync(msg, cancellationToken).ConfigureAwait(false);
                return;
            }
            Progress(step, "observing", result.Message, plan.ToolName);
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        const string capped = "i hit the step limit for this agent run.";
        _overlay.ShowResponding(capped);
        _onReply?.Invoke(capped);
        Progress(_maxSteps, "stopped", capped);
        await SpeakReplyAsync(capped, cancellationToken).ConfigureAwait(false);
    }

    private void Progress(int step, string state, string message, string? tool = null)
        => _onProgress?.Invoke(new(step, state, message, tool));

    private async Task SpeakReplyAsync(string message, CancellationToken token)
    {
        try { await _tts.SpeakAsync(message, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (Exception ex) when (!token.IsCancellationRequested)
        { _log("agent_voice_error=" + ex.GetType().Name); _onReply?.Invoke("Spoken narration is unavailable; the task result is in chat."); }
    }

    private static string FormatArgs(IReadOnlyDictionary<string, string> args) =>
        string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}"));

    private static Dictionary<string, string> ResolveArguments(AgentPlanStep plan, IReadOnlyList<ScreenCaptureResult> screens)
    {
        var args = new Dictionary<string, string>(plan.Arguments, StringComparer.OrdinalIgnoreCase);
        if (plan.ToolName != "click") return args;
        if (!args.TryGetValue("x", out var xs) || !args.TryGetValue("y", out var ys)
            || !double.TryParse(xs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(ys, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
            || !double.IsFinite(x) || !double.IsFinite(y)) throw new FormatException("Invalid click coordinates.");
        var space = args.GetValueOrDefault("space", "screenshot");
        (int virtualX, int virtualY)? mapped;
        if (space.Equals("virtual", StringComparison.OrdinalIgnoreCase))
        {
            if (x != Math.Truncate(x) || y != Math.Truncate(y)
                || !screens.Any(s => x >= s.MonitorLeft && y >= s.MonitorTop && x < (long)s.MonitorLeft + s.MonitorWidth && y < (long)s.MonitorTop + s.MonitorHeight))
                throw new FormatException("The click target is outside the current monitors.");
            mapped = ((int)x, (int)y);
        }
        else if (space.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
        {
            int? screen = null;
            if (args.TryGetValue("screen", out var raw))
            {
                if (!int.TryParse(raw, out var n)) throw new FormatException("Invalid screen number.");
                screen = n;
            }
            mapped = CoordinateMapper.ToVirtualScreen(new PointingParseResult("", x, y, null, screen, false), screens);
        }
        else throw new FormatException("Unknown click coordinate space.");
        if (mapped is not { } point) throw new FormatException("The click target is outside the screenshot.");
        args["x"] = point.virtualX.ToString(System.Globalization.CultureInfo.InvariantCulture);
        args["y"] = point.virtualY.ToString(System.Globalization.CultureInfo.InvariantCulture);
        args["space"] = "virtual";
        return args;
    }
}
