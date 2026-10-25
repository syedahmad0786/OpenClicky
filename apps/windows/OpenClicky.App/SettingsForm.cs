using OpenClicky.App.Settings;
using OpenClicky.Core;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Settings;
using OpenClicky.Providers;
using OpenClicky.Input;

namespace OpenClicky.App;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly Action _onApplied;
    private readonly TextBox _proxyBox;
    private readonly CheckBox _useProxy;
    private readonly RadioButton _providerOpenRouter;
    private readonly RadioButton _providerChatGpt;
    private readonly TextBox _apiKeyBox;
    private readonly Button _removeKey;
    private readonly TextBox _modelBox;
    private readonly ComboBox _chatGptModelBox;
    private readonly ComboBox _quickModelBox;
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
    private bool _signInBusy;
    private readonly CheckBox _windowsSpeech;
    private readonly CheckBox _speakReplies;
    private readonly TextBox _transcriptionModelBox;
    private readonly ComboBox _microphoneBox;
    private readonly ProgressBar _microphoneLevel;
    private readonly Button _testMicrophone;
    private readonly Label _voiceModelStatus;
    private NAudioMicrophoneRecorder? _microphoneTest;
    private readonly System.Windows.Forms.Timer _meterTimer = new() { Interval = 100 };
    private readonly CancellationTokenSource _voiceDownload = new();

    public SettingsForm(AppSettings settings, ISecretStore secrets, Action onApplied, Action? onClearHistory = null)
    {
        _settings = settings;
        _secrets = secrets;
        _onApplied = onApplied;
        _clearHistory = onClearHistory;
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
        // The saved key is never shown: the box only takes a new key. Empty keeps the saved one; Remove key deletes it.
        _apiKeyBox = Field(_openRouterPanel, "OpenRouter API key", "");
        _apiKeyBox.UseSystemPasswordChar = true;
        _apiKeyBox.AccessibleName = "OpenRouter API key";
        _removeKey = AppTheme.Button("Remove key");
        _removeKey.Click += (_, _) => RemoveOpenRouterKey();
        _openRouterPanel.Controls.Add(_removeKey);
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
        _chatGptPanel.Controls.Add(AppTheme.Label("Quick screen replies · Agent keeps the model above", 9, true));
        _quickModelBox = new ComboBox { Width = 550, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(ChatGptModel.DisplayName), AccessibleName = "Quick reply model", Margin = new Padding(0,6,0,12) };
        _chatGptPanel.Controls.Add(_quickModelBox);
        var usageButtons = new FlowLayoutPanel { Width = 560, AutoSize = true, Margin = Padding.Empty };
        _refreshModels = AppTheme.Button("Refresh models");
        _refreshModels.Click += async (_, _) => await RefreshModelsAsync();
        var manageUsage = AppTheme.Button("Manage usage");
        manageUsage.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(NativeSettings.UsageUrl) { UseShellExecute = true });
        usageButtons.Controls.AddRange(new Control[] { _refreshModels, manageUsage });
        _chatGptPanel.Controls.Add(usageButtons);
        var speechNote = AppTheme.Label("Your ChatGPT plan powers Ask and Agent. Voice recognition and spoken replies run locally on Windows. Sign-in does not give OpenClicky access to your ChatGPT conversations.", 9);
        speechNote.MaximumSize = new Size(530, 0);
        _chatGptPanel.Controls.Add(speechNote);
        _providerChatGpt.CheckedChanged += (_, _) => RefreshProviderPanels();

        AddHeading(voice, "Talk from any app", "Click Start conversation in Home for hands-free turns, or hold your shortcut for one request. You can always type.");
        voice.Controls.Add(AppTheme.Label("Microphone · choose the input you actually use", 9, true));
        _microphoneBox = new ComboBox { Width = 550, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(MicrophoneDevice.Name), AccessibleName = "Microphone input", Margin = new Padding(0,6,0,8) };
        voice.Controls.Add(_microphoneBox);
        var microphoneButtons = new FlowLayoutPanel { AutoSize = true, Width = 550, Margin = Padding.Empty };
        var refreshMicrophones = AppTheme.Button("Refresh microphones");
        refreshMicrophones.Click += (_, _) => RefreshMicrophones();
        _testMicrophone = AppTheme.Button("Test microphone");
        _testMicrophone.Click += (_, _) => TestMicrophone();
        microphoneButtons.Controls.AddRange(new Control[] { _testMicrophone, refreshMicrophones });
        voice.Controls.Add(microphoneButtons);
        _microphoneLevel = new ProgressBar { Width = 550, Height = 14, AccessibleName = "Microphone input level", Margin = new Padding(0,6,0,8) };
        voice.Controls.Add(_microphoneLevel);
        voice.Controls.Add(AppTheme.Label("While testing, speak and watch the level move. Test audio is discarded. Save your selection below.", 9));
        _meterTimer.Tick += (_, _) => _microphoneLevel.Value = Math.Clamp((int)((_microphoneTest?.PeakLevel ?? 0) * 100), 0, 100);
        _microphoneBox.SelectedIndexChanged += (_, _) => StopMicrophoneTest();
        // Built once but shown many times: reload every control from the live settings each time it opens, so Save
        // never writes back values that were changed elsewhere (the notch settings page) while it was hidden.
        VisibleChanged += async (_, _) =>
        {
            if (!Visible) { StopMicrophoneTest(); return; }
            LoadFromSettings();
            // LoadFromSettings shows a model list from this session (classic or notch); otherwise fetch it as before.
            if (NativeSettings.CachedModels.Count == 0) await RefreshModelsAsync();
        };
        Disposed += (_, _) => { StopMicrophoneTest(); _meterTimer.Dispose(); _voiceDownload.Cancel(); _voiceDownload.Dispose(); };
        RefreshMicrophones();
        _voiceModelStatus = AppTheme.Label(LocalWhisperModel.IsAvailable(WhisperModels.English) ? "Whisper is ready · English speech stays on this PC." : "Compatibility voice is active. Download Whisper for stronger recognition and live previews.", 9);
        _voiceModelStatus.MaximumSize = new Size(530, 0);
        voice.Controls.Add(_voiceModelStatus);
        var downloadVoice = AppTheme.Button("Download accurate voice · 142 MB");
        downloadVoice.Click += async (_, _) =>
        {
            downloadVoice.Enabled = false;
            try
            {
                await LocalWhisperModel.DownloadAsync(WhisperModels.English, new Progress<int>(percent => { if (!IsDisposed) _voiceModelStatus.Text = $"Downloading local voice: {percent}%"; }), _voiceDownload.Token);
                if (IsDisposed) return;
                _voiceModelStatus.Text = "Whisper is ready · English speech stays on this PC.";
                _onApplied();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) _voiceModelStatus.Text = "Download failed: " + ex.Message; }
            finally { if (!IsDisposed) downloadVoice.Enabled = true; }
        };
        voice.Controls.Add(downloadVoice);
        voice.Controls.Add(AppTheme.Label("Change shortcuts in Settings → Shortcuts", 9));
        _windowsSpeech = new CheckBox { Text = "Use local speech recognition", Checked = settings.UseWindowsSpeech, AutoSize = true, Margin = new Padding(0,12,0,10) };
        voice.Controls.Add(_windowsSpeech);
        string languages;
        try { languages = string.Join(", ", LocalSpeechSttClient.InstalledLanguages()); }
        catch { languages = ""; }
        voice.Controls.Add(AppTheme.Label(string.IsNullOrWhiteSpace(languages) ? "Download Whisper for local speech. ChatGPT sign-in does not include cloud transcription." : "Windows compatibility language: " + languages + ". Whisper is used when downloaded; compatibility words appear after Send.", 9));
        _transcriptionModelBox = Field(voice, "OpenRouter transcription model · used when local speech is off", settings.TranscriptionModel);
        _speakReplies = new CheckBox { Text = "Read replies aloud using a Windows voice", Checked = settings.SpeakReplies, AutoSize = true, Margin = new Padding(0,12,0,10) };
        voice.Controls.Add(_speakReplies);

        AddHeading(privacy, "You stay in charge", "Screen questions share every connected display (up to four, the one with your cursor first) with OpenClicky's own windows left out, or only the area you select with your AI provider. Tasks may inspect more of the desktop. Images stay in memory.");
        privacy.Controls.Add(AppTheme.Label("There is no OpenClicky cloud account. Keys and ChatGPT tokens use Windows Credential Manager or encrypted DPAPI storage.", 10));
        _persistHistory = new CheckBox { Text = "Remember conversations and task history · text only", Checked = settings.PersistConversationHistory, AutoSize = true, Margin = new Padding(0,10,0,12) };
        privacy.Controls.Add(_persistHistory);
        var clear = AppTheme.Button("Clear conversation and task history");
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show("Clear saved conversations and task history? Notes and explicit memories are kept.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                _clearHistory?.Invoke();
        };
        privacy.Controls.Add(clear);
        privacy.Controls.Add(AppTheme.Label("Approve each action is the default. Your task access mode controls approval prompts. Stop cancels the current task. Close sensitive windows before sharing screens.", 10));
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
        _quickModelBox.Enabled = signedIn && !_signInBusy && _quickModelBox.Items.Count > 0;
    }

    private void RefreshProviderPanels()
    {
        _chatGptPanel.Visible = _providerChatGpt.Checked;
        _openRouterPanel.Visible = !_providerChatGpt.Checked;
        if (_providerChatGpt.Checked) _windowsSpeech.Checked = true;
        _windowsSpeech.Enabled = !_providerChatGpt.Checked;
        _transcriptionModelBox.Enabled = !_providerChatGpt.Checked;
    }

    /// <summary>Fills both model lists with the saved choices selected; changes nothing in the settings.</summary>
    private void ShowModels(IReadOnlyList<ChatGptModel> models)
    {
        _chatGptModelBox.DataSource = models.ToList();
        _chatGptModelBox.SelectedItem = NativeSettings.PickModel(models, _settings.ChatGptModel);
        var quick = new List<ChatGptModel> { new("", "Use the same model") };
        quick.AddRange(models);
        _quickModelBox.DataSource = quick;
        _quickModelBox.SelectedItem = quick.FirstOrDefault(m => m.Slug == _settings.ChatGptQuickModel) ?? quick[0];
        RefreshChatGptButtons();
    }

    private async Task RefreshModelsAsync()
    {
        if (!ProviderFactory.IsChatGptSignedIn(_secrets)) return;
        _refreshModels.Enabled = false;
        _chatGptStatus.Text = "Connected · loading your available models…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var models = await NativeSettings.ListModelsAsync(_settings, _secrets, cts.Token);
            var selected = (_chatGptModelBox.SelectedItem as ChatGptModel)?.Slug ?? _settings.ChatGptModel;
            _chatGptModelBox.DataSource = models.ToList();
            _chatGptModelBox.SelectedItem = NativeSettings.PickModel(models, selected);
            _settings.ChatGptModel = ((ChatGptModel)_chatGptModelBox.SelectedItem!).Slug;
            var quick = new List<ChatGptModel> { new("", "Use the same model") };
            quick.AddRange(models);
            _quickModelBox.DataSource = quick;
            _quickModelBox.SelectedItem = quick.FirstOrDefault(m => m.Slug == _settings.ChatGptQuickModel) ?? quick[0];
            SettingsStore.Save(_settings);
            RefreshChatGptButtons();
            _onApplied();
        }
        catch (Exception ex)
        {
            _chatGptModelBox.DataSource = null;
            _quickModelBox.DataSource = null;
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
            var credential = await NativeSettings.OAuth.SignInAsync(_settings, _secrets, cts.Token).ConfigureAwait(true);
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
            var revoked = await NativeSettings.OAuth.SignOutAsync(_settings, _secrets).ConfigureAwait(true);
            NativeSettings.ForgetModels();
            _chatGptModelBox.DataSource = null;
            _quickModelBox.DataSource = null;
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

    private void RefreshMicrophones()
    {
        var selected = _microphoneBox.SelectedItem as MicrophoneDevice;
        var id = selected is null ? _settings.MicrophoneDeviceId : selected.Id;
        StopMicrophoneTest();
        _microphoneBox.Items.Clear();
        try
        {
            foreach (var device in NAudioMicrophoneRecorder.ListDevices()) _microphoneBox.Items.Add(device);
            var match = _microphoneBox.Items.Cast<MicrophoneDevice>().FirstOrDefault(d => d.Id == id);
            if (match is null && id is not null)
            {
                match = new(id, "Saved microphone is disconnected · choose another");
                _microphoneBox.Items.Add(match);
            }
            _microphoneBox.SelectedItem = match ?? _microphoneBox.Items[0];
        }
        catch (Exception ex) { MessageBox.Show("Could not list microphones: " + ex.Message, Text); }
    }

    private void TestMicrophone()
    {
        if (_microphoneTest is not null) { StopMicrophoneTest(); return; }
        var id = (_microphoneBox.SelectedItem as MicrophoneDevice)?.Id;
        try
        {
            _microphoneTest = new NAudioMicrophoneRecorder(() => id);
            _microphoneTest.Start();
            _testMicrophone.Text = "Stop microphone test";
            _meterTimer.Start();
        }
        catch (Exception ex) { StopMicrophoneTest(); MessageBox.Show("Could not open this microphone: " + ex.Message, Text); }
    }

    private void StopMicrophoneTest()
    {
        _meterTimer.Stop();
        _microphoneTest?.Dispose();
        _microphoneTest = null;
        _testMicrophone.Text = "Test microphone";
        _microphoneLevel.Value = 0;
    }

    /// <summary>Every control from the live settings; the model lists from the session cache (empty until fetched).</summary>
    private void LoadFromSettings()
    {
        _providerChatGpt.Checked = _settings.AuthProvider == AuthProvider.ChatGPT;
        _providerOpenRouter.Checked = !_providerChatGpt.Checked;
        _apiKeyBox.Text = "";
        var keySaved = !string.IsNullOrWhiteSpace(_settings.OpenRouterApiKey);
        _apiKeyBox.PlaceholderText = keySaved ? "Saved — leave empty to keep" : "Paste your OpenRouter API key";
        _removeKey.Enabled = keySaved;
        _modelBox.Text = _settings.OpenRouterModel;
        if (NativeSettings.CachedModels is { Count: > 0 } models) ShowModels(models);
        else { _chatGptModelBox.DataSource = null; _quickModelBox.DataSource = null; }
        _windowsSpeech.Checked = _settings.UseWindowsSpeech;
        _transcriptionModelBox.Text = _settings.TranscriptionModel;
        _speakReplies.Checked = _settings.SpeakReplies;
        _persistHistory.Checked = _settings.PersistConversationHistory;
        _denyExtraBox.Text = _settings.AgentDenyListExtra ?? "";
        _useProxy.Checked = _settings.UseLocalProxy;
        _proxyBox.Text = _settings.LocalProxyBaseUrl;
        _forceGdi.Checked = _settings.ForceGdiCapture;
        _debugTranscriptBox.Text = _settings.DebugTranscriptOverride ?? "";
        _microphoneBox.Items.Clear(); // so RefreshMicrophones selects the saved device, not the old selection
        RefreshMicrophones();
        _voiceModelStatus.Text = LocalWhisperModel.IsAvailable(WhisperModels.English) ? "Whisper is ready · English speech stays on this PC." : "Compatibility voice is active. Download Whisper for stronger recognition and live previews.";
        _logBox.Text = DebugLog.Snapshot();
        RefreshChatGptButtons();
        RefreshProviderPanels();
    }

    /// <summary>The explicit way to delete the saved OpenRouter key (an empty box never deletes it).</summary>
    private void RemoveOpenRouterKey()
    {
        if (MessageBox.Show("Remove the saved OpenRouter API key from this PC?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            _secrets.Delete(SecretNames.OpenRouterApiKey);
            _settings.OpenRouterApiKey = null;
            _apiKeyBox.Text = "";
            _apiKeyBox.PlaceholderText = "Paste your OpenRouter API key";
            _removeKey.Enabled = false;
            _onApplied();
        }
        catch (Exception ex) { MessageBox.Show("Could not remove the key: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ApplyFormToSettings(bool persistSecrets)
    {
        _settings.AuthProvider = _providerChatGpt.Checked ? AuthProvider.ChatGPT : AuthProvider.OpenRouter;
        // Only a newly typed key is saved; empty keeps the saved key (Remove key deletes it).
        if (SettingsActionArgs.TypedSecret(_apiKeyBox.Text) is { } key && _providerOpenRouter.Checked)
        {
            if (persistSecrets) _secrets.Set(SecretNames.OpenRouterApiKey, key);
            _settings.OpenRouterApiKey = key;
        }

        _settings.OpenRouterModel = string.IsNullOrWhiteSpace(_modelBox.Text) ? "openai/gpt-4o-mini" : _modelBox.Text.Trim();
        if (_chatGptModelBox.SelectedItem is ChatGptModel model) _settings.ChatGptModel = model.Slug;
        if (_quickModelBox.SelectedItem is ChatGptModel quick) _settings.ChatGptQuickModel = quick.Slug;
        _settings.UseWindowsSpeech = _providerChatGpt.Checked || _windowsSpeech.Checked;
        _settings.MicrophoneDeviceId = (_microphoneBox.SelectedItem as MicrophoneDevice)?.Id;
        _settings.SpeakReplies = _speakReplies.Checked;
        _settings.TranscriptionModel = string.IsNullOrWhiteSpace(_transcriptionModelBox.Text) ? "openai/whisper-1" : _transcriptionModelBox.Text.Trim();
        _settings.UseLocalProxy = _useProxy.Checked;
        // The notch applies the same rule (SettingsPatch): an address that is not loopback keeps the saved one and the hint under the box.
        if (SettingsCatalog.LoopbackUrl(_proxyBox.Text) is { } proxy) _settings.LocalProxyBaseUrl = proxy;
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
        StopMicrophoneTest();
        try
        {
            if (_useProxy.Checked) ProviderFactory.ResolveBaseUrl(new AppSettings { UseLocalProxy = true, LocalProxyBaseUrl = _proxyBox.Text });
            ApplyFormToSettings(persistSecrets: true);
            SettingsStore.Save(_settings);
            LoadFromSettings(); // the typed key is now saved: clear the box and show "Saved"
            _onApplied();
            MessageBox.Show(
            _settings.AuthProvider == AuthProvider.ChatGPT && !ProviderFactory.IsChatGptSignedIn(_secrets)
                ? "Saved. Click Continue with ChatGPT to connect your account."
                : "Saved.",
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
}
