using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class WinFormsAgentConfirm : IAgentConfirm
{
    public Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        void Show()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            var result = MessageBox.Show(
                summary + "\n\nAllow this action?",
                "OpenClicky Agent",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            tcs.TrySetResult(result == DialogResult.Yes);
        }

        if (Application.MessageLoop)
        {
            // Prefer UI thread if available.
            var ctx = SynchronizationContext.Current;
            if (ctx is not null)
            {
                ctx.Post(_ => Show(), null);
            }
            else
            {
                Show();
            }
        }
        else
        {
            Show();
        }

        return tcs.Task;
    }
}
