using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class WinFormsAgentConfirm : IAgentConfirm
{
    private readonly SynchronizationContext _ui;

    public WinFormsAgentConfirm(SynchronizationContext uiContext)
    {
        _ui = uiContext;
    }

    public Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Show()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                var result = MessageBox.Show(
                    summary + "\n\nAllow this action?",
                    "OpenClicky Agent",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                tcs.TrySetResult(result == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        _ui.Post(_ => Show(), null);
        return tcs.Task;
    }
}
