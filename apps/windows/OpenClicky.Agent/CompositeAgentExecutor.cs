namespace OpenClicky.Agent;

/// <summary>Routes tools to UIA snapshot or SendInput backends.</summary>
public sealed class CompositeAgentExecutor : IAgentActionExecutor
{
    private readonly SendInputExecutor _input = new();
    private readonly UiaSnapshotService _uia = new();

    public Task<AgentActionResult> ExecuteAsync(
        AgentActionRequest request,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (request.ToolName.Equals(AgentToolNames.UiaSnapshot, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var max = 80;
                if (request.Arguments.TryGetValue("max_nodes", out var raw) && int.TryParse(raw, out var parsed))
                {
                    max = Math.Clamp(parsed, 10, 200);
                }

                var snapshot = _uia.CaptureForeground(max);
                return Task.FromResult(new AgentActionResult(true, snapshot, RequiresConfirmation: false));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AgentActionResult(false, $"uia_snapshot failed: {ex.Message}", false));
            }
        }

        if (request.ToolName.Equals(AgentToolNames.Screenshot, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new AgentActionResult(
                true,
                "screenshot is provided automatically each agent step by the orchestrator.",
                false));
        }

        return _input.ExecuteAsync(request, userConfirmed, cancellationToken);
    }
}
