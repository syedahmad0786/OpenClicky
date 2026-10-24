using System.Runtime.InteropServices;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.App.Settings;

/// <summary>
/// Native settings flows shared by the classic <see cref="SettingsForm"/> and the notch settings page, so both use one
/// ChatGPT sign-in service, one model list and one access warning.
/// </summary>
internal static class NativeSettings
{
    public const string UsageUrl = "https://chatgpt.com/settings/usage";

    /// <summary>One OAuth service for the app: a second sign-in while one is open reuses the same flow rules.</summary>
    public static ChatGptOAuthService OAuth { get; } = new(msg => DebugLog.Append(msg));

    private static IReadOnlyList<ChatGptModel> _models = [];

    /// <summary>The models fetched last this session (classic or notch); empty until a refresh succeeds or after sign-out.</summary>
    public static IReadOnlyList<ChatGptModel> CachedModels => Volatile.Read(ref _models);

    public static async Task<IReadOnlyList<ChatGptModel>> ListModelsAsync(AppSettings settings, ISecretStore secrets, CancellationToken token)
    {
        var client = new ChatGptResponsesClient(ct => OAuth.GetAccessTokenAsync(settings, secrets, ct));
        var models = await client.ListModelsAsync(token);
        if (models.Count == 0) throw new InvalidOperationException("No models are available for this ChatGPT account.");
        Volatile.Write(ref _models, models);
        return models;
    }

    public static void ForgetModels() => Volatile.Write(ref _models, []);

    /// <summary>Keeps the selected model when offered, else gpt-5.6-terra, else the first.</summary>
    public static ChatGptModel PickModel(IReadOnlyList<ChatGptModel> models, string? selected) =>
        models.FirstOrDefault(m => m.Slug == selected) ?? models.FirstOrDefault(m => m.Slug == "gpt-5.6-terra") ?? models[0];

    /// <summary>The warning shown before Auto or Full access is turned on (classic Home and the notch page).</summary>
    public static string AccessWarning(AgentAccessMode mode) => mode == AgentAccessMode.FullAccess
        ? "Full access performs desktop input without individual approvals. It may send, publish, delete or change visible data. Stop cannot undo completed actions. Only use it in a task and account you intend to control. Enable Full access?"
        : "Auto asks you to approve the task plan, then runs routine steps. Sensitive-step detection uses model descriptions and conservative rules; it can miss risk. Choose Approve each action for strict control. Enable Auto?";

    public const string AccessWarningTitle = "Choose task access";

    /// <summary>An invisible top-most owner so dialogs opened from the notch appear above it. Dispose after the dialog.</summary>
    public static Form TopMostOwner()
    {
        var owner = new Form
        {
            TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Size = new Size(1, 1), Location = Cursor.Position,
        };
        owner.Show();
        return owner;
    }

    /// <summary>
    /// A small masked prompt. Returns null when cancelled; otherwise the secret (possibly empty) and, when
    /// <paramref name="headerDefault"/> is given, the header name the user left or typed.
    /// </summary>
    public static (string? Header, string Secret)? PromptSecret(string title, string message, string? headerDefault = null)
    {
        using var owner = TopMostOwner();
        using var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen, TopMost = true, ShowInTaskbar = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16),
        };
        AppTheme.Apply(form);
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        var label = AppTheme.Label(message, 10);
        label.MaximumSize = new Size(380, 0);
        flow.Controls.Add(label);
        TextBox? header = null;
        if (headerDefault is not null)
        {
            flow.Controls.Add(AppTheme.Label("Header name", 9, true));
            header = new TextBox { Text = headerDefault, Width = 380, MaxLength = 64, AccessibleName = "Header name" };
            flow.Controls.Add(header);
            flow.Controls.Add(AppTheme.Label("Key", 9, true));
        }
        var secret = new TextBox { Width = 380, UseSystemPasswordChar = true, MaxLength = 4_000, AccessibleName = title };
        flow.Controls.Add(secret);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 380 };
        var save = AppTheme.Button("Save", true);
        save.DialogResult = DialogResult.OK;
        var cancel = AppTheme.Button("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([save, cancel]);
        flow.Controls.Add(buttons);
        form.Controls.Add(flow);
        form.AcceptButton = save;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { form.Activate(); secret.Focus(); };
        return form.ShowDialog(owner) == DialogResult.OK ? (header?.Text.Trim(), secret.Text.Trim()) : null;
    }
}

/// <summary>
/// Records the next key chord with a temporary low-level keyboard hook (keys still reach the focused app). A non-modifier
/// key ends the chord (held modifiers + that key); releasing a modifier first ends a modifier-only chord; Esc alone or the
/// timeout cancels. <c>done</c> gets the virtual-key codes, or null when cancelled.
/// </summary>
internal sealed class HotkeyRecorder : IDisposable
{
    private const int WhKeyboardLl = 13, WmKeyDown = 0x100, WmKeyUp = 0x101, WmSysKeyDown = 0x104, WmSysKeyUp = 0x105, Injected = 0x10;
    private static readonly int[] ModifierKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];
    private readonly LowLevelKeyboardProc _proc;
    private readonly Action<IReadOnlyList<int>?> _done;
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _timeout;
    private readonly List<int> _modifiers = [];
    private nint _hook;
    private bool _finished;

    public HotkeyRecorder(TimeSpan timeout, Action<IReadOnlyList<int>?> done)
    {
        _done = done;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _proc = Hook;
        _timeout = new System.Windows.Forms.Timer { Interval = (int)timeout.TotalMilliseconds };
        _timeout.Tick += (_, _) => Finish(null);
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == 0) throw new InvalidOperationException("The keyboard could not be watched for a new shortcut.");
        _timeout.Start();
    }

    private nint Hook(int code, nint message, nint data)
    {
        if (code >= 0 && !_finished)
        {
            var vk = Marshal.ReadInt32(data);
            var flags = Marshal.ReadInt32(data, 8);
            if ((flags & Injected) == 0)
            {
                var down = message is WmKeyDown or WmSysKeyDown;
                var up = message is WmKeyUp or WmSysKeyUp;
                if (ModifierKeys.Contains(vk))
                {
                    if (down && !_modifiers.Contains(vk)) _modifiers.Add(vk);
                    else if (up && _modifiers.Count > 0) Post([.. _modifiers]);
                }
                else if (down) Post(vk == 0x1B && _modifiers.Count == 0 ? null : [.. _modifiers, vk]);
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    /// <summary>The hook must return quickly: finish (and unhook) on the next UI tick.</summary>
    private void Post(IReadOnlyList<int>? keys)
    {
        _finished = true;
        _ui.Post(_ => Finish(keys), null);
    }

    private void Finish(IReadOnlyList<int>? keys)
    {
        if (_hook == 0) return;
        Dispose();
        _done(keys);
    }

    public void Dispose()
    {
        _finished = true;
        _timeout.Dispose();
        if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
    }

    private delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, LowLevelKeyboardProc proc, nint module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
