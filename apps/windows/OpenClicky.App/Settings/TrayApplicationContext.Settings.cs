using System.Diagnostics;
using System.Speech.Synthesis;
using System.Text.Json;
using OpenClicky.App.Settings;
using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using OpenClicky.Input;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>The notch settings page: settings.section / settings.patch / settings.action from <see cref="ShellController"/>.</summary>
internal sealed partial class TrayApplicationContext
{
    private const string TalkLocked = "Changing this shortcut arrives with the new shortcuts update.";
    private SettingsService? _settingsService;
    private SettingsRuntimeProbe? _settingsProbe;
    /// <summary>Last connector.test outcome per key; shown until the connector is tested again or removed.</summary>
    private readonly Dictionary<string, (ConnectorStatus Status, int? Tools)> _connectorChecks = new(StringComparer.Ordinal);
    private NAudioMicrophoneRecorder? _micTest;
    private System.Windows.Forms.Timer? _micTestTimer;
    private SpeechSynthesizer? _voicePreview;
    private HotkeyRecorder? _hotkeyRecorder;
    private bool _accountBusy, _whisperBusy;

    private SettingsService NotchSettings => _settingsService ??= new SettingsService(_settings, s => SettingsStore.Save(s),
        ConfirmAccessMode, text => _shell?.AddToast(text), RequestShellPush, OnNotchSettingApplied);

    private SettingsRuntimeProbe SettingsProbe => _settingsProbe ??= new SettingsRuntimeProbe(_secrets, () => _settings,
        () => _chromePipe?.IsConnected == true, _work.RootDirectory, () => OnUi(RequestShellPush));

    /// <summary>Tray "Settings": the notch Settings tab when the shell runs, else the classic form.</summary>
    private void ShowNotchSettings()
    {
        if (_shell is { IsActive: true }) _shell.OpenNotch(NotchTab.Settings, ShellContract.DefaultSection);
        else ShowSettings();
    }

    /// <summary>Settings data for the next push: cached probe facts plus routines and connectors the push already read.</summary>
    private SettingsRuntime SettingsRuntimeFor(IReadOnlyList<Routine> routines, IReadOnlyList<WorkJob> jobs, IReadOnlyList<ConnectorInfo> connectors) =>
        SettingsProbe.Current with
        {
            Routines = SettingsActionArgs.RoutineViews(routines, jobs),
            Connectors = connectors.Select(c => _connectorChecks.TryGetValue(c.Key, out var check)
                ? new ConnectorView(c.Key, c.DisplayName, c.Host, check.Status, check.Tools)
                : new ConnectorView(c.Key, c.DisplayName, c.Host, ConnectorStatus.Connected, null)).ToList(),
        };

    void IShellHost.SettingsSection(string section)
    {
        SettingsProbe.Refresh();
        if (section == "account" && NativeSettings.CachedModels.Count == 0 && ProviderFactory.IsChatGptSignedIn(_secrets))
            _ = RefreshModelsFromShellAsync(announce: false);
    }

    // Deferred: a modal dialog must not open inside the WebView2 message callback.
    void IShellHost.PatchSetting(string key, JsonElement value) => OnUi(() => NotchSettings.Patch(key, value));

    void IShellHost.SettingsAction(string action, string? arg) => OnUi(() =>
    {
        try { RunSettingsAction(action, arg); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log($"settings_action_error action={action} type={ex.GetType().Name}");
            _shell?.AddToast("That did not work: " + ex.Message);
        }
    });

    private bool ConfirmAccessMode(AgentAccessMode mode)
    {
        using var owner = NativeSettings.TopMostOwner();
        return MessageBox.Show(owner, NativeSettings.AccessWarning(mode), NativeSettings.AccessWarningTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    /// <summary>Side effects the classic form gets from OnSettingsApplied, limited to the key that changed.</summary>
    private void OnNotchSettingApplied(string key)
    {
        switch (key)
        {
            case "voice.readReplies": _tts.Enabled = _settings.SpeakReplies; break;
            case "access.rememberHistory": ResetShellChat(); break;
            case "access.mode": _home.RefreshAccess(); break;
            case "microphone.deviceId": StopMicTest(); RefreshConnection(); break; // the recorders read the device id on each start
            case var account when account.StartsWith("account.", StringComparison.Ordinal): RefreshConnection(); break;
        }
    }

    private void RunSettingsAction(string action, string? arg)
    {
        switch (action)
        {
            case "signIn": _ = SignInFromShellAsync(); break;
            case "signOut": _ = SignOutFromShellAsync(); break;
            case "refreshModels": _ = RefreshModelsFromShellAsync(announce: true); break;
            case "manageUsage": OpenUrl(NativeSettings.UsageUrl); break;
            case "secret.edit" when arg == "openrouter": EditOpenRouterKey(); break;
            case "testMic": StartMicTest(); break;
            case "downloadWhisper": _ = DownloadWhisperFromShellAsync(); break;
            case "playTestSound": System.Media.SystemSounds.Asterisk.Play(); break; // default output device
            case "previewVoice": PreviewVoice(arg); break;
            case "clearHistory": ClearHistory(); _shell?.AddToast("Conversation and task history cleared."); break;
            case "exportMemory": ExportMemory(); break;
            case "openNotes": ShowClassic(); _home.ShowNotes(); break;
            case "openTasks": ShowClassic(); _home.ShowTasks(); break;
            case "openClipboard": ShowClipboardShelf(); break;
            case "chooseAgentFolder": ChooseAgentFolder(); break;
            case "resetAgentFolder": if (NotchSettings.Change(s => s.AgentFolder = null)) _shell?.AddToast("Agents use the default folder again."); break;
            case "openPrivacySetting":
                if (SettingsActionArgs.PrivacyUri(arg) is { } uri) OpenUrl(uri);
                else Log("settings: unknown privacy area");
                break;
            case "routine.run" or "routine.pause" or "routine.resume" or "routine.delete": RoutineAction(action, arg); break;
            case "connector.add": AddConnector(arg); break;
            case "connector.test": _ = TestConnectorAsync(arg); break;
            case "connector.remove": RemoveConnector(arg); break;
            case "chrome.setup":
                if (arg == "disconnect") _shell?.AddToast("Remove the extension from Chrome to disconnect.");
                else { ShowChromeSetup(); SettingsProbe.Refresh(); }
                break;
            case "recordHotkey": RecordHotkey(arg); break;
            case "resetHotkeys":
                if (NotchSettings.Change(SettingsActionArgs.ResetHotkeys)) _shell?.AddToast("Shortcuts reset. Talk keeps its current shortcut until the new shortcuts update.");
                break;
            case "openLogs": Launch(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(DebugLog.LogPath)}\"")); break;
            default: Log("settings: action not handled: " + action); break;
        }
    }

    /// <summary>The same browser OAuth flow as the classic "Continue with ChatGPT" button; the user signs in in their browser.</summary>
    private async Task SignInFromShellAsync()
    {
        if (_accountBusy) return;
        _accountBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var credential = await NativeSettings.OAuth.SignInAsync(_settings, _secrets, cts.Token);
            RefreshConnection();
            SettingsProbe.Refresh();
            if (!credential.HasUsableSession)
            {
                _shell?.AddToast("Signed in, but ChatGPT plan usage is disabled. Enable it with Continue with ChatGPT or Manage usage.");
                return;
            }
            await RefreshModelsFromShellAsync(announce: false);
            if (!_settings.ChatGptPlanWelcomeShown && NotchSettings.Change(s => s.ChatGptPlanWelcomeShown = true))
                _shell?.AddToast("You're using your ChatGPT plan. Eligible Ask and Agent requests use your plan allowance or available credits.");
            else _shell?.AddToast("Signed in to ChatGPT.");
        }
        catch (OperationCanceledException) { _shell?.AddToast("Sign-in timed out."); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("chatgpt_signin_error=" + ex.GetType().Name);
            _shell?.AddToast("Sign in with ChatGPT failed: " + ex.Message);
        }
        finally { _accountBusy = false; RequestShellPush(); }
    }

    private async Task SignOutFromShellAsync()
    {
        if (_accountBusy) return;
        _accountBusy = true;
        try
        {
            var revoked = await NativeSettings.OAuth.SignOutAsync(_settings, _secrets);
            NativeSettings.ForgetModels();
            RefreshConnection();
            SettingsProbe.Refresh();
            _shell?.AddToast(revoked ? "Signed out of ChatGPT." : "Signed out locally. Remote revocation was not confirmed; disconnect OpenClicky in ChatGPT settings if needed.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _shell?.AddToast("Sign out failed: " + ex.Message); }
        finally { _accountBusy = false; RequestShellPush(); }
    }

    /// <summary>Fetches the plan's models into the shared cache and keeps the chosen model valid (the classic rule).</summary>
    private async Task RefreshModelsFromShellAsync(bool announce)
    {
        if (!ProviderFactory.IsChatGptSignedIn(_secrets))
        {
            if (announce) _shell?.AddToast("Sign in with ChatGPT to load your models.");
            return;
        }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var models = await NativeSettings.ListModelsAsync(_settings, _secrets, cts.Token);
            var pick = NativeSettings.PickModel(models, _settings.ChatGptModel).Slug;
            if (pick != _settings.ChatGptModel) NotchSettings.Change(s => s.ChatGptModel = pick);
            RefreshConnection();
            if (announce) _shell?.AddToast($"{models.Count} models available.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("chatgpt_models_error=" + ex.GetType().Name);
            if (announce) _shell?.AddToast("Model list unavailable. " + ex.Message);
        }
        RequestShellPush();
    }

    /// <summary>OpenRouter key: masked native prompt, stored only in Credential Manager. Empty clears it.</summary>
    private void EditOpenRouterKey()
    {
        if (NativeSettings.PromptSecret("OpenRouter API key", "Paste your OpenRouter API key. It is saved with Windows Credential Manager, never in settings or logs. Leave it empty to remove the key.")
            is not { Secret: var key }) return;
        if (key.Length == 0) _secrets.Delete(SecretNames.OpenRouterApiKey);
        else _secrets.Set(SecretNames.OpenRouterApiKey, key);
        _settings.OpenRouterApiKey = key.Length == 0 ? null : key;
        RefreshConnection();
        _shell?.AddToast(key.Length == 0 ? "OpenRouter key removed." : "OpenRouter key saved.");
        RequestShellPush();
    }

    /// <summary>Five seconds of the selected microphone's level as mic.level every 50 ms. Test audio is discarded.</summary>
    private void StartMicTest()
    {
        StopMicTest();
        try
        {
            _micTest = new NAudioMicrophoneRecorder(() => _settings.MicrophoneDeviceId);
            _micTest.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StopMicTest();
            _shell?.AddToast("Could not open this microphone: " + ex.Message);
            return;
        }
        var ticks = 0;
        _micTestTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _micTestTimer.Tick += (_, _) =>
        {
            if (++ticks > 100) { StopMicTest(); _shell?.PostMicLevel(0); return; }
            _shell?.PostMicLevel(_micTest?.PeakLevel ?? 0);
        };
        _micTestTimer.Start();
    }

    private void StopMicTest()
    {
        _micTestTimer?.Dispose();
        _micTestTimer = null;
        _micTest?.Dispose();
        _micTest = null;
    }

    private async Task DownloadWhisperFromShellAsync()
    {
        if (_whisperBusy || LocalWhisperModel.IsAvailable) { SettingsProbe.Refresh(); return; }
        _whisperBusy = true;
        SettingsProbe.SetWhisperDownload(0);
        RequestShellPush();
        try
        {
            await LocalWhisperModel.DownloadAsync(new Progress<int>(percent =>
            {
                SettingsProbe.SetWhisperDownload(Math.Clamp(percent / 100.0, 0, 1));
                RequestShellPush();
            }), _lifetime.Token);
            RefreshConnection();
            _shell?.AddToast("Whisper is ready · English speech stays on this PC.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _shell?.AddToast("Download failed: " + ex.Message); }
        finally
        {
            _whisperBusy = false;
            SettingsProbe.SetWhisperDownload(null);
            SettingsProbe.Refresh();
        }
    }

    /// <summary>Speaks a short sample with a Windows voice at the saved speed. OpenAI voices need sub-project B's key.</summary>
    private void PreviewVoice(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (SettingsCatalog.OpenAiVoices.Contains(id)) { _shell?.AddToast("Add your OpenAI key to preview this voice."); return; }
        var name = id == SettingsCatalog.WindowsDefaultVoice ? null : SettingsProbe.VoiceName(id);
        if (id != SettingsCatalog.WindowsDefaultVoice && name is null) { _shell?.AddToast("That voice is not installed on this PC."); return; }
        StopVoicePreview();
        var synth = new SpeechSynthesizer();
        try
        {
            if (name is not null) synth.SelectVoice(name);
            // ponytail: linear speed→SAPI rate around the app's usual rate 2 (0.5→-2 … 1.5→6); tune by ear if needed.
            synth.Rate = Math.Clamp((int)Math.Round(2 + (_settings.SpeechSpeed - 1) * 8), -10, 10);
            synth.SetOutputToDefaultAudioDevice();
            synth.SpeakAsync("Hi, this is how I sound.");
            _voicePreview = synth;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or PlatformNotSupportedException)
        {
            synth.Dispose();
            _shell?.AddToast("That voice could not play: " + ex.Message);
        }
    }

    private void StopVoicePreview()
    {
        try { _voicePreview?.SpeakAsyncCancelAll(); } catch (ObjectDisposedException) { }
        _voicePreview?.Dispose();
        _voicePreview = null;
    }

    /// <summary>The classic "Clear conversation and task history"; both surfaces confirm before calling it.</summary>
    private void ClearHistory()
    {
        _talk.ClearHistory();
        _home.ClearConversation();
        ClearTasks();
        ResetShellChat();
    }

    private void ExportMemory()
    {
        using var owner = NativeSettings.TopMostOwner();
        using var dialog = new SaveFileDialog
        {
            Title = "Export companion memory", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", DefaultExt = "json",
            AddExtension = true, FileName = "openclicky-companion-memory.json", OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try { _shell?.AddToast(CompanionMemoryStore.ExportTo(dialog.FileName) ? "Companion memory exported." : "There is no companion memory to export yet."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _shell?.AddToast("Memory could not be exported: " + ex.Message); }
    }

    /// <summary>
    /// Saves the folder agents should work in. WorkStore still uses its own root for existing companions; following this
    /// folder for new companions needs a per-companion folder in WorkStore (agents sub-project).
    /// </summary>
    private void ChooseAgentFolder()
    {
        using var owner = NativeSettings.TopMostOwner();
        using var dialog = new FolderBrowserDialog { Description = "Choose the folder agents work in", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (!string.IsNullOrWhiteSpace(_settings.AgentFolder)) dialog.InitialDirectory = _settings.AgentFolder;
        if (dialog.ShowDialog(owner) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
        var folder = Path.GetFullPath(dialog.SelectedPath);
        if (!Writable(folder)) { _shell?.AddToast("OpenClicky cannot write to that folder. Choose another one."); return; }
        if (NotchSettings.Change(s => s.AgentFolder = folder)) _shell?.AddToast("Agent folder saved.");

        static bool Writable(string dir)
        {
            var probe = Path.Combine(dir, ".openclicky-write-test-" + Guid.NewGuid().ToString("N"));
            try { File.WriteAllText(probe, ""); File.Delete(probe); return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    private void RoutineAction(string action, string? id)
    {
        if (_work.Routines().FirstOrDefault(r => r.Id == id) is not { } routine) { _shell?.AddToast("That routine no longer exists."); RequestShellPush(); return; }
        switch (action)
        {
            case "routine.run": StartJob(routine.JobId, null, routine.Id); break; // StartJob reports why it could not start
            case "routine.pause": _work.SaveRoutine(SettingsActionArgs.Pause(routine)); break;
            case "routine.resume": _work.SaveRoutine(SettingsActionArgs.Resume(routine, DateTimeOffset.UtcNow, TimeZoneInfo.Local)); break;
            case "routine.delete": _work.DeleteRoutine(routine.Id); _shell?.AddToast("Routine deleted."); break;
        }
        _home.RefreshWork();
        RequestShellPush();
    }

    private void AddConnector(string? arg)
    {
        if (!SettingsActionArgs.TryParseConnector(arg, out var draft, out var error)) { _shell?.AddToast(error); return; }
        string? header = null, value = null;
        switch (draft!.Auth)
        {
            case ConnectorAuth.Browser: _shell?.AddToast("Browser sign-in arrives with the connectors update."); return;
            case ConnectorAuth.ApiKey:
                if (NativeSettings.PromptSecret($"{draft.Name} key", "Paste the connector's API key. It is saved with Windows Credential Manager, never in settings or logs.", "Authorization")
                    is not { Secret: { Length: > 0 } secret } entered) return;
                (header, value) = SettingsActionArgs.ApiKeyHeader(entered.Header, secret);
                break;
        }
        try
        {
            var info = _connectors.Add(draft.Name, draft.Url, header, value);
            _shell?.AddToast($"Added {info.DisplayName}. Test it to see its tools.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { _shell?.AddToast(ex.Message); }
        RequestShellPush();
    }

    /// <summary>Lists the connector's tools (30 s limit) and caches the count or the error for the page.</summary>
    private async Task TestConnectorAsync(string? key)
    {
        if (_connectors.List().FirstOrDefault(c => c.Key == key) is not { } info) { _shell?.AddToast("That connector no longer exists."); return; }
        _connectorChecks[info.Key] = (ConnectorStatus.Checking, null);
        RequestShellPush();
        try
        {
            var secret = await Task.Run(() => _connectors.Secret(info.Key));
            if (secret is null) { _connectorChecks[info.Key] = (ConnectorStatus.NeedsSignIn, null); _shell?.AddToast($"{info.DisplayName} needs its address and key again."); return; }
            using var connector = new RemoteMcpConnector(info.Key, info.DisplayName, secret);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var tools = await connector.ListToolsAsync(cts.Token);
            _connectorChecks[info.Key] = (ConnectorStatus.Connected, tools.Count);
            _shell?.AddToast($"{info.DisplayName}: {tools.Count} tool{(tools.Count == 1 ? "" : "s")} available.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The message may name the endpoint URL (which can carry account ids): show it, but log only the type.
            Log("connector_test_error=" + ex.GetType().Name);
            _connectorChecks[info.Key] = (ConnectorStatus.Error, null);
            _shell?.AddToast($"{info.DisplayName} could not be reached: " + (ex is OperationCanceledException ? "it did not answer within 30 seconds." : ex.Message));
        }
        finally { RequestShellPush(); }
    }

    private void RemoveConnector(string? key)
    {
        if (_connectors.List().FirstOrDefault(c => c.Key == key) is not { } info) { RequestShellPush(); return; }
        _connectors.Remove(info.Key);
        _connectorChecks.Remove(info.Key);
        _shell?.AddToast($"Removed {info.DisplayName}.");
        RequestShellPush();
    }

    /// <summary>Records the next chord for a shortcut row (7 s) and applies it as a settings.patch, so conflicts toast.</summary>
    private void RecordHotkey(string? row)
    {
        if (row == "talk") { _shell?.AddToast(TalkLocked); RequestShellPush(); return; }
        if (SettingsActionArgs.HotkeyKey(row) is not { } key) return;
        _hotkeyRecorder?.Dispose();
        _hotkeyRecorder = new HotkeyRecorder(TimeSpan.FromSeconds(7), keys =>
        {
            _hotkeyRecorder = null;
            if (keys is null || SettingsActionArgs.RecordedHotkey(_settings, row, keys) is not { } value)
            {
                _shell?.AddToast("No shortcut recorded.");
                RequestShellPush();
                return;
            }
            NotchSettings.Patch(key, value);
        });
    }

    private void DisposeSettings()
    {
        StopMicTest();
        StopVoicePreview();
        _hotkeyRecorder?.Dispose();
        _settingsProbe?.Dispose();
    }
}
