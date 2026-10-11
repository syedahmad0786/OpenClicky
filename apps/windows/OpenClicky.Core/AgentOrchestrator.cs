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
        Func<IEnumerable<string>?>? denyListExtra = null)
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
    }

    public async Task RunAsync(string task, string model, CancellationToken cancellationToken = default)
    {
        var prior = new List<string>();
        _overlay.ShowProcessing();
        _log($"agent_start task={task}");

        for (var step = 1; step <= _maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Hide buddy so it does not pollute the vision / UIA capture.
            _overlay.Hide();
            var screens = await _capture.CaptureAllMonitorsJpegAsync(cancellationToken).ConfigureAwait(false);
            _overlay.ShowProcessing();
            var plan = await _planner.NextStepAsync(task, screens, prior, model, cancellationToken).ConfigureAwait(false);
            _log($"agent_step={step} tool={plan.ToolName} rationale={plan.Rationale}");

            if (plan.Done || plan.ToolName.Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                var summary = plan.Arguments.TryGetValue("summary", out var s) && !string.IsNullOrWhiteSpace(s)
                    ? s
                    : (string.IsNullOrWhiteSpace(plan.Rationale) ? "done." : plan.Rationale);
                _overlay.ShowResponding(summary);
                await _tts.SpeakAsync(summary, cancellationToken).ConfigureAwait(false);
                return;
            }

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
                    _log($"agent_denied title={title}");
                    _overlay.ShowResponding(blocked);
                    await _tts.SpeakAsync(blocked, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var confirmed = !mutating;
            if (mutating)
            {
                var summary = $"{plan.ToolName}: {FormatArgs(plan.Arguments)} — {plan.Rationale}";
                confirmed = await _confirm.ConfirmAsync(summary, cancellationToken).ConfigureAwait(false);
                if (!confirmed)
                {
                    const string denied = "okay, i stopped the agent.";
                    _overlay.ShowResponding(denied);
                    await _tts.SpeakAsync(denied, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var args = new Dictionary<string, string>(plan.Arguments, StringComparer.OrdinalIgnoreCase);
            if (plan.ToolName == "click" && args.ContainsKey("x") && args.ContainsKey("y"))
            {
                var space = args.TryGetValue("space", out var sp) ? sp : "screenshot";
                if (space.Equals("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(args["x"], out var vx) && int.TryParse(args["y"], out var vy))
                    {
                        _overlay.PointAt(vx, vy, plan.Rationale);
                    }
                }
                else
                {
                    var fakePoint = new PointingParseResult(
                        string.Empty,
                        double.TryParse(args["x"], out var x) ? x : null,
                        double.TryParse(args["y"], out var y) ? y : null,
                        null,
                        null,
                        IsNone: false);
                    var mapped = CoordinateMapper.ToVirtualScreen(fakePoint, screens);
                    if (mapped is { } p)
                    {
                        args["x"] = p.virtualX.ToString();
                        args["y"] = p.virtualY.ToString();
                        _overlay.PointAt(p.virtualX, p.virtualY, plan.Rationale);
                    }
                }
            }

            var request = new ActionRequest(plan.ToolName, args, plan.Rationale);
            var result = await _execute(request, confirmed, cancellationToken).ConfigureAwait(false);
            prior.Add($"step {step}: {plan.ToolName} → success={result.Success} {result.Message}");
            _log($"agent_result={result.Message}");

            if (!result.Success && result.RequiresConfirmation)
            {
                const string msg = "action needed confirmation and was not approved.";
                _overlay.ShowResponding(msg);
                await _tts.SpeakAsync(msg, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        const string capped = "i hit the step limit for this agent run.";
        _overlay.ShowResponding(capped);
        await _tts.SpeakAsync(capped, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatArgs(IReadOnlyDictionary<string, string> args) =>
        string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}"));
}
