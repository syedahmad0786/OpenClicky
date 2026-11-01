namespace OpenClicky.Core;

public interface IAgentConfirm
{
    /// <summary>Ask the user to approve one mutating action. Runs on a UI-capable thread.</summary>
    Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default);

    /// <summary>Approve an action that does not send input to the current foreground control.</summary>
    Task<bool> ConfirmNonInputAsync(string summary, CancellationToken cancellationToken = default) =>
        ConfirmAsync(summary, cancellationToken);
}
