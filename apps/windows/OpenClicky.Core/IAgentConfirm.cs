namespace OpenClicky.Core;

public interface IAgentConfirm
{
    /// <summary>Ask the user to approve one mutating action. Runs on a UI-capable thread.</summary>
    Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default);
}
