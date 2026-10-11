using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class HomeForm : Form
{
    private readonly AppSettings _settings;
    private readonly Func<string, bool, Task> _send;
    private readonly Action _stop;
    private readonly Label _connection;
    private readonly Label _status;
    private readonly Label _shortcut;
    private readonly RichTextBox _conversation;
    private readonly TextBox _prompt;
    private readonly Button _sendButton;
    private readonly Button _stopButton;
    private readonly RadioButton _ask;
    private readonly RadioButton _agent;
    private readonly Panel _welcome;
    private readonly Button _demo;
    private bool _busy;

    public HomeForm(AppSettings settings, Func<string, bool, Task> send, Action stop,
        Action openSettings, Func<Task> tryDemo, Func<Task> testAgent)
    {
        _settings = settings;
        _send = send;
        _stop = stop;
        Text = "OpenClicky";
        AppTheme.Apply(this);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(980, 720);
        MinimumSize = new Size(820, 640);
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Tint, Padding = new Padding(22, 28, 22, 22) };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        nav.Controls.Add(new PictureBox { Image = AppTheme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(58, 58), Margin = new Padding(0, 0, 0, 12) });
        nav.Controls.Add(AppTheme.Label("OpenClicky", 19, true));
        nav.Controls.Add(AppTheme.Label("A buddy for your Windows.", 9));
        var home = AppTheme.Button("Your companion", true);
        home.Width = 162;
        home.Click += (_, _) => FocusPrompt();
        nav.Controls.Add(home);
        var settingsButton = AppTheme.Button("Settings");
        settingsButton.Width = 162;
        settingsButton.Click += (_, _) => openSettings();
        nav.Controls.Add(settingsButton);
        _demo = AppTheme.Button("Try pointing demo");
        _demo.Width = 162;
        _demo.Click += async (_, _) => await RunDemoAsync(tryDemo);
        nav.Controls.Add(_demo);
        var sandbox = AppTheme.Button("Test desktop actions");
        sandbox.Width = 162;
        sandbox.Click += async (_, _) => await RunDemoAsync(testAgent);
        nav.Controls.Add(sandbox);
        foreach (var button in nav.Controls.OfType<Button>())
        {
            button.AutoSize = false;
            button.Size = new Size(162, 42);
            button.Font = new Font("Segoe UI", 9);
            button.Padding = new Padding(6, 4, 6, 4);
        }
        sidebar.Controls.Add(nav);
        var foot = AppTheme.Label("Open source.\nYour keys. Your control.\n\nv0.2 · Windows", 9);
        foot.Dock = DockStyle.Bottom;
        sidebar.Controls.Add(foot);
        layout.Controls.Add(sidebar, 0, 0);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 20), RowCount = 4, ColumnCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 208));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(main, 1, 0);

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(AppTheme.Label("Your companion", 19, true));
        _connection = AppTheme.Label("Set up a provider to get started", 9);
        _connection.Top = 38;
        header.Controls.Add(_connection);
        _stopButton = AppTheme.Button("Stop · Esc");
        _stopButton.AutoSize = false;
        _stopButton.Size = new Size(110, 38);
        _stopButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_stopButton);
        header.Resize += (_, _) => _stopButton.Left = header.ClientSize.Width - _stopButton.Width;
        _stopButton.Click += (_, _) => _stop();
        main.Controls.Add(header, 0, 0);

        var surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 14) };
        _conversation = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
            ForeColor = AppTheme.Ink, Font = new Font("Segoe UI", 11), DetectUrls = false,
            AccessibleName = "Conversation", TabStop = true,
        };
        surface.Controls.Add(_conversation);
        _welcome = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var welcomeFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        welcomeFlow.Controls.Add(AppTheme.Label("A little help,\nright where you work.", 25, true));
        welcomeFlow.Controls.Add(AppTheme.Label("Ask about your screen. Find the right button.\nLet your buddy handle a task, with your approval.", 11));
        foreach (var example in new[] { "What's on my screen?", "Where is the save button?", "Type a draft in the open app" })
        {
            var chip = AppTheme.Button(example);
            chip.Margin = new Padding(0, 0, 0, 8);
            chip.Click += (_, _) => SetExample(example);
            welcomeFlow.Controls.Add(chip);
        }
        _welcome.Controls.Add(welcomeFlow);
        surface.Controls.Add(_welcome);
        _welcome.BringToFront();
        main.Controls.Add(surface, 0, 1);

        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
        composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        composer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _ask = new RadioButton { Text = "Ask about my screen", AutoSize = true, Checked = true, ForeColor = AppTheme.Accent };
        _agent = new RadioButton { Text = "Do a task · Agent", AutoSize = true, Margin = new Padding(20, 3, 3, 3) };
        modes.Controls.AddRange(new Control[] { _ask, _agent });
        composer.Controls.Add(modes, 0, 0);
        _prompt = new TextBox
        {
            Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "What would you like help with?", AccessibleName = "Your request", MaxLength = 12000,
        };
        _prompt.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; await SendAsync(); }
        };
        composer.Controls.Add(_prompt, 0, 1);
        composer.Controls.Add(AppTheme.Label("Sending shares all connected screens with your provider. Agent asks before each action.", 8), 0, 2);
        var actions = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _status = AppTheme.Label("Ready when you are", 9);
        _status.Top = 10;
        actions.Controls.Add(_status);
        _sendButton = AppTheme.Button("Send request  →", true);
        _sendButton.AutoSize = false;
        _sendButton.Size = new Size(155, 40);
        _sendButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        actions.Controls.Add(_sendButton);
        actions.Resize += (_, _) => _sendButton.Left = actions.Width - _sendButton.Width;
        _sendButton.Click += async (_, _) => await SendAsync();
        composer.Controls.Add(actions, 0, 3);
        main.Controls.Add(composer, 0, 2);
        _shortcut = AppTheme.Label($"Hold {settings.PushToTalk.DisplayName} in any app to talk. Closing this window keeps your buddy in the tray.", 8);
        _shortcut.Dock = DockStyle.Fill;
        _shortcut.TextAlign = ContentAlignment.MiddleLeft;
        main.Controls.Add(_shortcut, 0, 3);
        SetState(CompanionVoiceState.Idle);
        ResumeLayout(true);
    }

    public void SetConnection(string text) => _connection.Text = text;
    private void FocusPrompt() => _prompt.Focus();
    private void SetExample(string example)
    {
        _prompt.Text = example;
        _agent.Checked = example.StartsWith("Type");
        _ask.Checked = !_agent.Checked;
        _prompt.Focus();
    }
    public void SetStatus(string text) => _status.Text = text;
    public void RefreshShortcut() => _shortcut.Text = $"Hold {_settings.PushToTalk.DisplayName} in any app to talk. Closing this window keeps your buddy in the tray.";

    public void SetState(CompanionVoiceState state)
    {
        _busy = state != CompanionVoiceState.Idle;
        _sendButton.Enabled = !_busy;
        _demo.Enabled = !_busy;
        _stopButton.Enabled = _busy;
        _status.Text = state switch
        {
            CompanionVoiceState.Listening => "Listening… release the shortcut to send",
            CompanionVoiceState.Processing => "Looking at your screen…",
            CompanionVoiceState.Pointing => "Pointing it out",
            CompanionVoiceState.Responding => "Here’s what I found",
            _ => "Ready when you are",
        };
    }

    public void AddMessage(string role, string text)
    {
        _welcome.Hide();
        _conversation.SelectionStart = _conversation.TextLength;
        using var headingFont = new Font("Segoe UI", 9, FontStyle.Bold);
        _conversation.SelectionFont = headingFont;
        _conversation.SelectionColor = role == "You" ? AppTheme.Muted : AppTheme.Accent;
        _conversation.AppendText(role + "\n");
        _conversation.SelectionFont = _conversation.Font;
        _conversation.SelectionColor = AppTheme.Ink;
        _conversation.AppendText(text + "\n\n");
        _conversation.ScrollToCaret();
    }

    public void ClearConversation() { _conversation.Clear(); _welcome.Show(); }

    private async Task SendAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_prompt.Text)) return;
        var text = _prompt.Text.Trim();
        _prompt.Clear();
        await _send(text, _agent.Checked);
    }

    private async Task RunDemoAsync(Func<Task> demo)
    {
        if (_busy) return;
        try { await demo(); }
        catch (Exception ex) { AddMessage("OpenClicky", $"Demo couldn't finish: {ex.Message}"); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _busy) { _stop(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
}
