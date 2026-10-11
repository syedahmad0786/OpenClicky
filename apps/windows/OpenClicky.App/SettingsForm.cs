using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.App;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly Action _onApplied;
    private readonly TextBox _hotkeyBox;
    private readonly Label _hotkeyHint;
    private readonly TextBox _proxyBox;
    private readonly CheckBox _useProxy;
    private readonly RadioButton _providerOpenRouter;
    private readonly RadioButton _providerChatGpt;
    private readonly TextBox _apiKeyBox;
    private readonly TextBox _modelBox;
    private readonly ComboBox _chatGptModelBox;
    private readonly FlowLayoutPanel _chatGptPanel;
    private readonly FlowLayoutPanel _openRouterPanel;
    private readonly Button _refreshModels;
    private readonly Label _chatGptStatus;
    private readonly Button _chatGptSignIn;
    private readonly Button _chatGptSignOut;
    private readonly TextBox _debugTranscriptBox;
    private readonly CheckBox _persistHistory;
    private readonly CheckBox _forceGdi;
    private readonly TextBox _denyExtraBox;
    private readonly TextBox _logBox;
    private readonly Action? _clearHistory;
    private readonly ChatGptOAuthService _chatGptOAuth = new(msg => DebugLog.Append(msg));
    private HotkeyChord _draft;
    private bool _signInBusy;
    private readonly CheckBox _windowsSpeech;
    private readonly CheckBox _speakReplies;
    private readonly TextBox _transcriptionModelBox;

    public SettingsForm(AppSettings settings, ISecretStore secrets, Action onApplied, Action? onClearHistory = null)
    {
        _settings = settings;
        _secrets = secrets;
        _onApplied = onApplied;
        _clearHistory = onClearHistory;
        _draft = settings.PushToTalk;
        _settings.EnsureChatGptHostId();
        Text = "OpenClicky Settings";
        AppTheme.Apply(this);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 660);
        MinimumSize = new Size(620, 600);
        MaximizeBox = false;

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(24, 18, 24, 8), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        header.Controls.Add(AppTheme.Label("Make your buddy feel at home.", 20, true));
        header.Controls.Add(AppTheme.Label("Your provider. Your voice. Your control.", 10));
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 8), AccessibleName = "Settings sections" };
        var provider = Page(tabs, "Connection");
        var voice = Page(tabs, "Voice & shortcut");
        var privacy = Page(tabs, "Privacy");
        var advanced = Page(tabs, "Advanced");

        AddHeading(provider, "Use your ChatGPT plan", "Connect your eligible ChatGPT Plus or Pro account. No API key needed. Screen images and requests go to OpenAI.");
        var providers = new FlowLayoutPanel { AutoSize = true, Width = 560, Margin = new Padding(0, 0, 0, 16) };
        _providerOpenRouter = new RadioButton { Text = "OpenRouter", Checked = settings.AuthProvider != AuthProvider.ChatGPT, AutoSize = true };
        _providerChatGpt = new RadioButton { Text = "ChatGPT plan", Checked = settings.AuthProvider == AuthProvider.ChatGPT, AutoSize = true };
        _providerOpenRouter.Margin = new Padding(20,3,3,3);
        providers.Controls.AddRange(new Control[] { _providerChatGpt, _providerOpenRouter });
        provider.Controls.Add(providers);
        _openRouterPanel = new FlowLayoutPanel { AutoSize = true, Width = 560, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        _chatGptPanel = new FlowLayoutPanel { AutoSize = true, Width = 560, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        provider.Controls.AddRange(new Control[] { _chatGptPanel, _openRouterPanel });
        _apiKeyBox = Field(_openRouterPanel, "OpenRouter API key", settings.OpenRouterApiKey ?? "");
        _apiKeyBox.UseSystemPasswordChar = true;
        _apiKeyBox.AccessibleName = "OpenRouter API key";
        _openRouterPanel.Controls.Add(AppTheme.Label("Saved securely with Windows. Never included in settings or logs.", 8));
        _modelBox = Field(_openRouterPanel, "Vision model", settings.OpenRouterModel);
        _chatGptStatus = AppTheme.Label(ChatGptStatusText(), 9);
        _chatGptStatus.MaximumSize = new Size(530, 0);
        _chatGptPanel.Controls.Add(_chatGptStatus);
        var authButtons = new FlowLayoutPanel { AutoSize = true, Width = 560, Margin = Padding.Empty };
        _chatGptSignIn = AppTheme.Button("Continue with ChatGPT");
        _chatGptSignIn.BackColor = Color.Black;
        _chatGptSignIn.ForeColor = Color.White;
        _chatGptSignIn.Click += async (_, _) => await SignInChatGptAsync();
        _chatGptSignOut = AppTheme.Button("Sign out");
        _chatGptSignOut.Click += async (_, _) => await SignOutChatGptAsync();
        authButtons.Controls.AddRange(new Control[] { _chatGptSignIn, _chatGptSignOut });
        _chatGptPanel.Controls.Add(authButtons);
        _chatGptPanel.Controls.Add(AppTheme.Label("Model · available after sign-in", 9, true));
        _chatGptModelBox = new ComboBox { Width = 550, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(ChatGptModel.DisplayName), ValueMember = nameof(ChatGptModel.Slug), AccessibleName = "ChatGPT model", Margin = new Padding(0,6,0,12) };
        _chatGptPanel.Controls.Add(_chatGptModelBox);
        var usageButtons = new FlowLayoutPanel { Width = 560, AutoSize = true, Margin = Padding.Empty };
        _refreshModels = AppTheme.Button("Refresh models");
        _refreshModels.Click += async (_, _) => await RefreshModelsAsync();
        var manageUsage = AppTheme.Button("Manage usage");
        manageUsage.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true });
        usageButtons.Controls.AddRange(new Control[] { _refreshModels, manageUsage });
        _chatGptPanel.Controls.Add(usageButtons);
        var speechNote = AppTheme.Label("Your ChatGPT plan powers Ask and Agent. Voice recognition and spoken replies run locally on Windows. Sign-in does not give OpenClicky access to your ChatGPT conversations.", 9);
        speechNote.MaximumSize = new Size(530, 0);
        _chatGptPanel.Controls.Add(speechNote);
        _providerChatGpt.CheckedChanged += (_, _) => RefreshProviderPanels();

        AddHeading(voice, "Talk from any app", "Hold your shortcut while speaking, then release. You can always type in Home.");
        _hotkeyBox = Field(voice, "Shortcut · click the field and press your combination", _draft.DisplayName);
        _hotkeyBox.ReadOnly = true;
        _hotkeyBox.KeyDown += HotkeyBoxOnKeyDown;
        var reset = AppTheme.Button("Reset to Win+Alt+Space");
        reset.Click += (_, _) => { _draft = HotkeyChord.Default; _hotkeyBox.Text = _draft.DisplayName; SetHotkeyHint(null); };
        voice.Controls.Add(reset);
        _hotkeyHint = AppTheme.Label("Default: Win+Alt+Space. Use a modifier and a key.", 9);
        voice.Controls.Add(_hotkeyHint);
        _windowsSpeech = new CheckBox { Text = "Use local Windows speech recognition", Checked = settings.UseWindowsSpeech, AutoSize = true, Margin = new Padding(0,12,0,10) };
        voice.Controls.Add(_windowsSpeech);
        string languages;
        try { languages = string.Join(", ", LocalSpeechSttClient.InstalledLanguages()); }
        catch { languages = ""; }
        voice.Controls.Add(AppTheme.Label(string.IsNullOrWhiteSpace(languages) ? "No Windows speech language found. Turn this off to use OpenRouter transcription." : "Installed: " + languages + ". Audio stays on this PC when Windows speech is on.", 9));
        _transcriptionModelBox = Field(voice, "OpenRouter transcription model · used when local speech is off", settings.TranscriptionModel);
        _speakReplies = new CheckBox { Text = "Read replies aloud using a Windows voice", Checked = settings.SpeakReplies, AutoSize = true, Margin = new Padding(0,12,0,10) };
        voice.Controls.Add(_speakReplies);

        AddHeading(privacy, "You stay in charge", "All connected screens are captured for a request. Images stay in memory; your selected AI provider receives them to answer.");
        privacy.Controls.Add(AppTheme.Label("There is no OpenClicky cloud account. Keys and ChatGPT tokens use Windows Credential Manager or encrypted DPAPI storage.", 10));
        _persistHistory = new CheckBox { Text = "Remember my last ten conversations · text only", Checked = settings.PersistConversationHistory, AutoSize = true, Margin = new Padding(0,10,0,12) };
        privacy.Controls.Add(_persistHistory);
        var clear = AppTheme.Button("Clear conversation history");
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show("Clear saved conversation history?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                _clearHistory?.Invoke();
        };
        privacy.Controls.Add(clear);
        privacy.Controls.Add(AppTheme.Label("Agent asks before every click, keystroke, and scroll. Stop cancels the current task. Keep sensitive windows closed while sharing your screen.", 10));
        _denyExtraBox = Field(privacy, "Extra protected window titles · separated by commas", settings.AgentDenyListExtra ?? "");

        AddHeading(advanced, "For troubleshooting", "You can leave these at their defaults.");
        _useProxy = new CheckBox { Text = "Use a localhost proxy for OpenRouter", Checked = settings.UseLocalProxy, AutoSize = true };
        advanced.Controls.Add(_useProxy);
        _proxyBox = Field(advanced, "Local proxy address · only localhost or loopback IP", settings.LocalProxyBaseUrl);
        _forceGdi = new CheckBox { Text = "Use compatibility screen capture (GDI)", Checked = settings.ForceGdiCapture, AutoSize = true, Margin = new Padding(0,6,0,12) };
        advanced.Controls.Add(_forceGdi);
        _debugTranscriptBox = Field(advanced, "Fixture transcript · only for offline test launches", settings.DebugTranscriptOverride ?? "");
        _logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 550, Height = 130, Font = new Font("Consolas",9), AccessibleName = "Diagnostic log" };
        advanced.Controls.Add(_logBox);
        var refresh = AppTheme.Button("Refresh diagnostics");
        refresh.Click += (_, _) => _logBox.Text = DebugLog.Snapshot();
        advanced.Controls.Add(refresh);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 68, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(20,12,20,8) };
        var apply = AppTheme.Button("Save changes", true);
        apply.Click += (_, _) => Save();
        var done = AppTheme.Button("Close");
        done.Click += (_, _) => Hide();
        footer.Controls.AddRange(new Control[] { apply, done });
        Controls.Add(tabs);
        Controls.Add(header);
        Controls.Add(footer);
        RefreshChatGptButtons();
        RefreshProviderPanels();
        ResumeLayout(true);
    }

    private static FlowLayoutPanel Page(TabControl tabs, string name)
    {
        var page = new TabPage(name) { BackColor = AppTheme.Background, Padding = new Padding(24,20,16,12) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        page.Controls.Add(flow);
        tabs.TabPages.Add(page);
        return flow;
    }

    private static void AddHeading(FlowLayoutPanel page, string title, string description)
    {
        page.Controls.Add(AppTheme.Label(title, 16, true));
        var detail = AppTheme.Label(description);
        detail.MaximumSize = new Size(530,0);
        page.Controls.Add(detail);
    }

    private static TextBox Field(FlowLayoutPanel page, string label, string value)
    {
        var caption = AppTheme.Label(label, 9, true);
        caption.Margin = new Padding(0,6,0,6);
        page.Controls.Add(caption);
        var box = new TextBox { Text = value, Width = 550, AccessibleName = label, Margin = new Padding(0,0,0,12) };
        page.Controls.Add(box);
        return box;
    }

    private string ChatGptStatusText()
    {
        var credential = ChatGptCredentialStore.Load(_secrets);
        if (credential?.AccessToken is { Length: > 0 })
        {
            var email = credential.Email ?? "your account";
            return credential.HasUsableSession ? $"Connected as {email} · Using ChatGPT plan."
                : $"Connected as {email}. Plan usage is disabled. Continue with ChatGPT to enable it.";
        }

        return "Not signed in. Continue with ChatGPT opens the official OpenAI browser login.";
    }

    private void RefreshChatGptButtons()
    {
        var signedIn = ProviderFactory.IsChatGptSignedIn(_secrets);
        _chatGptStatus.Text = ChatGptStatusText();
        _chatGptSignOut.Enabled = ChatGptCredentialStore.Load(_secrets)?.AccessToken is { Length: > 0 } && !_signInBusy;
        _chatGptSignIn.Enabled = !_signInBusy;
        _refreshModels.Enabled = signedIn && !_signInBusy;
        _chatGptModelBox.Enabled = signedIn && !_signInBusy && _chatGptModelBox.Items.Count > 0;
    }

    private void RefreshProviderPanels()
    {
        _chatGptPanel.Visible = _providerChatGpt.Checked;
        _openRouterPanel.Visible = !_providerChatGpt.Checked;
        if (_providerChatGpt.Checked) _windowsSpeech.Checked = true;
        _windowsSpeech.Enabled = !_providerChatGpt.Checked;
        _transcriptionModelBox.Enabled = !_providerChatGpt.Checked;
    }

    private async Task RefreshModelsAsync()
    {
        if (!ProviderFactory.IsChatGptSignedIn(_secrets)) return;
        _refreshModels.Enabled = false;
        _chatGptStatus.Text = "Connected · loading your available models…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var client = new ChatGptResponsesClient(ct => _chatGptOAuth.GetAccessTokenAsync(_settings, _secrets, ct));
            var models = await client.ListModelsAsync(cts.Token);
            if (models.Count == 0) throw new InvalidOperationException("No models are available for this ChatGPT account.");
            var selected = (_chatGptModelBox.SelectedItem as ChatGptModel)?.Slug ?? _settings.ChatGptModel;
            _chatGptModelBox.DataSource = models.ToList();
            _chatGptModelBox.SelectedItem = models.FirstOrDefault(m => m.Slug == selected)
                ?? models.FirstOrDefault(m => m.Slug == "gpt-5.6-terra") ?? models[0];
            _settings.ChatGptModel = ((ChatGptModel)_chatGptModelBox.SelectedItem!).Slug;
            SettingsStore.Save(_settings);
            RefreshChatGptButtons();
            _onApplied();
        }
        catch (Exception ex)
        {
            _chatGptModelBox.DataSource = null;
            RefreshChatGptButtons();
            _chatGptStatus.Text = "Model list unavailable. " + ex.Message;
            DebugLog.Append($"chatgpt_models_error={ex.GetType().Name}");
        }
    }

    private async Task SignInChatGptAsync()
    {
        if (_signInBusy)
        {
            return;
        }

        _signInBusy = true;
        RefreshChatGptButtons();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var credential = await _chatGptOAuth.SignInAsync(_settings, _secrets, cts.Token).ConfigureAwait(true);
            _providerChatGpt.Checked = true;
            _settings.AuthProvider = AuthProvider.ChatGPT;
            SettingsStore.Save(_settings);
            RefreshChatGptButtons();
            _onApplied();
            if (credential.HasUsableSession)
            {
                await RefreshModelsAsync();
                if (!_settings.ChatGptPlanWelcomeShown)
                {
                    MessageBox.Show("You're using your ChatGPT plan. Eligible Ask and Agent requests use your plan allowance or available credits. Manage usage in ChatGPT settings.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _settings.ChatGptPlanWelcomeShown = true;
                    SettingsStore.Save(_settings);
                }
            }
            else MessageBox.Show("Signed in, but ChatGPT plan usage is disabled. Enable it using Continue with ChatGPT or Manage usage before making AI requests.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Sign-in timed out.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            DebugLog.Append($"chatgpt_signin_error={ex.GetType().Name}");
            MessageBox.Show(ex.Message, "Sign in with ChatGPT failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _signInBusy = false;
            RefreshChatGptButtons();
        }
    }

    private async Task SignOutChatGptAsync()
    {
        if (_signInBusy)
        {
            return;
        }

        _signInBusy = true;
        RefreshChatGptButtons();
        try
        {
            var revoked = await _chatGptOAuth.SignOutAsync(_settings, _secrets).ConfigureAwait(true);
            _chatGptModelBox.DataSource = null;
            SettingsStore.Save(_settings);
            RefreshChatGptButtons();
            _onApplied();
            MessageBox.Show(revoked ? "Signed out of ChatGPT." : "Signed out locally. Remote revocation was not confirmed. Disconnect OpenClicky in ChatGPT settings if needed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Sign out failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _signInBusy = false;
            RefreshChatGptButtons();
        }
    }

    private void ApplyFormToSettings(bool persistSecrets)
    {
        _settings.PushToTalk = _draft;
        _settings.AuthProvider = _providerChatGpt.Checked ? AuthProvider.ChatGPT : AuthProvider.OpenRouter;
        var key = string.IsNullOrWhiteSpace(_apiKeyBox.Text) ? null : _apiKeyBox.Text.Trim();
        if (persistSecrets && _providerOpenRouter.Checked)
        {
            if (string.IsNullOrEmpty(key))
            {
                _secrets.Delete(SecretNames.OpenRouterApiKey);
                _settings.OpenRouterApiKey = null;
            }
            else
            {
                _secrets.Set(SecretNames.OpenRouterApiKey, key);
                _settings.OpenRouterApiKey = key;
            }
        }
        else if (_providerOpenRouter.Checked)
        {
            _settings.OpenRouterApiKey = key;
        }

        _settings.OpenRouterModel = string.IsNullOrWhiteSpace(_modelBox.Text) ? "openai/gpt-4o-mini" : _modelBox.Text.Trim();
        if (_chatGptModelBox.SelectedItem is ChatGptModel model) _settings.ChatGptModel = model.Slug;
        _settings.UseWindowsSpeech = _providerChatGpt.Checked || _windowsSpeech.Checked;
        _settings.SpeakReplies = _speakReplies.Checked;
        _settings.TranscriptionModel = string.IsNullOrWhiteSpace(_transcriptionModelBox.Text) ? "openai/whisper-1" : _transcriptionModelBox.Text.Trim();
        _settings.UseLocalProxy = _useProxy.Checked;
        _settings.LocalProxyBaseUrl = _proxyBox.Text.Trim();
        _settings.DebugTranscriptOverride = string.IsNullOrWhiteSpace(_debugTranscriptBox.Text)
            ? null
            : _debugTranscriptBox.Text.Trim();
        _settings.PersistConversationHistory = _persistHistory.Checked;
        _settings.ForceGdiCapture = _forceGdi.Checked;
        _settings.AgentDenyListExtra = string.IsNullOrWhiteSpace(_denyExtraBox.Text)
            ? null
            : _denyExtraBox.Text.Trim();
        _settings.EnsureChatGptHostId();
    }

    private void Save()
    {
        if (!_draft.TryValidate(out var hotkeyError))
        {
            SetHotkeyHint(hotkeyError, isError: true);
            MessageBox.Show(
                hotkeyError ?? "Invalid hotkey.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _hotkeyBox.Focus();
            return;
        }

        try
        {
            if (_useProxy.Checked) ProviderFactory.ResolveBaseUrl(new AppSettings { UseLocalProxy = true, LocalProxyBaseUrl = _proxyBox.Text });
            ApplyFormToSettings(persistSecrets: true);
            SettingsStore.Save(_settings);
            _onApplied();
            SetHotkeyHint($"Active now: {_draft.DisplayName}.", isError: false);
            MessageBox.Show(
            _settings.AuthProvider == AuthProvider.ChatGPT && !ProviderFactory.IsChatGptSignedIn(_secrets)
                ? "Saved. Click Continue with ChatGPT to connect your account."
                : $"Saved. Push-to-talk is now {_draft.DisplayName}.",
            Text,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not save changes: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    private void HotkeyBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return;
        }

        // Esc clears the draft so Save can reject empty bindings with clear UX.
        if (e.KeyCode == Keys.Escape && !e.Alt && !e.Control && !e.Shift)
        {
            const int vkLWinClear = 0x5B;
            const int vkRWinClear = 0x5C;
            var winHeld =
                (GetAsyncKeyState(vkLWinClear) & 0x8000) != 0
                || (GetAsyncKeyState(vkRWinClear) & 0x8000) != 0;
            if (!winHeld)
            {
                _draft = new HotkeyChord(false, false, false, false, 0);
                _hotkeyBox.Text = _draft.DisplayName;
                SetHotkeyHint("Hotkey cleared. Press a shortcut or Reset default before Save.", isError: true);
                return;
            }
        }

        const int vkLWin = 0x5B;
        const int vkRWin = 0x5C;
        var winDown = (GetAsyncKeyState(vkLWin) & 0x8000) != 0 || (GetAsyncKeyState(vkRWin) & 0x8000) != 0;
        var candidate = new HotkeyChord(winDown, e.Alt, e.Control, e.Shift, e.KeyValue);
        if (!candidate.TryValidate(out var error))
        {
            SetHotkeyHint(error, isError: true);
            return;
        }

        _draft = candidate;
        _hotkeyBox.Text = _draft.DisplayName;
        SetHotkeyHint($"Will use {_draft.DisplayName} after Save.", isError: false);
    }

    private void SetHotkeyHint(string? message, bool isError = false)
    {
        _hotkeyHint.Text = string.IsNullOrWhiteSpace(message)
            ? "Default: Win+Alt+Space. Needs a modifier + key. Esc clears."
            : message;
        _hotkeyHint.ForeColor = isError ? Color.Firebrick : SystemColors.GrayText;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Drop unsaved capture edits when the dialog is reopened.
        _draft = HotkeyChord.NormalizeOrDefault(_settings.PushToTalk);
        _hotkeyBox.Text = _draft.DisplayName;
        SetHotkeyHint(null);
        _logBox.Text = DebugLog.Snapshot();
        RefreshChatGptButtons();
        await RefreshModelsAsync();
    }
}
