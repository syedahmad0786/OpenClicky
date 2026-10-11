using OpenClicky.Agent;
using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>
/// Human Yes/No gate for mutating Agent tools.
/// Uses a short-lived form (not MessageBox) and restores desktop focus before
/// returning so the next SendInput hotkey/type targets the intended window.
/// </summary>
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

            var prior = ForegroundFocusRestorer.Capture();
            try
            {
                using var dialog = new AgentConfirmDialog(summary);
                var result = dialog.ShowDialog();
                // Form is closed/disposed; yield UI messages before restoring focus.
                Application.DoEvents();
                ForegroundFocusRestorer.Restore(prior);
                tcs.TrySetResult(result == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                try
                {
                    ForegroundFocusRestorer.Restore(prior);
                }
                catch
                {
                    // best-effort restore on failure path
                }

                tcs.TrySetException(ex);
            }
        }

        _ui.Post(_ => Show(), null);
        return tcs.Task;
    }

    /// <summary>Top-most Yes/No form that fully closes before SendInput continues.</summary>
    private sealed class AgentConfirmDialog : Form
    {
        public AgentConfirmDialog(string summary)
        {
            Text = "OpenClicky Agent";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(440, 180);
            Padding = new Padding(16);

            var label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 100,
                Text = summary + "\n\nAllow this action?",
            };

            var yes = new Button
            {
                Text = "Yes",
                DialogResult = DialogResult.Yes,
                Width = 96,
                Height = 32,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            var no = new Button
            {
                Text = "No",
                DialogResult = DialogResult.No,
                Width = 96,
                Height = 32,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };

            no.Location = new Point(ClientSize.Width - no.Width - 16, ClientSize.Height - no.Height - 16);
            yes.Location = new Point(no.Left - yes.Width - 8, no.Top);

            AcceptButton = yes;
            CancelButton = no;
            Controls.Add(label);
            Controls.Add(yes);
            Controls.Add(no);
            // Default to No (same as MessageBoxDefaultButton.Button2).
            ActiveControl = no;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // Drop TopMost so it cannot keep stealing activation after close.
            TopMost = false;
        }
    }
}
