using OpenClicky.Core;

namespace OpenClicky.Agent.Smoke;

/// <summary>
/// Mirrors the tray confirm → execute gate used by <c>AgentOrchestrator</c>,
/// without LLM planning or overlay/TTS (so CI can drive click/type against Sandbox).
/// </summary>
internal static class ConfirmGatedRunner
{
    private static readonly HashSet<string> Mutating = new(StringComparer.OrdinalIgnoreCase)
    {
        AgentToolNames.Click,
        AgentToolNames.TypeText,
        AgentToolNames.Hotkey,
        AgentToolNames.Scroll,
    };

    public static async Task<AgentActionResult> RunAsync(
        IAgentConfirm confirm,
        IAgentActionExecutor executor,
        AgentActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var mutating = Mutating.Contains(request.ToolName);
        var confirmed = !mutating;
        if (mutating)
        {
            var summary = $"{request.ToolName}: {FormatArgs(request.Arguments)} — {request.Rationale}";
            confirmed = await confirm.ConfirmAsync(summary, cancellationToken).ConfigureAwait(false);
            if (!confirmed)
            {
                return new AgentActionResult(false, "user denied", RequiresConfirmation: true);
            }
        }

        return await executor.ExecuteAsync(request, confirmed, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatArgs(IReadOnlyDictionary<string, string> args) =>
        string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}"));
}
