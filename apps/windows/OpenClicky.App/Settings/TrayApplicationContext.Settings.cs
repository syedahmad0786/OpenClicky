using System.Diagnostics;
using System.Text.Json;
using OpenClicky.App.Settings;
using OpenClicky.App.Speech;
using OpenClicky.Core;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Speech;
using OpenClicky.Input;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>The notch settings page: settings.section / settings.patch / settings.action from <see cref="ShellController"/>.</summary>
internal sealed partial class TrayApplicationContext
{
    private SettingsService? _settingsService;
    private SettingsRuntimeProbe? _settingsProbe;
    private NAudioMicrophoneRecorder? _micTest;
    private System.Windows.Forms.Timer? _micTestTimer;
    private bool _accountBusy;
    private readonly HashSet<WhisperModelInfo> _whisperDownloads = []; // UI thread only
    private int _recordGeneration;
    /// <summary>One native modal at a time from the notch: a double click must not stack warnings, prompts or pickers.</summary>
    private bool _settingsModalOpen;

    private SettingsService NotchSettings => _settingsService ??= new SettingsService(_settings, s => SettingsStore.Save(s),
        ConfirmAccessMode, text => _shell?.AddToast(text), RequestShellPush, OnNotchSettingApplied, Log);

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
            Connectors = ConnectorViews(connectors),
        };

    void IShellHost.SettingsSection(string section)
    {
        SettingsProbe.Refresh();
        if (section == "integrations") OnUi(VerifyUncheckedConnectors);
        if (section == "account" && NativeSettings.CachedModels.Count == 0 && ProviderFactory.IsChatGptSignedIn(_secrets))
            _ = RefreshModelsFromShellAsync(announce: false);
    }

    // Deferred: a modal dialog must not open inside the WebView2 message callback.
    void IShellHost.PatchSetting(string key, JsonElement value) => OnUi(() =>
    {
        if (_settingsModalOpen) { Log("settings: patch ignored while a dialog is open"); RequestShellPush(); return; }
        NotchSettings.Patch(key, value);
    });

    void IShellHost.SettingsAction(string action, string? arg) => OnUi(() =>
    {
        if (_settingsModalOpen) { Log("settings: action ignored while a dialog is open: " + action); return; }
        try { RunSettingsAction(action, arg); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log($"settings_action_error action={action} type={ex.GetType().Name}");
            _shell?.AddToast("That did not work: " + SettingsActionArgs.UserMessage(ex));
        }
    });

    private bool ConfirmAccessMode(AgentAccessMode mode) => Modal(() =>
    {
        using var owner = NativeSettings.TopMostOwner();
        return MessageBox.Show(owner, NativeSettings.AccessWarning(mode), NativeSettings.AccessWarningTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }, busy: false);

    /// <summary>Runs a native modal under <see cref="_settingsModalOpen"/>; returns <paramref name="busy"/> if one is already open.</summary>
    private T Modal<T>(Func<T> show, T busy)
    {
        if (_settingsModalOpen) return busy;
        _settingsModalOpen = true;
        try { return show(); }
        finally { _settingsModalOpen = false; }
    }

    /// <summary>Side effects the classic form gets from OnSettingsApplied, limited to the key that changed.</summary>
    private void OnNotchSettingApplied(string key)
    {
        switch (key)
        {
            case "voice.readReplies": _tts.Enabled = _settings.SpeakReplies; break;
            case "access.rememberHistory": ResetShellChat(); break;
            case "access.mode": _home.RefreshAccess(); break;
            // As the classic OnSettingsApplied: a new provider or proxy must not serve the task or turn in flight.
            case "account.provider" or "advanced.useLocalProxy" or "advanced.proxyUrl":
                CancelTask();
                CancelTalk();
                RefreshConnection();
                break;
            case "microphone.deviceId": StopMicTest(); RefreshConnection(); break; // the recorders read the device id on each start
            case var account when account.StartsWith("account.", StringComparison.Ordinal): RefreshConnection(); break;
            case var shortcut when shortcut.StartsWith("shortcuts.", StringComparison.Ordinal): ApplyShortcutBindings(); break;
            case var cursor when cursor.StartsWith("cursor.", StringComparison.Ordinal): ApplyCompanionStyle(); break;
        }
    }

    /// <summary>The classic window pauses a running task when it comes forward (input must not reach the wrong window); say so, since the notch gave no hint.</summary>
    private void ShowClassicFromNotch()
    {
        var wasRunning = _taskCts is not null && !_taskControl.IsPaused;
        ShowClassic();
        if (wasRunning) _shell?.AddToast("Your task is paused while the classic window is open. Resume it from Tasks.");
    }

    /// <summary>As <see cref="ShowChromeSetup"/>, but owned by a top-most window so it never opens behind the notch.</summary>
    private void ShowChromeSetupFromNotch()
    {
        PauseTask();
        using var owner = NativeSettings.TopMostOwner();
        using var setup = new ChromeBridgeSetupForm(Application.ExecutablePath, RuntimePath("chrome-extension"));
        setup.ShowDialog(owner);
        if (_chromePipe is null && File.Exists(ChromeBridgeConfiguration.DefaultPath)) _ = ConnectChromeAsync();
    }

    private void RunSettingsAction(string action, string? arg)
    {
        switch (action)
        {
            case "signIn": _ = SignInFromShellAsync(); break;
            case "signOut": _ = SignOutFromShellAsync(); break;
            case "refreshModels": _ = RefreshModelsFromShellAsync(announce: true); break;
            case "manageUsage": OpenUrl(NativeSettings.UsageUrl); break;
            case "secret.edit": EditSecret(arg); break;
            case "testMic": StartMicTest(); break;
            case "downloadWhisper": _ = DownloadWhisperFromShellAsync(WhisperModels.English); break;
            case "downloadWhisperMultilingual": _ = DownloadWhisperFromShellAsync(WhisperModels.Multilingual); break;
            case "playTestSound": _ = _tts.PlayTestSoundAsync(); break;
            case "previewVoice": PreviewVoice(arg); break;
            case "clearHistory": ClearHistory(); _shell?.AddToast("Conversation and task history cleared."); break;
            case "exportMemory": ExportMemory(); break;
            case "openNotes": ShowClassicFromNotch(); _home.ShowNotes(); break;
            case "openTasks": ShowClassicFromNotch(); _home.ShowTasks(); break;
            case "openClipboard": ShowClipboardShelf(); break;
            case "chooseAgentFolder": ChooseAgentFolder(); break;
            case "resetAgentFolder": if (NotchSettings.Change(s => s.AgentFolder = null)) _shell?.AddToast("Agents use the default folder again."); break;
            case "openPrivacySetting":
                if (SettingsActionArgs.PrivacyUri(arg) is { } uri) OpenUrl(uri);
                else Log("settings: unknown privacy area");
                break;
            case "routine.run" or "routine.pause" or "routine.resume" or "routine.delete": RoutineAction(action, arg); break;
            case "connector.add" or "connector.signIn" or "connector.label" or "connector.test" or "connector.remove": ConnectorAction(action, arg); break;
            case "chrome.setup":
                if (arg == "disconnect") _shell?.AddToast("Remove the extension from Chrome to disconnect.");
                else if (Modal(() => { ShowChromeSetupFromNotch(); return true; }, false)) SettingsProbe.Refresh();
                break;
            case "recordHotkey": RecordHotkey(arg); break;
            case "resetHotkeys":
                if (NotchSettings.Change(SettingsActionArgs.ResetHotkeys)) { ApplyShortcutBindings(); _shell?.AddToast("Shortcuts reset."); }
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

    /// <summary>secret.edit for "openrouter" or "openai": masked native prompt, stored only in Credential Manager. Empty clears it.</summary>
    private void EditSecret(string? arg)
    {
        if (SettingsActionArgs.SecretFor(arg) is not { } secret) { Log("settings: unknown secret"); return; }
        var (name, title) = secret;
        var openAi = name == SecretNames.OpenAiApiKey;
        var use = openAi ? " It is used only for OpenAI voices: with one selected, the text of each reply sentence is sent to api.openai.com to be spoken." : "";
        if (Modal(() => NativeSettings.PromptSecret(title, $"Paste your {title}.{use} It is saved with Windows Credential Manager, never in settings or logs. Leave it empty to remove the key."), null)
            is not { Secret: var key }) return;
        if (key.Length == 0) _secrets.Delete(name);
        else _secrets.Set(name, key);
        var label = openAi ? "OpenAI key" : "OpenRouter key";
        if (!openAi)
        {
            _settings.OpenRouterApiKey = key.Length == 0 ? null : key;
            RefreshConnection();
        }
        SettingsProbe.Refresh();
        _shell?.AddToast(key.Length == 0 ? $"{label} removed." : $"{label} saved.");
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

    private async Task DownloadWhisperFromShellAsync(WhisperModelInfo model)
    {
        if (_whisperDownloads.Contains(model) || LocalWhisperModel.IsAvailable(model)) { SettingsProbe.Refresh(); return; }
        _whisperDownloads.Add(model);
        SettingsProbe.SetWhisperDownload(model, 0);
        RequestShellPush();
        try
        {
            await LocalWhisperModel.DownloadAsync(model, new Progress<int>(percent =>
            {
                SettingsProbe.SetWhisperDownload(model, Math.Clamp(percent / 100.0, 0, 1));
                RequestShellPush();
            }), _lifetime.Token);
            RefreshConnection();
            _shell?.AddToast(model.Multilingual ? "Multilingual voice is ready · speech stays on this PC." : "Whisper is ready · English speech stays on this PC.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _shell?.AddToast("Download failed: " + ex.Message); }
        finally
        {
            _whisperDownloads.Remove(model);
            SettingsProbe.SetWhisperDownload(model, null);
            SettingsProbe.Refresh();
        }
    }

    /// <summary>Says the preview line with that voice at the saved speed on the selected device.</summary>
    private void PreviewVoice(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (VoiceCatalog.IsOpenAi(id))
        {
            if (!SettingsProbe.Current.OpenAiKeySet) { _shell?.AddToast("Add your OpenAI key to preview this voice."); return; }
        }
        else if (id != SettingsCatalog.WindowsDefaultVoice && SettingsProbe.VoiceName(id) is null) { _shell?.AddToast("That voice is not installed on this PC."); return; }
        _ = _tts.PreviewAsync(id);
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
        using var dialog = new SaveFileDialog
        {
            Title = "Export companion memory", Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*", DefaultExt = "json",
            AddExtension = true, FileName = "openclicky-companion-memory.json", OverwritePrompt = true,
        };
        if (!Modal(() => { using var owner = NativeSettings.TopMostOwner(); return dialog.ShowDialog(owner) == DialogResult.OK; }, false)) return;
        try { _shell?.AddToast(CompanionMemoryStore.ExportTo(dialog.FileName) ? "Companion memory exported." : "There is no companion memory to export yet."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _shell?.AddToast("Memory could not be exported: " + ex.Message); }
    }

    /// <summary>
    /// Saves the folder agents should work in. WorkStore still uses its own root for existing companions; following this
    /// folder for new companions needs a per-companion folder in WorkStore (agents sub-project).
    /// </summary>
    private void ChooseAgentFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the folder agents work in", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (!string.IsNullOrWhiteSpace(_settings.AgentFolder)) dialog.InitialDirectory = _settings.AgentFolder;
        if (!Modal(() => { using var owner = NativeSettings.TopMostOwner(); return dialog.ShowDialog(owner) == DialogResult.OK; }, false)
            || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
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

    /// <summary>
    /// Records the next chord for a shortcut row through the shortcut monitor (7 s; no shortcut fires while it records)
    /// and applies it as a settings.patch, so validation and conflicts toast. A paused monitor listens just for this.
    /// </summary>
    private void RecordHotkey(string? row)
    {
        if (SettingsActionArgs.HotkeyKey(row) is not { } key || SettingsActionArgs.HotkeyAction(row) is not { } action) return;
        var resume = _settings.Paused;
        var generation = ++_recordGeneration;
        if (resume)
        {
            try { _hotkey.Start(); }
            catch (InvalidOperationException ex) { Log("hotkey_record_error=" + ex.Message); _shell?.AddToast("The keyboard could not be watched for a new shortcut."); RequestShellPush(); return; }
        }
        _hotkey.Record(action, binding =>
        {
            if (generation != _recordGeneration) return; // replaced by a newer recording, which owns the hook now
            if (resume && _settings.Paused) _hotkey.Stop();
            if (binding is null)
            {
                _shell?.AddToast("No shortcut recorded.");
                RequestShellPush();
                return;
            }
            NotchSettings.Patch(key, SettingsActionArgs.HotkeyPatchValue(binding));
        });
    }

    private void DisposeSettings()
    {
        StopMicTest();
        _tts.Stop();
        _settingsProbe?.Dispose();
        DisposeConnectors();
    }
}
