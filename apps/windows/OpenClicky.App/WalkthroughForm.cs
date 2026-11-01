using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class WalkthroughForm : Form
{
    private readonly Label _heading;
    private readonly TextBox _instruction;
    private readonly Label _status;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _refresh;
    private string _targetTitle = "";

    public WalkthroughForm(Func<WalkthroughMove, Task> move, Action stop)
    {
        AppTheme.Apply(this);
        Text = "OpenClicky walkthrough";
        AccessibleName = Text;
        TopMost = true;
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(470, 320);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _heading = AppTheme.Label("Guided walkthrough", 11, true); _heading.Dock = DockStyle.Fill;
        _heading.AutoSize = false; _heading.AutoEllipsis = true; _heading.Margin = Padding.Empty;
        _instruction = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            BorderStyle = BorderStyle.None, BackColor = AppTheme.Background, ForeColor = AppTheme.Ink,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 11) };
        _instruction.AccessibleName = "Walkthrough instruction";
        _status = AppTheme.Label("", 9); _status.Dock = DockStyle.Fill; _status.AutoSize = false;
        _status.Margin = Padding.Empty;
        _status.AccessibleName = "Walkthrough status";
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _back = AppTheme.Button("Back");
        _next = AppTheme.Button("Next", true);
        _refresh = AppTheme.Button("Recheck");
        var stopButton = AppTheme.Button("Stop");
        foreach (var button in new[] { _back, _next, _refresh, stopButton }) { button.AutoSize = false; button.Width = 98; }
        _back.Click += async (_, _) => await move(WalkthroughMove.Back);
        _next.Click += async (_, _) => await move(WalkthroughMove.Next);
        _refresh.Click += async (_, _) => await move(WalkthroughMove.Refresh);
        stopButton.Click += (_, _) => stop();
        actions.Controls.AddRange([_back, _next, _refresh, stopButton]);
        var hint = AppTheme.Label("You perform the actions. Next checks the result. Back does not undo.", 8);
        hint.Dock = DockStyle.Fill; hint.AutoSize = false;
        layout.Controls.Add(_heading); layout.Controls.Add(_instruction); layout.Controls.Add(_status);
        layout.Controls.Add(actions); layout.Controls.Add(hint);
        Controls.Add(layout);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; stop(); } };
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(work.Left + 24, work.Bottom - Height - 24);
        Shown += (_, _) => Location = new Point(work.Left + 24, Math.Max(work.Top + 24, work.Bottom - Height - 24));
        ResumeLayout(true);
    }

    // Keep mouse navigation from changing the application being explained, as the companion dock does.
    protected override bool ShowWithoutActivation => true;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; }
        base.WndProc(ref m);
    }

    public void UpdateStep(WalkthroughView view, string? targetTitle = null)
    {
        if (targetTitle is not null) _targetTitle = targetTitle;
        _heading.Text = (view.Active ? $"Walkthrough · step {Math.Max(1, view.Step)}" : "Walkthrough finished")
            + "\nViewing: " + _targetTitle;
        _instruction.Text = view.Instruction.Length == 0 ? view.Goal : view.Instruction;
        _status.Text = view.Status;
        _back.Enabled = view.Active && !view.Busy && view.CanBack;
        _next.Enabled = view.Active && !view.Busy && view.Step > 0;
        _refresh.Enabled = view.Active && !view.Busy;
    }
}
