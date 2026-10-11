namespace OpenClicky.Agent;

public interface IAgentActionExecutor
{
    Task<AgentActionResult> ExecuteAsync(AgentActionRequest request, bool userConfirmed, CancellationToken cancellationToken = default);
}
