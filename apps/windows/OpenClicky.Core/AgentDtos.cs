namespace OpenClicky.Core.Agent;

public sealed record ActionRequest(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments,
    string? Rationale,
    string? TrustedTargetName = null);

public sealed record ActionResult(
    bool Success,
    string Message,
    bool RequiresConfirmation);
