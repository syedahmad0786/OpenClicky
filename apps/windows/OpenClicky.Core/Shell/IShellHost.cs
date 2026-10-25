using System.Text.Json;

namespace OpenClicky.Core.Shell;

/// <summary>What the shell controller needs from the app. All calls arrive on the UI thread.</summary>
public interface IShellHost
{
    /// <summary>Host-owned data for the next state push (settings, jobs, chat history, connectors, phase, theme).</summary>
    ShellInputs CollectInputs();
    void ToggleTalk();
    void SubmitText(string text);
    void StopJob(string id);
    /// <summary>The page opened a job (it is now featured); refresh anything cached about it.</summary>
    void OpenJob(string id);
    void SetGeneral(string key, bool value);
    /// <summary>Only called with a path the host itself put in the last pushed state.</summary>
    void OpenFile(string path);
    void RevealFile(string path);
    void OpenUrl(string url);
    void CheckForUpdates();
    void InstallUpdate();
    void Quit();
    /// <summary>The shell could not start: show the classic window instead.</summary>
    void ShowClassic();
    void Trace(string message);
    /// <summary>The settings page shows <paramref name="section"/> (picked, or the Settings tab opened): refresh what it needs.</summary>
    void SettingsSection(string section);
    /// <summary>A shape-checked settings.patch; the host validates the key and value (SettingsPatch) and re-pushes.</summary>
    void PatchSetting(string key, JsonElement value);
    /// <summary>An allow-listed settings.action; <paramref name="arg"/> is at most 512 chars and still untrusted.</summary>
    void SettingsAction(string action, string? arg);
    /// <summary>A button on a card the controller still shows; <paramref name="actionId"/> is one of that card's actions.
    /// Return true to keep the card, false to remove it.</summary>
    bool CardAction(string cardId, string actionId);
    /// <summary>The user closed a card the controller still showed (no action ran).</summary>
    void CardDismissed(string cardId) { }
    /// <summary>A button on a saved-drawing chat row; <paramref name="id"/> is a drawing the last pushed state offered and <paramref name="action"/> one of ShellIntentParser.DrawingActions.</summary>
    void DrawingAction(string id, string action);
    /// <summary>An allow-listed first-run tour action from the page (<see cref="OnboardingContract.Actions"/>).</summary>
    void OnboardingAction(string action) { }
}

/// <summary>The page surface (a WebView2 window in the app, a fake in tests).</summary>
public interface IShellSurface : IDisposable
{
    /// <summary>Raised with (kind, raw JSON) for allow-listed page messages.</summary>
    event Action<string, string>? Message;
    /// <summary>Raised on the UI thread when the surface can no longer show the page (its browser process died).</summary>
    event Action? Failed;
    /// <summary>Posts a complete host message, already serialized.</summary>
    void Post(string json);
    /// <summary>The notch state the host last pushed or learned from the page.</summary>
    void SetNotch(ShellNotch notch);
}
