namespace OpenClicky.Core;

public sealed record AgentProgress(
    int Step,
    string Status,
    string Message,
    string? Tool = null);

public sealed record TaskRunRecord(
    string Id,
    string Task,
    DateTimeOffset StartedAt,
    string Status,
    List<AgentProgress> Events,
    string AccessMode);

public static class AgentProgressStates
{
    public const string Started = "started";
    public const string Planning = "planning";
    public const string Proposed = "proposed";
    public const string AwaitingApproval = "awaiting-approval";
    public const string Executing = "executing";
    public const string Observing = "observing";
    public const string Completed = "completed";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
    public const string Paused = "paused";
}
