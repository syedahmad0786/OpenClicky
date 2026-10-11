using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly Action _onApplied;
    private readonly TextBox _hotkeyBox;
    private readonly TextBox _proxyBox;
    private readonly CheckBox _useProxy;
    private readonly TextBox _apiKeyBox;
    private readonly TextBox _modelBox;
    private readonly TextBox _debugTranscriptBox;
    private readonly CheckBox _persistHistory;
    private readonly CheckBox _forceGdi;
    private readonly TextBox _denyExtraBox;
    private readonly TextBox _logBox;
    private readonly Action? _clearHistory;
    private HotkeyChord _draft;

    public SettingsForm(AppSettings settings, ISecretStore secrets, Action onApplied, Action? onClearHistory = null)
    {
        _settings = settings;
        _secrets = secrets;
        _onApplied = onApplied;
        _clearHistory = onClearHistory;
        _draft = settings.PushToTalk;

        Text = "OpenClicky Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 660);

        var y = 16;
        Controls.Add(LabelAt(y, "Push-to-talk hotkey (click field, then press the combo):"));
        y += 22;
        _hotkeyBox = new TextBox { Left = 16, Top = y, Width = 480, ReadOnly = true, Text = _draft.DisplayName };
        _hotkeyBox.KeyDown += HotkeyBoxOnKeyDown;
        Controls.Add(_hotkeyBox);
        y += 36;

        Controls.Add(LabelAt(y, "OpenRouter API key (Windows Credential Manager — not written to settings.json):"));
        y += 22;
        _apiKeyBox = new TextBox
        {
            Left = 16,
            Top = y,
            Width = 480,
            UseSystemPasswordChar = true,
            Text = _settings.OpenRouterApiKey ?? string.Empty,
        };
        Controls.Add(_apiKeyBox);
        y += 36;

        Controls.Add(LabelAt(y, "Vision model (OpenRouter id):"));
        y += 22;
        _modelBox = new TextBox { Left = 16, Top = y, Width = 480, Text = _settings.OpenRouterModel };
        Controls.Add(_modelBox);
        y += 36;

        _useProxy = new CheckBox
        {
            Left = 16,
            Top = y,
            Width = 480,
            Text = "Use local proxy",
            Checked = _settings.UseLocalProxy,
        };
        Controls.Add(_useProxy);
        y += 28;

        Controls.Add(LabelAt(y, "Local proxy base URL:"));
        y += 22;
        _proxyBox = new TextBox { Left = 16, Top = y, Width = 480, Text = _settings.LocalProxyBaseUrl };
        Controls.Add(_proxyBox);
        y += 36;

        Controls.Add(LabelAt(y, "Debug transcript override (skips mic/STT when set):"));
        y += 22;
        _debugTranscriptBox = new TextBox
        {
            Left = 16,
            Top = y,
            Width = 480,
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
        var clearHistoryButton = new Button { Left = 340, Top = y - 2, Width = 156, Text = "Clear history" };
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
            Width = 480,
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
            Width = 480,
            Text = _settings.AgentDenyListExtra ?? string.Empty,
        };
        Controls.Add(_denyExtraBox);
        y += 36;

        _logBox = new TextBox
        {
            Left = 16,
            Top = y + 40,
            Width = 480,
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
        Controls.Add(apply);
        Controls.Add(refreshLog);
        Controls.Add(_logBox);
    }

    private static Label LabelAt(int top, string text) =>
        new() { Left = 16, Top = top, Width = 480, Text = text };

    private void Save()
    {
        _settings.PushToTalk = _draft;
        var key = string.IsNullOrWhiteSpace(_apiKeyBox.Text) ? null : _apiKeyBox.Text.Trim();
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

        _settings.OpenRouterModel = string.IsNullOrWhiteSpace(_modelBox.Text) ? "openai/gpt-4o-mini" : _modelBox.Text.Trim();
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
    }
}
