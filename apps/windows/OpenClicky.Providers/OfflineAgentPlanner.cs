using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// Opt-in offline Agent planner fixture for local smoke (no network / no API keys).
/// Activated when <c>OPENCLICKY_OFFLINE_AGENT=1</c> (see <see cref="ProviderFactory"/>).
/// Types <see cref="SafeTypeText"/> into a user-selected test field; it never launches a shell.
/// Confirmations still go through the tray's <c>WinFormsAgentConfirm</c> — this planner never auto-confirms.
/// </summary>
public sealed class OfflineAgentPlanner : IAgentPlanner
{
    public const string EnvVarName = "OPENCLICKY_OFFLINE_AGENT";

    /// <summary>Single word typed into the selected test field during the offline plan.</summary>
    public const string SafeTypeText = "hello";

    public static bool IsEnvEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fixed plan steps indexed by prior-result count (one step per orchestrator loop).
    /// The typing step still requires human approval in the normal tray path.
    /// </summary>
    public static IReadOnlyList<AgentPlanStep> FixedPlan { get; } =
    [
        new AgentPlanStep(
            "type_text",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["text"] = SafeTypeText },
            "Offline test: type hello into the selected test field",
            Done: false),
        new AgentPlanStep(
            "done",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["summary"] = "The offline test sent hello to your selected field. Check the actual editor to confirm the result; this was not a live AI task.",
            },
            "Offline input test finished; the editor still needs visual verification",
            Done: true),
    ];

    public Task<AgentPlanStep> NextStepAsync(
        string task,
        IReadOnlyList<ScreenCaptureResult> screens,
        IReadOnlyList<string> priorResults,
        string model,
        CancellationToken cancellationToken = default)
    {
        _ = task;
        _ = screens;
        _ = model;
        cancellationToken.ThrowIfCancellationRequested();

        var index = priorResults.Count;
        if (index >= FixedPlan.Count)
        {
            return Task.FromResult(FixedPlan[^1]);
        }

        return Task.FromResult(FixedPlan[index]);
    }
}
