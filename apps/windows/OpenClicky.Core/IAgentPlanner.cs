namespace OpenClicky.Core;

public sealed record AgentPlanStep(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments,
    string Rationale,
    bool Done);

public interface IAgentPlanner
{
    Task<AgentPlanStep> NextStepAsync(
        string task,
        IReadOnlyList<ScreenCaptureResult> screens,
        IReadOnlyList<string> priorResults,
        string model,
        CancellationToken cancellationToken = default);
}
