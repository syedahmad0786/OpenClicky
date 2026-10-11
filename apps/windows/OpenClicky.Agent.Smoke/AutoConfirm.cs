using OpenClicky.Core;

namespace OpenClicky.Agent.Smoke;

/// <summary>
/// Test-only confirm that always allows mutating actions.
/// Must never be wired into the tray app — production uses WinFormsAgentConfirm.
/// </summary>
internal sealed class AutoConfirm : IAgentConfirm
{
    public int ConfirmCalls { get; private set; }

    public Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default)
    {
        ConfirmCalls++;
        Console.WriteLine($"auto_confirm #{ConfirmCalls}: {summary}");
        return Task.FromResult(true);
    }
}
