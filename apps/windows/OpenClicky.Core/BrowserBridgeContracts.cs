using OpenClicky.Core.Agent;

namespace OpenClicky.Core;

public sealed record BrowserContext(string TabId, string FrameId, long NavigationRevision, string Origin, string Title);

public sealed record BrowserObservation(
    string Id,
    BrowserContext Context,
    string Document,
    IReadOnlyDictionary<string, string> Links);

public sealed record BrowserAction(string Name, IReadOnlyDictionary<string, string> Arguments, string? Rationale);

public sealed record BrowserActionResult(bool Success, string Message);

/// <summary>The Chrome-side implementation must enforce exclusions before reading page content or acting.</summary>
public interface IChromeBridgeHost
{
    Task<BrowserContext> GetCurrentContextAsync(CancellationToken token);
    Task<BrowserObservation> ObserveAsync(CancellationToken token);
    Task<BrowserActionResult> ExecuteAsync(BrowserAction action, CancellationToken token);
}

/// <summary>Routes browser writes through the application's task-scoped approval broker.</summary>
public interface IChromeActionBroker
{
    Task<bool> ApproveAsync(ActionRequest request, CancellationToken token);
}
