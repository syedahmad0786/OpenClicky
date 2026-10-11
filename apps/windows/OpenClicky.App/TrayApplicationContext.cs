using OpenClicky.Agent;
using OpenClicky.Capture;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Input;
using OpenClicky.Overlay;
using OpenClicky.Providers;

namespace OpenClicky.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private AppSettings _settings;
    private readonly ISecretStore _secrets = new WindowsCredentialSecretStore();
    private readonly PushToTalkHotkeyMonitor _hotkey;
    private readonly BuddyOverlayController _overlay;
    private readonly TalkOrchestrator _talk;
    private readonly SettingsForm _settingsForm;
    private readonly IMicrophoneRecorder _mic;
    private readonly WgcScreenCaptureService _capture;
    private readonly LocalSpeechTtsClient _tts;
    private readonly CompositeAgentExecutor _executor = new();
    private readonly WinFormsAgentConfirm _confirm;
    private readonly SynchronizationContext _uiContext;
    private readonly HomeForm _home;
    private CancellationTokenSource? _submissionCts;
    private bool _demoTalk;
    private OpenClicky.Sandbox.SandboxForm? _sandbox;
    private bool _sandboxDemo;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_uiContext);
        // The built-in sandbox is the only trusted input target inside this process.
        _confirm = new WinFormsAgentConfirm(_uiContext, () => _sandboxDemo && _sandbox is { IsDisposed: false }
            ? (_sandbox.Handle, _sandbox.ActiveControl?.Handle ?? IntPtr.Zero) : null);

        _settings = LoadSettingsWithSecrets();
        IgnoreLegacyFixtureTranscript();
        _hotkey = new PushToTalkHotkeyMonitor(_settings.PushToTalk);
        _overlay = new BuddyOverlayController();
        _capture = new WgcScreenCaptureService(Log, () => _settings.ForceGdiCapture);
        _mic = CreateMic();
        _tts = new LocalSpeechTtsClient();
        _tts.Enabled = _settings.SpeakReplies;

        _talk = new TalkOrchestrator(
            _hotkey,
            _overlay,
            _capture,
            _mic,
            () => _settings.UseWindowsSpeech ? new LocalSpeechSttClient() : ProviderFactory.CreateSttClient(_settings, _secrets),
            () => _demoTalk ? new OfflineTalkClient() : ProviderFactory.CreateTalkClient(_settings, _secrets),
            _tts,
            () => _settings,
            RunAgentAsync,
            Log);

        _home = new HomeForm(_settings, SubmitFromHomeAsync, Stop, ShowSettings, TryPointingDemoAsync, TrySandboxDemoAsync);
        _ = _home.Handle;
        _talk.StateChanged += state => OnUi(() => _home.SetState(state));
        _talk.MessageReceived += (role, text) => OnUi(() => _home.AddMessage(role, text));
        _hotkey.Cancelled += (_, _) => Stop();
        _hotkey.Pressed += (_, _) => _submissionCts?.Cancel();
        _settingsForm = new SettingsForm(_settings, _secrets, OnSettingsApplied, () =>
        {
            _talk.ClearHistory();
            _home.ClearConversation();
        });

        _tray = new NotifyIcon
        {
            Icon = AppTheme.Icon,
            Visible = true,
            Text = $"OpenClicky — hold {_settings.PushToTalk.DisplayName}",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open OpenClicky", null, (_, _) => ShowHome());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Stop current task", null, (_, _) => Stop());
        menu.Items.Add("Test capture once", null, async (_, _) => await TestCaptureAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowHome();

        _talk.Start();
        Log($"tray ready — hold {_settings.PushToTalk.DisplayName}");
        if (string.IsNullOrWhiteSpace(_settings.OpenRouterApiKey)
            && !ProviderFactory.IsChatGptSignedIn(_secrets))
        {
            Log("hint: set OpenRouter API key or Continue with ChatGPT in Settings (or use debug transcript)");
        }

        MaybeShowFirstRunTip();
        RefreshConnection();
        _uiContext.Post(_ => ShowHome(), null);
    }

    private void OnUi(Action action) => _uiContext.Post(_ =>
    {
        if (!_home.IsDisposed) action();
    }, null);

    private void IgnoreLegacyFixtureTranscript()
    {
        if (!OfflineTalkClient.IsEnvEnabled() && !OfflineAgentPlanner.IsEnvEnabled())
            _settings.DebugTranscriptOverride = null;
    }

    private void RefreshConnection()
    {
        var status = OfflineTalkClient.IsEnvEnabled() || OfflineAgentPlanner.IsEnvEnabled()
            ? "Offline fixture launch · canned responses"
            : _settings.AuthProvider == AuthProvider.ChatGPT
                ? (ProviderFactory.IsChatGptSignedIn(_secrets) ? "ChatGPT signed in · live request not yet verified" : "ChatGPT · sign in in Settings")
                : string.IsNullOrWhiteSpace(_settings.OpenRouterApiKey) ? "Connect a provider in Settings · demos work without a key" : "OpenRouter key saved · " + _settings.ResolveVisionModel();
        _home.SetConnection(status);
    }

    private void Stop()
    {
        _submissionCts?.Cancel();
        _talk.Cancel();
    }

    private void ShowHome()
    {
        _home.Show();
        _home.WindowState = FormWindowState.Normal;
        _home.Activate();
        RefreshConnection();
    }

    private async Task SubmitFromHomeAsync(string text, bool agent)
    {
        _submissionCts?.Cancel();
        using var submission = new CancellationTokenSource();
        _submissionCts = submission;
        try
        {
            _home.SetState(CompanionVoiceState.Processing);
            _home.SetStatus("Switch to your target app · starting in 3 seconds");
            _home.WindowState = FormWindowState.Minimized;
            _settingsForm.Hide();
            await Task.Delay(3000, submission.Token);
            await _talk.SubmitTextAsync(agent ? "openclicky agent " + text : text);
        }
        catch (OperationCanceledException) { _home.AddMessage("OpenClicky", "Stopped before the request started."); }
        finally
        {
            if (ReferenceEquals(_submissionCts, submission))
            {
                _submissionCts = null;
                _home.SetState(_talk.State);
                if (_talk.State == CompanionVoiceState.Idle) ShowHome();
            }
        }
    }

    private async Task TryPointingDemoAsync()
    {
        _demoTalk = true;
        _home.SetConnection("Offline pointing demo · no model call");
        try { await _talk.SubmitTextAsync("Show me the center of this screen", remember: false); }
        finally { _demoTalk = false; RefreshConnection(); }
    }

    private async Task TrySandboxDemoAsync()
    {
        if (_talk.State != CompanionVoiceState.Idle) return;
        _sandbox?.Dispose();
        _sandbox = new OpenClicky.Sandbox.SandboxForm();
        AppTheme.Apply(_sandbox);
        _sandbox.Icon = AppTheme.Icon;
        _sandbox.ResumeLayout(true);
        _sandbox.Show();
        _sandbox.Activate();
        _sandboxDemo = true;
        _home.SetConnection("Offline action demo · only the test window");
        try { await _talk.SubmitTextAsync("openclicky agent click the green button then type hello in the test window", remember: false); }
        finally { _sandboxDemo = false; RefreshConnection(); ShowHome(); }
    }

    private void MaybeShowFirstRunTip()
    {
        if (_settings.FirstRunCompleted)
        {
            return;
        }

        _settings.FirstRunCompleted = true;
        SettingsStore.Save(_settings);

    }

    private async Task RunAgentAsync(string task, CancellationToken cancellationToken)
    {
        var planner = _sandboxDemo && _sandbox is not null
            ? new SandboxPlanner(_sandbox)
            : ProviderFactory.CreateAgentPlanner(_settings, _secrets);
        if (planner is null)
        {
            var msg = _settings.AuthProvider == AuthProvider.ChatGPT
                ? "sign in with chatgpt in settings to run agents, or switch to openrouter."
                : "add your openrouter api key in settings to run agents, or sign in with chatgpt.";
            _overlay.ShowResponding(msg);
            _talk.ReportAssistantMessage(msg);
            await _tts.SpeakAsync(msg, cancellationToken).ConfigureAwait(false);
            return;
        }

        var agent = new AgentOrchestrator(
            _capture,
            planner,
            ExecuteMappedAsync,
            _confirm,
            _overlay,
            _tts,
            Log,
            foregroundTitle: ForegroundWindowInfo.GetTitle,
            denyListExtra: () => AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra),
            onReply: _talk.ReportAssistantMessage);

        await agent.RunAsync(task, _settings.ResolveVisionModel(), cancellationToken).ConfigureAwait(false);
    }

    private AppSettings LoadSettingsWithSecrets()
    {
        var settings = SettingsStore.Load();
        // Migrate plaintext keys out of settings.json into Credential Manager.
        if (!string.IsNullOrWhiteSpace(settings.OpenRouterApiKey))
        {
            try
            {
                _secrets.Set(SecretNames.OpenRouterApiKey, settings.OpenRouterApiKey!);
                settings.OpenRouterApiKey = null;
                SettingsStore.Save(settings);
                Log("migrated OpenRouter key → Windows Credential Manager");
            }
            catch (Exception ex)
            {
                Log($"secret_migrate_error={ex.Message}");
            }
        }

        try
        {
            settings.OpenRouterApiKey = _secrets.Get(SecretNames.OpenRouterApiKey);
        }
        catch (Exception ex)
        {
            Log($"secret_load_error={ex.Message}");
        }

        return settings;
    }

    private async Task<ActionResult> ExecuteMappedAsync(
        ActionRequest request,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        var legacy = new AgentActionRequest(request.ToolName, request.Arguments, request.Rationale);
        var result = await _executor.ExecuteAsync(legacy, confirmed, cancellationToken).ConfigureAwait(false);
        return new ActionResult(result.Success, result.Message, result.RequiresConfirmation);
    }

    private static IMicrophoneRecorder CreateMic()
    {
        try
        {
            return new NAudioMicrophoneRecorder();
        }
        catch (Exception ex)
        {
            Log($"mic_fallback null ({ex.Message})");
            return new NullMicrophoneRecorder();
        }
    }

    private void OnSettingsApplied()
    {
        IgnoreLegacyFixtureTranscript();
        _tts.Enabled = _settings.SpeakReplies;
        // LL hook reads Chord on each key event — assign is enough (no Unhook/re-hook).
        _hotkey.Chord = HotkeyChord.NormalizeOrDefault(_settings.PushToTalk);
        _settings.PushToTalk = _hotkey.Chord;
        _tray.Text = $"OpenClicky — hold {_settings.PushToTalk.DisplayName}";
        _home.RefreshShortcut();
        RefreshConnection();
        Log(
            $"settings_applied hotkey={_settings.PushToTalk.DisplayName} provider={_settings.AuthProvider} " +
            $"openrouter_key={(string.IsNullOrEmpty(_settings.OpenRouterApiKey) ? "missing" : "set")} " +
            $"chatgpt={(ProviderFactory.IsChatGptSignedIn(_secrets) ? "signed-in" : "out")}");
    }

    private void ShowSettings()
    {
        if (_settingsForm.Visible)
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm.Show();
    }

    private async Task TestCaptureAsync()
    {
        try
        {
            var results = await _capture.CaptureAllMonitorsJpegAsync();
            var primary = results.FirstOrDefault(r => r.IsCursorMonitor) ?? results.First();
            Log($"manual_capture_bytes={primary.JpegBytes.Length} magic={(primary.LooksLikeJpeg ? "FFD8" : "????")} backend={_capture.LastBackend}");
            MessageBox.Show(
                $"Captured {primary.JpegBytes.Length} bytes ({primary.PixelWidth}x{primary.PixelHeight}).\nJPEG magic OK: {primary.LooksLikeJpeg}\nBackend: {_capture.LastBackend} (WGC supported: {_capture.GraphicsCaptureSupported})",
                "OpenClicky capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log($"manual_capture_error={ex.Message}");
            MessageBox.Show(ex.Message, "OpenClicky capture failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Log(string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        DebugLog.Append(line);
        System.Diagnostics.Debug.WriteLine(line);
    }

    protected override void ExitThreadCore()
    {
        _talk.Dispose();
        _capture.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _settingsForm.Dispose();
        _home.Dispose();
        _sandbox?.Dispose();
        _submissionCts?.Cancel();
        base.ExitThreadCore();
    }

    private sealed class SandboxPlanner(OpenClicky.Sandbox.SandboxForm sandbox) : IAgentPlanner
    {
        public Task<AgentPlanStep> NextStepAsync(string task, IReadOnlyList<ScreenCaptureResult> screens,
            IReadOnlyList<string> priorResults, string model, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sandbox.IsDisposed) throw new InvalidOperationException("The test window was closed.");
            var step = (AgentPlanStep)sandbox.Invoke(() =>
            {
                if (priorResults.Count >= 3)
                    return new AgentPlanStep("done", new Dictionary<string,string> { ["summary"] = sandbox.HasVerifiedResult
                        ? "Verified in the test window: the green button was clicked and the field contains hello. This was an offline demonstration, not a live AI task."
                        : "The test window did not show the expected result. Desktop actions are not verified." }, "Inspect the actual test controls", true);
                if (priorResults.Count == 2)
                    return new AgentPlanStep("type_text", new Dictionary<string,string> { ["text"] = "hello" }, "Type hello in the test field", false);
                var point = priorResults.Count == 0 ? sandbox.ButtonCenter : sandbox.InputCenter;
                return new AgentPlanStep("click", new Dictionary<string,string> { ["x"] = point.X.ToString(), ["y"] = point.Y.ToString(), ["space"] = "virtual" },
                    priorResults.Count == 0 ? "Click the green button in the test window" : "Focus the test field", false);
            });
            return Task.FromResult(step);
        }
    }
}

internal static class DebugLog
{
    private static readonly object Gate = new();
    private static readonly List<string> Lines = new();
    private const long MaxLogBytes = 512 * 1024;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenClicky",
        "debug.log");

    public static void Append(string line)
    {
        lock (Gate)
        {
            Lines.Add(line);
            if (Lines.Count > 200)
            {
                Lines.RemoveAt(0);
            }

            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(LogPath, line + Environment.NewLine);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    Rotate();
                }
            }
            catch
            {
                // never break the tray on log I/O
            }
        }
    }

    public static string Snapshot()
    {
        lock (Gate)
        {
            return string.Join(Environment.NewLine, Lines);
        }
    }

    private static void Rotate()
    {
        try
        {
            var text = File.ReadAllText(LogPath);
            if (text.Length <= MaxLogBytes / 2)
            {
                return;
            }

            var keep = (int)(MaxLogBytes / 2);
            File.WriteAllText(LogPath, text[^keep..]);
        }
        catch
        {
            // ignore
        }
    }
}
