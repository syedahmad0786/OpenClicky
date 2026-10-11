namespace OpenClicky.Sandbox;

/// <summary>
/// Tiny playground for Agent demos: click a button, type into a field, verify Confirm dialogs.
/// Run beside OpenClicky and say: openclicky agent click the green button then type hello.
/// </summary>
internal sealed class SandboxForm : Form
{
    private readonly TextBox _input;
    private readonly Label _status;
    private int _clicks;
    private Button _clickMe = null!;

    public Point ButtonCenter => _clickMe.PointToScreen(new Point(_clickMe.Width / 2, _clickMe.Height / 2));
    public Point InputCenter => _input.PointToScreen(new Point(_input.Width / 2, _input.Height / 2));
    public bool HasVerifiedResult => _clicks > 0 && _input.Text == "hello";

    public SandboxForm()
    {
        Text = "OpenClicky Agent Sandbox";
        ClientSize = new Size(480, 280);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        Controls.Add(new Label
        {
            Left = 16,
            Top = 16,
            Width = 440,
            Height = 40,
            Text = "Use OpenClicky Agent against this window. Confirm each step in the tray app.",
        });

        _status = new Label
        {
            Left = 16,
            Top = 200,
            Width = 440,
            Height = 48,
            Text = "Ready.",
            AccessibleName = "Sandbox status",
        };

        var clickMe = new Button
        {
            Left = 16,
            Top = 72,
            Width = 160,
            Height = 40,
            Text = "Green button",
            AccessibleName = "Green button",
            BackColor = Color.FromArgb(56, 168, 90),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        clickMe.Click += (_, _) =>
        {
            _clicks++;
            _status.Text = $"Green button clicked {_clicks} time(s).";
            _status.AccessibleName = _status.Text;
        };
        _clickMe = clickMe;
        Controls.Add(clickMe);

        Controls.Add(new Label { Left = 16, Top = 130, Width = 200, Text = "Type target:" });
        _input = new TextBox
        {
            Left = 16,
            Top = 152,
            Width = 440,
            Height = 28,
            AccessibleName = "Type target",
        };
        Controls.Add(_input);

        var clear = new Button { Left = 200, Top = 72, Width = 120, Height = 40, Text = "Clear field" };
        clear.Click += (_, _) =>
        {
            _input.Clear();
            _status.Text = "Field cleared.";
        };
        Controls.Add(clear);
        Controls.Add(_status);
    }
}
