using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// Opt-in offline Agent planner fixture for local smoke (no network / no API keys).
/// Activated when <c>OPENCLICKY_OFFLINE_AGENT=1</c> (see <see cref="ProviderFactory"/>).
/// Emits a short fixed plan: Run → notepad → type <see cref="SafeTypeText"/>.
/// Confirmations still go through the tray's <c>WinFormsAgentConfirm</c> — this planner never auto-confirms.
/// </summary>
public sealed class OfflineAgentPlanner : IAgentPlanner
{
    public const string EnvVarName = "OPENCLICKY_OFFLINE_AGENT";

    /// <summary>Safe single word typed into Notepad during the offline smoke plan.</summary>
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
    /// Mutating tools: hotkey, type_text — each still requires human Yes/No in the tray path.
    /// </summary>
    public static IReadOnlyList<AgentPlanStep> FixedPlan { get; } =
    [
        new AgentPlanStep(
            "hotkey",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["keys"] = "win+r" },
            "offline smoke: open Run to launch Notepad",
            Done: false),
        new AgentPlanStep(
            "type_text",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["text"] = "notepad" },
            "offline smoke: type notepad in Run",
            Done: false),
        new AgentPlanStep(
            "hotkey",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["keys"] = "enter" },
            "offline smoke: confirm Run and focus Notepad",
            Done: false),
        new AgentPlanStep(
            "wait",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ms"] = "800" },
            "offline smoke: wait for Notepad",
            Done: false),
        new AgentPlanStep(
            "type_text",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["text"] = SafeTypeText },
            "offline smoke: type safe word",
            Done: false),
        new AgentPlanStep(
            "done",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["summary"] = "offline agent smoke: Notepad launched and typed hello.",
            },
            "offline agent smoke complete",
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
