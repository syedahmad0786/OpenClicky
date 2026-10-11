using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.App;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly Action _onApplied;
    private readonly TextBox _hotkeyBox;
    private readonly TextBox _proxyBox;
    private readonly CheckBox _useProxy;
    private readonly RadioButton _providerOpenRouter;
    private readonly RadioButton _providerChatGpt;
    private readonly TextBox _apiKeyBox;
    private readonly TextBox _modelBox;
    private readonly TextBox _chatGptModelBox;
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

    public SettingsForm(AppSettings settings, ISecretStore secrets, Action onApplied, Action? onClearHistory = null)
    {
        _settings = settings;
        _secrets = secrets;
        _onApplied = onApplied;
        _clearHistory = onClearHistory;
        _draft = settings.PushToTalk;
        _settings.EnsureChatGptHostId();

        Text = "OpenClicky Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 820);
        AutoScroll = true;

        var y = 16;
        Controls.Add(LabelAt(y, "Push-to-talk hotkey (click field, then press the combo):"));
        y += 22;
        _hotkeyBox = new TextBox { Left = 16, Top = y, Width = 500, ReadOnly = true, Text = _draft.DisplayName };
        _hotkeyBox.KeyDown += HotkeyBoxOnKeyDown;
        Controls.Add(_hotkeyBox);
        y += 36;

        Controls.Add(LabelAt(y, "Talk / Agent provider:"));
        y += 22;
        _providerOpenRouter = new RadioButton
        {
            Left = 16,
            Top = y,
            Width = 240,
            Text = "OpenRouter API key",
            Checked = _settings.AuthProvider != AuthProvider.ChatGPT,
        };
        _providerChatGpt = new RadioButton
        {
            Left = 260,
            Top = y,
            Width = 250,
            Text = "ChatGPT account",
            Checked = _settings.AuthProvider == AuthProvider.ChatGPT,
        };
        Controls.Add(_providerOpenRouter);
        Controls.Add(_providerChatGpt);
        y += 32;

        Controls.Add(LabelAt(y, "OpenRouter API key (Credential Manager — not written to settings.json):"));
        y += 22;
        _apiKeyBox = new TextBox
        {
            Left = 16,
            Top = y,
            Width = 500,
            UseSystemPasswordChar = true,
            Text = _settings.OpenRouterApiKey ?? string.Empty,
        };
        Controls.Add(_apiKeyBox);
        y += 36;

        Controls.Add(LabelAt(y, "OpenRouter vision model id:"));
        y += 22;
        _modelBox = new TextBox { Left = 16, Top = y, Width = 500, Text = _settings.OpenRouterModel };
        Controls.Add(_modelBox);
        y += 36;

        Controls.Add(LabelAt(y, "Sign in with ChatGPT (official OAuth / PKCE — opens your browser):"));
        y += 22;
        _chatGptStatus = new Label
        {
            Left = 16,
            Top = y,
            Width = 500,
            Height = 36,
            Text = ChatGptStatusText(),
        };
        Controls.Add(_chatGptStatus);
        y += 40;

        _chatGptSignIn = new Button { Left = 16, Top = y, Width = 200, Text = "Continue with ChatGPT" };
        _chatGptSignIn.Click += async (_, _) => await SignInChatGptAsync();
        _chatGptSignOut = new Button { Left = 228, Top = y, Width = 120, Text = "Sign out" };
        _chatGptSignOut.Click += async (_, _) => await SignOutChatGptAsync();
        Controls.Add(_chatGptSignIn);
        Controls.Add(_chatGptSignOut);
        y += 36;

        Controls.Add(LabelAt(y, "ChatGPT model slug (Responses API):"));
        y += 22;
        _chatGptModelBox = new TextBox { Left = 16, Top = y, Width = 500, Text = _settings.ChatGptModel };
        Controls.Add(_chatGptModelBox);
        y += 36;

        _useProxy = new CheckBox
        {
            Left = 16,
            Top = y,
            Width = 500,
            Text = "Use local proxy (OpenRouter path only)",
            Checked = _settings.UseLocalProxy,
        };
        Controls.Add(_useProxy);
        y += 28;

        Controls.Add(LabelAt(y, "Local proxy base URL:"));
        y += 22;
        _proxyBox = new TextBox { Left = 16, Top = y, Width = 500, Text = _settings.LocalProxyBaseUrl };
        Controls.Add(_proxyBox);
        y += 36;

        Controls.Add(LabelAt(y, "Debug transcript override (skips mic/STT when set):"));
        y += 22;
        _debugTranscriptBox = new TextBox
        {
            Left = 16,
            Top = y,
            Width = 500,
            Text = _settings.DebugTranscriptOverride ?? string.Empty,
        };
        Controls.Add(_debugTranscriptBox);
        y += 36;

        _persistHistory = new CheckBox
        {
            Left = 16,
            Top = y,
            Width = 320,
            Text = "Persist conversation history (text only)",
            Checked = _settings.PersistConversationHistory,
        };
        Controls.Add(_persistHistory);
        var clearHistoryButton = new Button { Left = 340, Top = y - 2, Width = 176, Text = "Clear history" };
        clearHistoryButton.Click += (_, _) =>
        {
            _clearHistory?.Invoke();
            MessageBox.Show("Conversation history cleared.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Controls.Add(clearHistoryButton);
        y += 32;

        _forceGdi = new CheckBox
        {
            Left = 16,
            Top = y,
            Width = 500,
            Text = "Force GDI capture (skip Windows.Graphics.Capture)",
            Checked = _settings.ForceGdiCapture,
        };
        Controls.Add(_forceGdi);
        y += 28;

        Controls.Add(LabelAt(y, "Extra agent deny-list fragments (comma-separated window titles):"));
        y += 22;
        _denyExtraBox = new TextBox
        {
            Left = 16,
            Top = y,
            Width = 500,
            Text = _settings.AgentDenyListExtra ?? string.Empty,
        };
        Controls.Add(_denyExtraBox);
        y += 36;

        _logBox = new TextBox
        {
            Left = 16,
            Top = y + 40,
            Width = 500,
            Height = 100,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 8f),
        };

        var apply = new Button { Left = 16, Top = y, Width = 120, Text = "Save" };
        apply.Click += (_, _) => Save();
        var refreshLog = new Button { Left = 152, Top = y, Width = 120, Text = "Refresh log" };
        refreshLog.Click += (_, _) => _logBox.Text = DebugLog.Snapshot();
        var openLog = new Button { Left = 288, Top = y, Width = 140, Text = "Open log folder" };
        openLog.Click += (_, _) =>
        {
            try
            {
                var dir = Path.GetDirectoryName(DebugLog.LogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true,
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        Controls.Add(apply);
        Controls.Add(refreshLog);
        Controls.Add(openLog);
        Controls.Add(_logBox);
        RefreshChatGptButtons();
    }

    private string ChatGptStatusText()
    {
        var signedIn = ProviderFactory.IsChatGptSignedIn(_secrets);
        if (signedIn)
        {
            var email = _settings.ChatGptAccountEmail ?? "(signed in)";
            return $"Signed in as {email}. Tokens in OS secret store (DPAPI / Credential Manager).";
        }

        return "Not signed in. Continue with ChatGPT opens the official OpenAI browser login.";
    }

    private void RefreshChatGptButtons()
    {
        var signedIn = ProviderFactory.IsChatGptSignedIn(_secrets);
        _chatGptStatus.Text = ChatGptStatusText();
        _chatGptSignOut.Enabled = signedIn && !_signInBusy;
        _chatGptSignIn.Enabled = !_signInBusy;
        _chatGptSignIn.Text = signedIn ? "Re-authorize ChatGPT" : "Continue with ChatGPT";
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
            ApplyFormToSettings(persistSecrets: true);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _chatGptOAuth.SignInAsync(_settings, _secrets, cts.Token).ConfigureAwait(true);
            _providerChatGpt.Checked = true;
            RefreshChatGptButtons();
            _onApplied();
            MessageBox.Show(
                "Signed in with ChatGPT. Talk/Agent will use your ChatGPT plan when that provider is selected.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Sign-in timed out.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            DebugLog.Append($"chatgpt_signin_error={ex.Message}");
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
            await _chatGptOAuth.SignOutAsync(_settings, _secrets).ConfigureAwait(true);
            _providerOpenRouter.Checked = true;
            RefreshChatGptButtons();
            _onApplied();
            MessageBox.Show("Signed out of ChatGPT.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private static Label LabelAt(int top, string text) =>
        new() { Left = 16, Top = top, Width = 500, Text = text };

    private void ApplyFormToSettings(bool persistSecrets)
    {
        _settings.PushToTalk = _draft;
        _settings.AuthProvider = _providerChatGpt.Checked ? AuthProvider.ChatGPT : AuthProvider.OpenRouter;
        var key = string.IsNullOrWhiteSpace(_apiKeyBox.Text) ? null : _apiKeyBox.Text.Trim();
        if (persistSecrets)
        {
            if (string.IsNullOrEmpty(key))
            {
                try
                {
                    _secrets.Delete(SecretNames.OpenRouterApiKey);
                }
                catch (Exception ex)
                {
                    DebugLog.Append($"secret_delete_error={ex.Message}");
                }

                _settings.OpenRouterApiKey = null;
            }
            else
            {
                _secrets.Set(SecretNames.OpenRouterApiKey, key);
                _settings.OpenRouterApiKey = key;
            }
        }
        else
        {
            _settings.OpenRouterApiKey = key;
        }

        _settings.OpenRouterModel = string.IsNullOrWhiteSpace(_modelBox.Text) ? "openai/gpt-4o-mini" : _modelBox.Text.Trim();
        _settings.ChatGptModel = string.IsNullOrWhiteSpace(_chatGptModelBox.Text) ? "gpt-4o" : _chatGptModelBox.Text.Trim();
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
        ApplyFormToSettings(persistSecrets: true);
        if (_settings.AuthProvider == AuthProvider.ChatGPT && !ProviderFactory.IsChatGptSignedIn(_secrets))
        {
            MessageBox.Show(
                "ChatGPT provider selected but you are not signed in. Click Continue with ChatGPT, or switch back to OpenRouter.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        SettingsStore.Save(_settings);
        _onApplied();
        MessageBox.Show("Saved.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void HotkeyBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return;
        }

        const int vkLWin = 0x5B;
        const int vkRWin = 0x5C;
        var winDown = (GetAsyncKeyState(vkLWin) & 0x8000) != 0 || (GetAsyncKeyState(vkRWin) & 0x8000) != 0;
        _draft = new HotkeyChord(winDown, e.Alt, e.Control, e.Shift, e.KeyValue);
        _hotkeyBox.Text = _draft.DisplayName;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _logBox.Text = DebugLog.Snapshot();
        RefreshChatGptButtons();
    }
}
