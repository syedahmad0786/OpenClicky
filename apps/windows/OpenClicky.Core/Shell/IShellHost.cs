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
