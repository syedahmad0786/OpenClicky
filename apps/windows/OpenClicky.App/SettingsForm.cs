using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action<HotkeyChord> _onHotkeyChanged;
    private readonly TextBox _hotkeyBox;
    private readonly TextBox _proxyBox;
    private readonly TextBox _logBox;
    private HotkeyChord _draft;

    public SettingsForm(AppSettings settings, Action<HotkeyChord> onHotkeyChanged)
    {
        _settings = settings;
        _onHotkeyChanged = onHotkeyChanged;
        _draft = settings.PushToTalk;

        Text = "OpenClicky Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 360);

        var hotkeyLabel = new Label { Left = 16, Top = 16, Width = 440, Text = "Push-to-talk hotkey (click field, then press the combo):" };
        _hotkeyBox = new TextBox
        {
            Left = 16,
            Top = 40,
            Width = 440,
            ReadOnly = true,
            Text = _draft.DisplayName,
        };
        _hotkeyBox.KeyDown += HotkeyBoxOnKeyDown;

        var proxyLabel = new Label { Left = 16, Top = 80, Width = 440, Text = "Local proxy base URL (optional, Phase B):" };
        _proxyBox = new TextBox
        {
            Left = 16,
            Top = 104,
            Width = 440,
            Text = _settings.LocalProxyBaseUrl,
        };

        var apply = new Button { Left = 16, Top = 140, Width = 120, Text = "Apply hotkey" };
        apply.Click += (_, _) =>
        {
            _settings.LocalProxyBaseUrl = _proxyBox.Text.Trim();
            _onHotkeyChanged(_draft);
            _hotkeyBox.Text = _draft.DisplayName;
            MessageBox.Show($"Hotkey set to {_draft.DisplayName}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var refreshLog = new Button { Left = 152, Top = 140, Width = 120, Text = "Refresh log" };
        _logBox = new TextBox
        {
            Left = 16,
            Top = 180,
            Width = 440,
            Height = 160,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 8f),
        };
        refreshLog.Click += (_, _) => _logBox.Text = DebugLog.Snapshot();

        Controls.Add(hotkeyLabel);
        Controls.Add(_hotkeyBox);
        Controls.Add(proxyLabel);
        Controls.Add(_proxyBox);
        Controls.Add(apply);
        Controls.Add(refreshLog);
        Controls.Add(_logBox);
    }

    private void HotkeyBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        // Ignore pure modifier presses while recording a chord.
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return;
        }

        // Win is awkward via KeyEventArgs — sample async key state.
        const int vkLWin = 0x5B;
        const int vkRWin = 0x5C;
        var winDown = (GetAsyncKeyState(vkLWin) & 0x8000) != 0 || (GetAsyncKeyState(vkRWin) & 0x8000) != 0;

        _draft = new HotkeyChord(
            Win: winDown,
            Alt: e.Alt,
            Control: e.Control,
            Shift: e.Shift,
            VirtualKey: e.KeyValue);

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
