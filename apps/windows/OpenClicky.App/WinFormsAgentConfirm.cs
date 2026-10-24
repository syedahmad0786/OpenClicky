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
    private readonly Func<(IntPtr window, IntPtr focus)?>? _testTarget;
    private volatile bool _isDialogOpen;
    public bool IsDialogOpen => _isDialogOpen;

    public WinFormsAgentConfirm(SynchronizationContext uiContext, Func<(IntPtr window, IntPtr focus)?>? testTarget = null)
    {
        _ui = uiContext;
        _testTarget = testTarget;
    }

    public Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default)
        => ConfirmCoreAsync(summary, Wording.Action, true, cancellationToken);

    public Task<bool> ConfirmNonInputAsync(string summary, CancellationToken cancellationToken = default)
        => ConfirmCoreAsync(summary, Wording.Action, false, cancellationToken);

    public Task<bool> ConfirmTaskAsync(string summary, CancellationToken cancellationToken = default)
        => ConfirmCoreAsync(summary, Wording.Task, true, cancellationToken);

    /// <summary>A talk reply's OPEN or TYPE: declining only skips that action. Restore the target when typing follows.</summary>
    public Task<bool> ConfirmTalkAsync(string summary, bool restoreTarget, CancellationToken cancellationToken = default)
        => ConfirmCoreAsync(summary, Wording.Talk, restoreTarget, cancellationToken);

    private enum Wording { Action, Task, Talk }

    private Task<bool> ConfirmCoreAsync(string summary, Wording wording, bool restoreTarget, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Show()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            var test = _testTarget?.Invoke();
            var prior = test?.window ?? ForegroundFocusRestorer.Capture();
            var focus = test?.focus ?? ForegroundFocusRestorer.CaptureFocus(prior);
            _isDialogOpen = true;
            try
            {
                using var dialog = new AgentConfirmDialog(summary, wording);
                using var registration = cancellationToken.Register(() => _ui.Post(_ =>
                {
                    if (!dialog.IsDisposed) dialog.Close();
                }, null));
                var result = dialog.ShowDialog();
                // Form is closed/disposed; yield UI messages before restoring focus.
                Application.DoEvents();
                var restored = !restoreTarget || ForegroundFocusRestorer.Restore(prior, test is not null, focus);
                if (cancellationToken.IsCancellationRequested) tcs.TrySetCanceled(cancellationToken);
                else if (result == DialogResult.Yes && !restored)
                    tcs.TrySetException(new InvalidOperationException("I couldn't restore the target app and field. Select the app you want to use, then try again."));
                else tcs.TrySetResult(result == DialogResult.Yes);
                _isDialogOpen = false;
            }
            catch (Exception ex)
            {
                try
                {
                    if (restoreTarget) ForegroundFocusRestorer.Restore(prior, test is not null, focus);
                }
                catch
                {
                    // best-effort restore on failure path
                }

                _isDialogOpen = false;
                tcs.TrySetException(ex);
            }
        }

        _ui.Post(_ => Show(), null);
        return tcs.Task;
    }

    /// <summary>Top-most Yes/No form that fully closes before SendInput continues.</summary>
    private sealed class AgentConfirmDialog : Form
    {
        public AgentConfirmDialog(string summary, Wording wording)
        {
            var wholeTask = wording == Wording.Task;
            var talk = wording == Wording.Talk;
            Text = talk ? "OpenClicky" : "OpenClicky Agent";
            AppTheme.Apply(this);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(540, 340);
            Padding = new Padding(24);

            var label = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = AppTheme.Background, ForeColor = AppTheme.Ink,
                Text = (talk ? "Do this?" : wholeTask ? "Approve this task plan?" : "Allow this one action?") + "\r\n\r\n" + summary
                    + (talk ? "\r\n\r\nNothing happens if you decline." : "\r\n\r\nThe task stops if you decline or press Escape."),
                AccessibleName = "Proposed desktop action",
            };

            var yes = AppTheme.Button(talk ? "Allow" : wholeTask ? "Approve task" : "Allow action", true);
            yes.DialogResult = DialogResult.Yes;
            var no = AppTheme.Button(talk ? "Don't" : "Stop task");
            no.DialogResult = DialogResult.No;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0,12,0,0) };
            buttons.Controls.AddRange(new Control[] { no, yes });
            AcceptButton = no;
            CancelButton = no;
            Controls.Add(label);
            Controls.Add(buttons);
            // Default to No (same as MessageBoxDefaultButton.Button2).
            ActiveControl = no;
            ResumeLayout(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // Drop TopMost so it cannot keep stealing activation after close.
            TopMost = false;
        }
    }
}
