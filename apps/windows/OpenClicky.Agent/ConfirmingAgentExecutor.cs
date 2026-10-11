namespace OpenClicky.Agent;

/// <summary>
/// Phase D stub: every mutating tool requires confirmation. Concrete UIA/SendInput lands next.
/// </summary>
public sealed class ConfirmingAgentExecutor : IAgentActionExecutor
{
    private static readonly HashSet<string> Mutating = new(StringComparer.OrdinalIgnoreCase)
    {
        AgentToolNames.Click,
        AgentToolNames.TypeText,
        AgentToolNames.Hotkey,
        AgentToolNames.Scroll,
    };

    public Task<AgentActionResult> ExecuteAsync(
        AgentActionRequest request,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (Mutating.Contains(request.ToolName) && !userConfirmed)
        {
            return Task.FromResult(new AgentActionResult(
                Success: false,
                Message: $"Confirmation required for {request.ToolName}",
                RequiresConfirmation: true));
        }

        // Placeholder until UIA/SendInput backends are wired.
        return Task.FromResult(new AgentActionResult(
            Success: false,
            Message: $"Tool '{request.ToolName}' not implemented yet (Phase D).",
            RequiresConfirmation: false));
    }
}
