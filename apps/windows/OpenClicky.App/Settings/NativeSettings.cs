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
