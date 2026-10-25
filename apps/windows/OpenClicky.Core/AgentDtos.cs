namespace OpenClicky.Core.Agent;

public sealed record ActionRequest(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments,
    string? Rationale,
    string? TrustedTargetName = null);

/// <param name="ObserveAgain">
/// Nothing was sent and the task was paused (the user took over, or the window refused input): the native agent waits out the
/// pause and inspects the screen again instead of ending the run. The stale action is never replayed.
/// </param>
public sealed record ActionResult(
    bool Success,
    string Message,
    bool RequiresConfirmation,
    bool ObserveAgain = false);
