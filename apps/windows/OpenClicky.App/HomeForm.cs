using OpenClicky.Core;
using OpenClicky.Providers;

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
    private readonly IntelligentUiPanel _intelligentUi;
    private readonly TextBox _prompt;
    private readonly Button _sendButton;
    private readonly Button _stopButton;
    private readonly Button _toolsButton;
    private readonly RadioButton _ask;
    private readonly RadioButton _agent;
    private readonly Panel _welcome;
    private readonly Button _demo;
    private readonly Button _listen;
    private readonly CheckBox _guide;
    private readonly Label _transcript;
    private readonly Label _microphone;
    private bool _busy;
    private bool _taskRunning;
    private bool _conversationActive;
    private bool _walkthroughActive;
    private readonly TaskWorkspacePanel _tasks;
    private readonly WorkPanel? _work;
    private readonly TabControl _workspace;
    private readonly ComboBox _access;
    private readonly HomeUiState _uiState = new();
    private readonly DictationPanel _dictation;
    private readonly TableLayoutPanel _main;
    private readonly TableLayoutPanel _composer;
    private (float Composer, float Footer)? _dictationRowHeights;
    private int _streamStart = -1, _streamLength;
    private string? _completedStreamText;
    private VisualWorkspacePanel? _visualWorkspace;
    private bool _syncingAccess;
    /// <summary>Raised after the classic access picker saved a new mode, so the notch shows it too.</summary>
    public event Action? AccessChanged;
    private readonly TabPage _notesPage;
    private (SizeType Type, float Height)[]? _nativeComposerRows;
    private float _nativeComposerHeight;

    public HomeForm(AppSettings settings, Func<string, bool, Task> send, Action stop,
        Action openSettings, Func<Task> tryDemo, Func<Task> testAgent,
        Action toggleConversation, Action<bool> guideChanged,
        Func<Task> selectRegion, Action pauseTask, Func<Task> resumeTask, Action clearTasks,
        Action showClipboardShelf,
        Func<Task>? beginDictation = null,
        Func<Task>? toggleDictationRecording = null,
        Func<string, Task>? insertDictation = null,
        Action? cancelDictation = null,
        Func<string, Task>? startWalkthrough = null,
        Action? setupChrome = null,
        WorkStore? work = null,
        Func<string, string?, Task<bool>>? runJob = null,
        ConnectorRegistry? connectors = null)
    {
        _settings = settings;
        _send = send;
        _stop = stop;
        Text = "OpenClicky";
        AppTheme.Apply(this);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 820);
        MinimumSize = new Size(980, 700);
        KeyPreview = true;
        Activated += (_, _) => { if (_taskRunning) pauseTask(); };
        Resize += (_, _) => _uiState.ObserveWindowState(WindowState);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Tint, Padding = new Padding(22, 28, 22, 22), AutoScroll = true };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        nav.Controls.Add(new PictureBox { Image = AppTheme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(58, 58), Margin = new Padding(0, 0, 0, 12) });
        nav.Controls.Add(AppTheme.Label("OpenClicky", 19, true));
        nav.Controls.Add(AppTheme.Label("A buddy for your Windows.", 9));
        var home = AppTheme.Button("Your companion", true);
        home.Width = 162;
        home.Click += (_, _) => FocusPrompt();
        nav.Controls.Add(home);
        var select = AppTheme.Button("Select screen area");
        select.Click += async (_, _) => { if (!_busy) await selectRegion(); };
        nav.Controls.Add(select);
        var settingsButton = AppTheme.Button("Settings");
        settingsButton.Width = 162;
        settingsButton.Click += (_, _) => openSettings();
        nav.Controls.Add(settingsButton);
        var clipboard = AppTheme.Button("Clipboard shelf");
        clipboard.Click += (_, _) => showClipboardShelf();
        nav.Controls.Add(clipboard);
        var dictate = AppTheme.Button("Dictate into app", true);
        dictate.Click += async (_, _) => { if (!_busy && beginDictation is not null) await beginDictation(); };
        nav.Controls.Add(dictate);
        var walkthrough = AppTheme.Button("Guide me step by step");
        walkthrough.AccessibleName = "Guide me step by step";
        walkthrough.Click += async (_, _) =>
        {
            if (_busy || startWalkthrough is null) return;
            if (_visualWorkspace is not null)
            {
                _visualWorkspace.SetPrompt("Show me how to ");
                SetStatus("Finish the walkthrough goal in chat, then send it.");
                return;
            }
            if (string.IsNullOrWhiteSpace(RequestText))
            {
                SetStatus("Type your walkthrough goal below, then click Guide me step by step.");
                FocusPrompt();
                return;
            }
            await startWalkthrough(RequestText);
        };
        nav.Controls.Add(walkthrough);
        _demo = AppTheme.Button("Try pointing demo");
        _demo.Width = 162;
        _demo.Click += async (_, _) => await RunDemoAsync(tryDemo);
        nav.Controls.Add(_demo);
        var sandbox = AppTheme.Button("Test desktop actions");
        sandbox.Width = 162;
        sandbox.Click += async (_, _) => await RunDemoAsync(testAgent);
        nav.Controls.Add(sandbox);
        _listen = AppTheme.Button("Start conversation", true);
        _listen.Click += (_, _) => toggleConversation();
        nav.Controls.Add(_listen);
        _microphone = AppTheme.Label("Microphone: Windows default", 8);
        _microphone.MaximumSize = new Size(160, 0);
        nav.Controls.Add(_microphone);
        var help = AppTheme.Button("Help with this app");
        help.Click += async (_, _) => { if (!_busy) await _send(AppKnowledge.ScreenHelpRequest, false); };
        nav.Controls.Add(help);
        var webHelp = AppTheme.Button("Find official web help");
        webHelp.Click += async (_, _) => { if (!_busy) await _send(AppKnowledge.WebHelpPrefix + "Explain the next useful step in the current app, and mark its visible control.", false); };
        nav.Controls.Add(webHelp);
        foreach (var button in nav.Controls.OfType<Button>())
        {
            button.AutoSize = false;
            button.Size = new Size(162, 42);
            button.Font = new Font("Segoe UI", 9);
            button.Padding = new Padding(6, 4, 6, 4);
        }
        _guide = new CheckBox { Text = "Offer help when\nI seem stuck", Checked = settings.AwarenessEnabled, AutoSize = true, Margin = new Padding(0,12,0,8), AccessibleName = "Local awareness" };
        nav.Controls.Add(_guide);
        var guideNote = AppTheme.Label("Local activity can suggest help.\nAccept before screen inspection.\nEsc pauses awareness.", 8);
        nav.Controls.Add(guideNote);
        sidebar.AutoScroll = true;
        sidebar.Controls.Add(nav);
        var foot = AppTheme.Label($"Open source.\nYour account. Your control.\n\nv{Application.ProductVersion.Split('+')[0]} · Windows", 9);
        foot.Dock = DockStyle.Bottom;
        sidebar.Controls.Add(foot);
        layout.Controls.Add(sidebar, 0, 0);

        _main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 20), RowCount = 4, ColumnCount = 1 };
        _main.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        _main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _main.RowStyles.Add(new RowStyle(SizeType.Absolute, 208));
        _main.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(_main, 1, 0);

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
        _toolsButton = AppTheme.Button("Tools");
        _toolsButton.AutoSize = false;
        _toolsButton.Size = new Size(90, 38);
        _toolsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_toolsButton);
        _guide.CheckedChanged += (_, _) => { guideChanged(_guide.Checked); _stopButton.Enabled = _walkthroughActive || _taskRunning || _busy || _guide.Checked; };
        header.Resize += (_, _) => _toolsButton.Left = _stopButton.Left - _toolsButton.Width - 8;
        _main.Controls.Add(header, 0, 0);

        var surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20), Margin = new Padding(0, 0, 0, 14) };
        _conversation = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
            ForeColor = AppTheme.Ink, Font = new Font("Segoe UI", 11), DetectUrls = true,
            AccessibleName = "Conversation", TabStop = true,
        };
        surface.Controls.Add(_conversation);
        _intelligentUi = new IntelligentUiPanel(PrepareSuggestedPromptAsync)
        {
            Dock = DockStyle.Bottom,
            Height = 260,
        };
        surface.Controls.Add(_intelligentUi);
        surface.SizeChanged += (_, _) => _intelligentUi.Height = Math.Clamp(
            (surface.ClientSize.Height - surface.Padding.Vertical) / 2, 96, 360);
        _conversation.LinkClicked += (_, e) =>
        {
            if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out var link) && link.Scheme == "https")
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });
        };
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
        _dictation = new DictationPanel(toggleDictationRecording ?? (() => Task.CompletedTask),
            insertDictation ?? (_ => Task.CompletedTask), cancelDictation ?? (() => { }));
        surface.Controls.Add(_dictation);
        _workspace = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Companion workspace" };
        var chatPage = new TabPage("Chat") { BackColor = Color.White }; chatPage.Controls.Add(surface);
        _tasks = new TaskWorkspacePanel(pauseTask, resumeTask, stop, clearTasks);
        var taskPage = new TabPage("Tasks") { BackColor = Color.White }; taskPage.Controls.Add(_tasks);
        var workPage = new TabPage("Work") { BackColor = Color.White };
        if (work is not null && runJob is not null) { _work = new WorkPanel(work, runJob); workPage.Controls.Add(_work); }
        var memoryPage = new TabPage("Memory") { BackColor = Color.White }; memoryPage.Controls.Add(new CompanionMemoryPanel());
        var notesPage = _notesPage = new TabPage("Notes") { BackColor = Color.White }; notesPage.Controls.Add(new NotesPanel());
        var connectionsPage = new TabPage("Connections") { BackColor = Color.White, Padding = new Padding(20) };
        var connectionInfo = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        connectionInfo.Controls.Add(AppTheme.Label("Your accounts, under your control", 17, true));
        connectionInfo.Controls.Add(AppTheme.Label("ChatGPT powers reasoning. Gmail, Slack and other accounts need their own consent.\nAdd any MCP server you control or one from a provider such as Composio; paste its address and credential below.", 11));
        connectionInfo.Controls.Add(AppTheme.Label("Tasks call connectors through OpenClicky: read-only tools run directly, others ask you first under Confirm each. Requests and results go to that provider. Your ChatGPT credentials are never copied into another engine.", 10));
        if (connectors is not null) connectionInfo.Controls.Add(new ConnectorsPanel(connectors));
        connectionInfo.Controls.Add(AppTheme.Label("OpenWork · tasks inside your companion", 13, true));
        connectionInfo.Controls.Add(AppTheme.Label("Choose Agent in Chat, then describe your task. Progress and approvals appear here in OpenClicky. Your selected access mode stays fixed for that task.", 10));
        connectionInfo.Controls.Add(AppTheme.Label("During a task, relevant window images and control text go to ChatGPT. Local speech recognition keeps raw audio on your PC.", 9));
        var chromeSetup = AppTheme.Button("Set up Chrome tabs");
        chromeSetup.Click += (_, _) => setupChrome?.Invoke();
        chromeSetup.Enabled = setupChrome is not null;
        connectionInfo.Controls.Add(chromeSetup);
        connectionInfo.Controls.Add(AppTheme.Label("Optional: let agents use your signed-in browser in their own tab group. Connect it in Settings → Integrations → Browser.", 9));
        connectionsPage.Controls.Add(connectionInfo);
        _workspace.TabPages.AddRange([chatPage, taskPage, workPage, memoryPage, notesPage, connectionsPage]);
        _workspace.SelectedIndexChanged += (_, _) =>
        {
            if (_workspace.SelectedIndex != 0 && _dictation.Visible) cancelDictation?.Invoke();
            if (_taskRunning) pauseTask();
            if (_workspace.SelectedIndex == 0 && Visible && WindowState != FormWindowState.Minimized
                && _uiState.ConsumeDeferredChatScroll())
                BeginInvoke(ScrollConversationToLatest);
            if (_workspace.SelectedTab == memoryPage && memoryPage.Controls[0] is CompanionMemoryPanel memory) memory.RefreshMemory();
            if (_workspace.SelectedTab == notesPage && notesPage.Controls[0] is NotesPanel notes) notes.RefreshNotes();
            if (_workspace.SelectedTab == workPage) _work?.RefreshWork();
        };
        _main.Controls.Add(_workspace, 0, 1);

        _composer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Margin = Padding.Empty };
        _composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _composer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _ask = new RadioButton { Text = "Ask about my screen", AutoSize = true, Checked = true, ForeColor = AppTheme.Accent };
        _agent = new RadioButton { Text = "Do a task · Agent", AutoSize = true, Margin = new Padding(20, 3, 3, 3) };
        modes.Controls.AddRange(new Control[] { _ask, _agent });
        _access = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "Task access mode", Margin = new Padding(12,0,0,0) };
        _access.Items.AddRange(["Preview only", "Approve each action", "Auto · approve task", "Full access"]);
        _access.SelectedIndex = Enum.IsDefined(settings.AgentAccess) ? (int)settings.AgentAccess : 1;
        _access.SelectedIndexChanged += (_, _) =>
        {
            var mode = (AgentAccessMode)_access.SelectedIndex;
            if (_syncingAccess) return;
            if (mode is AgentAccessMode.FullAccess or AgentAccessMode.Auto)
            {
                if (MessageBox.Show(this, OpenClicky.App.Settings.NativeSettings.AccessWarning(mode), OpenClicky.App.Settings.NativeSettings.AccessWarningTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { _access.SelectedIndex = 1; return; }
            }
            _settings.AgentAccess = mode;
            try { SettingsStore.Save(_settings); AccessChanged?.Invoke(); }
            catch (Exception ex) { AddMessage("OpenClicky", "Could not save task access: " + ex.Message); }
        };
        modes.Controls.Add(_access);
        _composer.Controls.Add(modes, 0, 0);
        _prompt = new TextBox
        {
            Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "What would you like help with?", AccessibleName = "Your request", MaxLength = 12000,
        };
        _prompt.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; await SendAsync(); }
        };
        _composer.Controls.Add(_prompt, 0, 1);
        _composer.Controls.Add(AppTheme.Label("Screen questions share every connected display (up to four, your cursor's first) without OpenClicky's own windows, or only the area you select. Tasks may inspect more of the desktop. Check your task access mode.", 8), 0, 2);
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
        _transcript = AppTheme.Label("Voice stays on this PC · recognized words appear here", 9);
        _transcript.AutoSize = false;
        _transcript.Dock = DockStyle.Fill;
        _transcript.AutoEllipsis = true;
        _transcript.AccessibleName = "Live transcript";
        _composer.Controls.Add(_transcript, 0, 3);
        _composer.Controls.Add(actions, 0, 4);
        _main.Controls.Add(_composer, 0, 2);
        _shortcut = AppTheme.Label($"{settings.TalkHotkey.Gesture} in any app to talk. Closing this window keeps your buddy in the tray.", 8);
        _shortcut.Dock = DockStyle.Fill;
        _shortcut.TextAlign = ContentAlignment.MiddleLeft;
        _main.Controls.Add(_shortcut, 0, 3);
        SetState(CompanionVoiceState.Idle);
        ResumeLayout(true);
    }

    public void SetConnection(string text) => _connection.Text = text;
    public void BeginDictation(string appTitle, string controlName, bool automatic = false)
    {
        _welcome.Hide();
        _workspace.SelectedIndex = 0;
        _dictationRowHeights ??= (_main.RowStyles[2].Height, _main.RowStyles[3].Height);
        _composer.Hide();
        _shortcut.Hide();
        _main.RowStyles[2].Height = 0;
        _main.RowStyles[3].Height = 0;
        _dictation.Begin(appTitle, controlName, automatic);
    }
    public void SetDictationTranscript(string text) => _dictation.ShowLive(text);
    public void AcceptDictationTranscript(string text) => _dictation.AcceptTranscript(text);
    public void SetDictationResult(string text, bool canContinue = true) => _dictation.SetResult(text, canContinue);
    public void EndDictation()
    {
        _dictation.End();
        if (_dictationRowHeights is { } heights)
        {
            _main.RowStyles[2].Height = heights.Composer;
            _main.RowStyles[3].Height = heights.Footer;
            _dictationRowHeights = null;
        }
        _composer.Show();
        _shortcut.Show();
        _visualWorkspace?.BringToFront();
        if (_conversation.TextLength == 0 && _visualWorkspace is null) _welcome.Show();
    }
    public void SetToolsMenu(ContextMenuStrip menu)
    {
        _toolsButton.ContextMenuStrip = menu;
        _toolsButton.Click += (_, _) => menu.Show(_toolsButton, new Point(0, _toolsButton.Height));
    }
    public void ShowIntelligentUi(IntelligentUiDocument? document)
    {
        if (document is null) _intelligentUi.ClearDocument();
        else _intelligentUi.ShowDocument(document);
    }
    public void UpdateTasks(IReadOnlyList<TaskRunRecord> tasks) => _tasks.UpdateRuns(tasks);
    public void RefreshWork() => _work?.RefreshWork();
    public bool AgentSelected => _agent.Checked;
    public void AttachVisualWorkspace(VisualWorkspacePanel panel)
    {
        _visualWorkspace = panel;
        panel.Dock = DockStyle.Fill;
        _conversation.Parent!.Controls.Add(panel);
        panel.BringToFront();
        _welcome.Hide(); _conversation.Hide(); _intelligentUi.Hide();
        _prompt.Hide(); _sendButton.Hide();
        _nativeComposerRows = _composer.RowStyles.Cast<RowStyle>().Select(style => (style.SizeType, style.Height)).ToArray();
        _nativeComposerHeight = _main.RowStyles[2].Height;
        for (var i = 1; i < _composer.RowStyles.Count; i++) _composer.RowStyles[i] = new RowStyle(SizeType.Absolute, 0);
        _main.RowStyles[2].Height = 42;
    }
    public void DetachVisualWorkspace()
    {
        _visualWorkspace?.Dispose(); _visualWorkspace = null;
        if (_nativeComposerRows is { } rows)
            for (var i = 0; i < rows.Length; i++) _composer.RowStyles[i] = new RowStyle(rows[i].Type, rows[i].Height);
        _main.RowStyles[2].Height = _nativeComposerHeight;
        _conversation.Show(); _prompt.Show(); _sendButton.Show();
        if (_conversation.TextLength == 0) _welcome.Show();
    }
    public void SetTaskRunning(bool running)
    {
        _taskRunning = running;
        _access.Enabled = !running;
        _stopButton.Enabled = _walkthroughActive || _busy || running || _guide.Checked;
        if (!_busy) _status.Text = running ? "Task running · voice pauses it" : "Ready when you are";
    }
    /// <summary>As <see cref="SetTaskRunning"/>, only when it changed (called on every lease change and twice a second).</summary>
    public void RefreshTaskRunning(bool running) { if (running != _taskRunning) SetTaskRunning(running); }
    public void ShowTasks() => _workspace.SelectedIndex = 1;
    public void ShowNotes() => _workspace.SelectedTab = _notesPage;

    /// <summary>The access mode changed elsewhere (the notch page, after its own warning): show it without asking again.</summary>
    public void RefreshAccess()
    {
        _syncingAccess = true;
        try { _access.SelectedIndex = Enum.IsDefined(_settings.AgentAccess) ? (int)_settings.AgentAccess : 1; }
        finally { _syncingAccess = false; }
    }
    public string RequestText => _prompt.Text.Trim();
    private Task PrepareSuggestedPromptAsync(string prompt)
    {
        _prompt.Text = prompt;
        _ask.Checked = true;
        FocusPrompt();
        SetStatus("Suggested request added · review it, then Send");
        return Task.CompletedTask;
    }
    private void FocusPrompt()
    {
        _workspace.SelectedIndex = 0;
        if (_visualWorkspace is not null) _visualWorkspace.SetPrompt(_prompt.Text);
        else _prompt.Focus();
    }
    private void SetExample(string example)
    {
        _prompt.Text = example;
        _agent.Checked = example.StartsWith("Type");
        _ask.Checked = !_agent.Checked;
        _prompt.Focus();
    }
    public void SetStatus(string text) => _status.Text = text;
    public void SetWalkthroughActive(bool active)
    {
        _walkthroughActive = active;
        _stopButton.Enabled = active || _busy || _taskRunning || _guide.Checked;
    }
    public void SetTranscript(string text) => _transcript.Text = string.IsNullOrWhiteSpace(text)
        ? "Voice stays on this PC · recognized words appear here" : "Hearing: " + text;
    public void SetMicrophone(string name, bool whisper) => _microphone.Text = "Microphone: " + name + "\n" + (whisper ? "Whisper · local English voice" : "Compatibility voice · download Whisper in Settings");
    public void SetConversationActive(bool active)
    {
        _conversationActive = active;
        _listen.Text = active ? "Stop conversation" : "Start conversation";
        _listen.AccessibleName = _listen.Text;
        _listen.Enabled = active || !_busy;
    }
    public void TurnOffGuide() => _guide.Checked = false;
    public void RefreshShortcut() => _shortcut.Text = $"{_settings.TalkHotkey.Gesture} in any app to talk. Closing this window keeps your buddy in the tray.";
    public void RestoreFromMinimized()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = _uiState.RestoreWindowState;
        if (Visible && _uiState.WindowBecameVisible(_workspace.SelectedIndex == 0))
            BeginInvoke(ScrollConversationToLatest);
    }

    public void MinimizeForRequest()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
            _uiState.WindowWillHide(_workspace.SelectedIndex == 0, IsConversationAtLatest());
        WindowState = FormWindowState.Minimized;
    }

    public void SetState(CompanionVoiceState state)
    {
        _busy = state != CompanionVoiceState.Idle;
        _sendButton.Enabled = !_busy;
        _demo.Enabled = !_busy;
        _stopButton.Enabled = _walkthroughActive || _busy || _taskRunning || _guide.Checked;
        _listen.Text = _conversationActive ? "Stop conversation" : "Start conversation";
        _listen.AccessibleName = _listen.Text;
        _listen.Enabled = _conversationActive || !_busy;
        _status.Text = state switch
        {
            CompanionVoiceState.Listening => _conversationActive ? "Listening… speak naturally; Stop ends the conversation" : "Listening… release the shortcut to send",
            CompanionVoiceState.Processing => "Thinking…",
            CompanionVoiceState.Pointing => "Speaking… pointing it out",
            CompanionVoiceState.Responding => "Speaking…",
            _ => _taskRunning ? "Task running · voice pauses it" : "Ready when you are",
        };
        if (_dictation.Visible) _dictation.SetVoiceState(state);
    }

    public void AddMessage(string role, string text)
    {
        if (role == "OpenClicky" && _completedStreamText == text) { _completedStreamText = null; return; }
        _streamStart = -1;
        _completedStreamText = null;
        var chatSelected = _workspace.SelectedIndex == 0;
        var windowVisible = Visible && WindowState != FormWindowState.Minimized;
        var scrollToLatest = _uiState.MessageAppended(
            chatSelected, windowVisible, chatSelected && windowVisible && IsConversationAtLatest());
        _welcome.Hide();
        _conversation.SelectionStart = _conversation.TextLength;
        using var headingFont = new Font("Segoe UI", 9, FontStyle.Bold);
        _conversation.SelectionFont = headingFont;
        _conversation.SelectionColor = role == "You" ? AppTheme.Muted : AppTheme.Accent;
        _conversation.AppendText(role + "\n");
        _conversation.SelectionFont = _conversation.Font;
        _conversation.SelectionColor = AppTheme.Ink;
        _conversation.AppendText(text + "\n\n");
        if (scrollToLatest) ScrollConversationToLatest();
    }

    public void SetStreamingResponse(string text, bool complete)
    {
        if (_streamStart < 0)
        {
            AddMessage("OpenClicky", "");
            _streamStart = _conversation.TextLength - 2;
            _streamLength = 0;
        }
        var follow = IsConversationAtLatest();
        _conversation.Select(_streamStart, _streamLength);
        _conversation.SelectedText = text;
        _streamLength = text.Length;
        if (follow) ScrollConversationToLatest();
        if (complete) { _streamStart = -1; _completedStreamText = text; }
    }

    public void ClearConversation() { _streamStart = -1; _completedStreamText = null; _conversation.Clear(); _intelligentUi.ClearDocument(); _visualWorkspace?.ClearConversation(); if (_visualWorkspace is null) _welcome.Show(); _uiState.ClearConversation(); }

    private bool IsConversationAtLatest()
    {
        if (_conversation.TextLength == 0) return true;
        var bottomRight = new Point(
            Math.Max(0, _conversation.ClientSize.Width - 2),
            Math.Max(0, _conversation.ClientSize.Height - 2));
        return _conversation.GetCharIndexFromPosition(bottomRight) >= _conversation.TextLength - 2;
    }

    private void ScrollConversationToLatest()
    {
        if (_conversation.IsDisposed) return;
        _conversation.SelectionStart = _conversation.TextLength;
        _conversation.SelectionLength = 0;
        _conversation.ScrollToCaret();
    }

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
        if (keyData == Keys.Escape && (_busy || _taskRunning || _guide.Checked)) { _stop(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_uiState.ConsumeInitialChatScroll(_workspace.SelectedIndex == 0, _conversation.TextLength > 0))
            BeginInvoke(ScrollConversationToLatest);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            _uiState.WindowWillHide(_workspace.SelectedIndex == 0, IsConversationAtLatest());
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}

internal sealed class HomeUiState
{
    private bool _deferredChatScroll;
    private bool _initialShowPending = true;
    private bool _followLatestWhileWindowHidden;

    public FormWindowState RestoreWindowState { get; private set; } = FormWindowState.Normal;

    public void ObserveWindowState(FormWindowState state)
    {
        if (state is FormWindowState.Normal or FormWindowState.Maximized)
            RestoreWindowState = state;
    }

    public void WindowWillHide(bool chatSelected, bool readerAtLatest) =>
        _followLatestWhileWindowHidden = chatSelected && readerAtLatest;

    public bool MessageAppended(bool chatSelected, bool windowVisible, bool readerAtLatest)
    {
        if (chatSelected && windowVisible) return readerAtLatest;
        if (!chatSelected || _followLatestWhileWindowHidden) _deferredChatScroll = true;
        return false;
    }

    public bool WindowBecameVisible(bool chatSelected)
    {
        _followLatestWhileWindowHidden = false;
        return chatSelected && ConsumeDeferredChatScroll();
    }

    public bool ConsumeDeferredChatScroll()
    {
        if (!_deferredChatScroll) return false;
        _deferredChatScroll = false;
        return true;
    }

    public bool ConsumeInitialChatScroll(bool chatSelected, bool hasMessages)
    {
        if (!_initialShowPending) return false;
        _initialShowPending = false;
        return chatSelected && hasMessages;
    }

    public void ClearConversation()
    {
        _deferredChatScroll = false;
        _followLatestWhileWindowHidden = false;
    }
}
