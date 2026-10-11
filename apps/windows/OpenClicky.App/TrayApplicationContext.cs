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

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_uiContext);
        _confirm = new WinFormsAgentConfirm(_uiContext);

        _settings = LoadSettingsWithSecrets();
        _hotkey = new PushToTalkHotkeyMonitor(_settings.PushToTalk);
        _overlay = new BuddyOverlayController();
        _capture = new WgcScreenCaptureService(Log, () => _settings.ForceGdiCapture);
        _mic = CreateMic();
        _tts = new LocalSpeechTtsClient();

        _talk = new TalkOrchestrator(
            _hotkey,
            _overlay,
            _capture,
            _mic,
            () => ProviderFactory.CreateSttClient(_settings, _secrets),
            () => ProviderFactory.CreateTalkClient(_settings, _secrets),
            _tts,
            () => _settings,
            RunAgentAsync,
            Log);

        _settingsForm = new SettingsForm(_settings, _secrets, OnSettingsApplied, () => _talk.ClearHistory());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "OpenClicky",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Test capture once", null, async (_, _) => await TestCaptureAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();

        _talk.Start();
        Log($"tray ready — hold {_settings.PushToTalk.DisplayName}");
        if (string.IsNullOrWhiteSpace(_settings.OpenRouterApiKey)
            && !ProviderFactory.IsChatGptSignedIn(_secrets))
        {
            Log("hint: set OpenRouter API key or Continue with ChatGPT in Settings (or use debug transcript)");
        }

        MaybeShowFirstRunTip();
    }

    private void MaybeShowFirstRunTip()
    {
        if (_settings.FirstRunCompleted)
        {
            return;
        }

        _settings.FirstRunCompleted = true;
        SettingsStore.Save(_settings);

        _tray.BalloonTipTitle = "OpenClicky";
        _tray.BalloonTipText =
            $"Hold {_settings.PushToTalk.DisplayName} to talk. Open Settings for OpenRouter key or Sign in with ChatGPT.";
        _tray.BalloonTipIcon = ToolTipIcon.Info;
        _tray.ShowBalloonTip(8000);

        if (string.IsNullOrWhiteSpace(_settings.OpenRouterApiKey)
            && !ProviderFactory.IsChatGptSignedIn(_secrets))
        {
            // Nudge empty-auth installs into Settings once.
            _uiContext.Post(_ => ShowSettings(), null);
        }
    }

    private async Task RunAgentAsync(string task, CancellationToken cancellationToken)
    {
        var planner = ProviderFactory.CreateAgentPlanner(_settings, _secrets);
        if (planner is null)
        {
            var msg = _settings.AuthProvider == AuthProvider.ChatGPT
                ? "sign in with chatgpt in settings to run agents, or switch to openrouter."
                : "add your openrouter api key in settings to run agents, or sign in with chatgpt.";
            _overlay.ShowResponding(msg);
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
            denyListExtra: () => AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra));

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
        _hotkey.Chord = _settings.PushToTalk;
        Log(
            $"settings_applied hotkey={_settings.PushToTalk.DisplayName} provider={_settings.AuthProvider} " +
            $"openrouter_key={(string.IsNullOrEmpty(_settings.OpenRouterApiKey) ? "missing" : "set")} " +
            $"chatgpt={(ProviderFactory.IsChatGptSignedIn(_secrets) ? _settings.ChatGptAccountEmail ?? "signed-in" : "out")}");
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
        base.ExitThreadCore();
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
