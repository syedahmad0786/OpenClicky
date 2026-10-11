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
        Action<string>? onReply = null)
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
    }

    public async Task RunAsync(string task, string model, CancellationToken cancellationToken = default)
    {
        var prior = new List<string>();
        _overlay.ShowProcessing();
        _log($"agent_start task_chars={task.Length}");

        for (var step = 1; step <= _maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Hide buddy so it does not pollute the vision / UIA capture.
            _overlay.Hide();
            var screens = await Task.Run(() => _capture.CaptureAllMonitorsJpegAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            _overlay.ShowProcessing();
            var plan = await _planner.NextStepAsync(task, screens, prior, model, cancellationToken).ConfigureAwait(false);
            plan = plan with { ToolName = plan.ToolName.Trim().ToLowerInvariant() };
            if (plan.ToolName is not ("click" or "type_text" or "hotkey" or "scroll" or "wait" or "uia_snapshot" or "screenshot" or "done"))
                throw new FormatException("The agent selected an unsupported tool.");
            _log($"agent_step={step} tool={plan.ToolName}");

            if (plan.Done || plan.ToolName.Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                var summary = plan.Arguments.TryGetValue("summary", out var s) && !string.IsNullOrWhiteSpace(s)
                    ? s
                    : (string.IsNullOrWhiteSpace(plan.Rationale) ? "done." : plan.Rationale);
                _overlay.ShowResponding(summary);
                _onReply?.Invoke(summary);
                await _tts.SpeakAsync(summary, cancellationToken).ConfigureAwait(false);
                return;
            }

            var args = ResolveArguments(plan, screens);
            var mutating = plan.ToolName is "click" or "type_text" or "hotkey" or "scroll";
            if (mutating && _foregroundTitle is not null)
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
                    await _tts.SpeakAsync(blocked, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var confirmed = !mutating;
            if (mutating)
            {
                var summary = $"{plan.ToolName}: {FormatArgs(args)} — {plan.Rationale}";
                confirmed = await _confirm.ConfirmAsync(summary, cancellationToken).ConfigureAwait(false);
                if (!confirmed)
                {
                    const string denied = "okay, i stopped the agent.";
                    _overlay.ShowResponding(denied);
                    _onReply?.Invoke(denied);
                    await _tts.SpeakAsync(denied, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (mutating && AgentDenyList.IsDenied(_foregroundTitle?.Invoke(), _denyListExtra?.Invoke()))
            {
                const string changed = "The active window changed to a protected app. I stopped without performing the action.";
                _onReply?.Invoke(changed);
                _overlay.ShowResponding(changed);
                return;
            }

            if (plan.ToolName == "click") _overlay.PointAt(int.Parse(args["x"]), int.Parse(args["y"]), plan.Rationale);

            var request = new ActionRequest(plan.ToolName, args, plan.Rationale);
            var result = await _execute(request, confirmed, cancellationToken).ConfigureAwait(false);
            prior.Add($"step {step}: {plan.ToolName} → success={result.Success} {result.Message}");
            _log($"agent_result success={result.Success} tool={plan.ToolName}");

            if (!result.Success)
            {
                var msg = $"I stopped because {plan.ToolName} failed: {result.Message}";
                _overlay.ShowResponding(msg);
                _onReply?.Invoke(msg);
                await _tts.SpeakAsync(msg, cancellationToken).ConfigureAwait(false);
                return;
            }
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        const string capped = "i hit the step limit for this agent run.";
        _overlay.ShowResponding(capped);
        _onReply?.Invoke(capped);
        await _tts.SpeakAsync(capped, cancellationToken).ConfigureAwait(false);
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
