using System.Diagnostics;
using System.Reflection;
using OpenClicky.App.Shell;
using OpenClicky.Core;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Updates;

namespace OpenClicky.App;

/// <summary>The tray app as the shell's host: feeds <see cref="ShellController"/> and carries out its intents.</summary>
internal sealed partial class TrayApplicationContext : IShellHost
{
    private static readonly string CurrentVersion =
        (typeof(TrayApplicationContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
    private static string WebViewFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "WebView2");

    private readonly TaskCompletionSource<CompanionWorkspaceRuntime> _workspaceReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Windows.Forms.Timer _shellPushTimer = new() { Interval = 60 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60 * 60 * 1000 };
    private readonly HttpClient _updateHttp = new();
    private ShellController? _shell;
    private ShellSurfaceAdapter? _shellSurface;
    private TaskbarPresence? _taskbar;
    private ToolStripMenuItem? _pauseItem;
    private Action? _pendingPush;
    private ReleaseInfo? _release;
    private const string UpdateAvailableTitle = "OpenClicky update available";
    private string? _announcedRelease;
    private bool _installingUpdate;
    private readonly bool _background;
    /// <summary>Caches so a push does not hit SQLite or the disk: history refreshes on TurnCommitted, outputs when a job finishes or is opened.</summary>
    private IReadOnlyList<ConversationTurn>? _shellTurns;
    private readonly Dictionary<string, IReadOnlyList<string>> _jobOutputs = new(StringComparer.Ordinal);

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open OpenClicky", null, (_, _) => ShowHome());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        _pauseItem = new ToolStripMenuItem(_settings.Paused ? "Resume" : "Pause", null, (_, _) => SetPaused(!_settings.Paused));
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Classic window", null, (_, _) => ShowClassic());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("What's new", null, (_, _) => OpenUrl(ShellController.WhatsNewUrl));
        menu.Items.Add("Report a bug", null, (_, _) => OpenUrl(ShellController.BugUrl));
        menu.Items.Add("Request a feature", null, (_, _) => OpenUrl(ShellController.FeatureUrl));
        menu.Items.Add("Check for updates", null, async (_, _) => await CheckUpdatesAsync(announce: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        return menu;
    }

    /// <summary>The classic window's Tools menu keeps the actions the tray menu used to carry.</summary>
    private ContextMenuStrip BuildClassicToolsMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Clipboard shelf", null, (_, _) => ShowClipboardShelf());
        menu.Items.Add("Dictate into focused field", null, (_, _) => BeginDictationAtCurrentTarget());
        menu.Items.Add("Start / stop conversation", null, (_, _) => ToggleConversation());
        menu.Items.Add("Stop current task", null, (_, _) => Stop());
        menu.Items.Add("Test capture once", null, async (_, _) => await TestCaptureAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        return menu;
    }

    private void WireShellEvents()
    {
        _shellPushTimer.Tick += (_, _) =>
        {
            _shellPushTimer.Stop();
            var push = _pendingPush;
            _pendingPush = null;
            push?.Invoke();
        };
        _updateTimer.Tick += async (_, _) => await CheckUpdatesAsync(announce: false);
        _tray.BalloonTipClicked += OnBalloonClicked;
        // GetUserNameExW can block on a domain lookup: read it off the UI thread, then push the name.
        GeneralBehaviour.WarmAccountDisplayName(() => OnUi(RequestShellPush));
        _talk.StateChanged += _ => OnUi(RequestShellPush);
        _talk.TurnCommitted += turn => OnUi(() => { _shellTurns = null; _shell?.EndTurn(turn.Assistant); });
        _talk.MessageReceived += (role, text) => OnUi(() =>
        {
            // Startup history replay happens before the shell is active; history reaches it through state.
            if (_shell is { IsActive: true }) _shell.AddLiveRow(role switch { "You" => ChatRole.User, "OpenClicky" => ChatRole.Assistant, _ => ChatRole.Event }, text);
        });
        _talk.AssistantResponseChanged += (text, complete) => OnUi(() => { if (_shell is { IsActive: true }) _shell.StreamAssistant(text, complete); });
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) => OnUi(RequestShellPush);

    private void RequestShellPush() => _shell?.RequestPush();

    /// <summary>History changed outside a turn (cleared, or persistence toggled): reload it and drop live rows.</summary>
    private void ResetShellChat()
    {
        _shellTurns = null;
        _shell?.EndTurn();
    }

    /// <summary>The Run value must point at this exe when enabled (it may have moved) and be gone when disabled.</summary>
    private void ReconcileLaunchAtLogin()
    {
        try { GeneralBehaviour.SetLaunchAtLogin(_settings.LaunchAtLogin); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { Log("launch_at_login_error=" + ex.GetType().Name); }
    }

    private async Task StartShellAsync()
    {
        ReconcileLaunchAtLogin();
        _shell = new ShellController(this, CreateShellSurfaceAsync, push => { _pendingPush = push; _shellPushTimer.Start(); });
        var started = await _shell.StartAsync();
        if (_lifetime.IsCancellationRequested) return;
        if (started)
        {
            ApplyTaskbarPresence();
            ApplyCaptureExclusion();
        }
        // Updates are checked in classic fallback mode too; there they announce through the tray.
        _updateTimer.Start();
        await CheckUpdatesAsync(announce: false);
    }

    private async Task<IShellSurface> CreateShellSurfaceAsync()
    {
        var runtime = await _workspaceReady.Task;
        using (var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
        using (var page = await probe.GetAsync(new Uri(runtime.Origin, "shell.html"), HttpCompletionOption.ResponseHeadersRead, _lifetime.Token))
            if (!page.IsSuccessStatusCode) throw new FileNotFoundException("The packaged shell page is missing.", "shell.html");
        // The shell page never calls the workspace runtime API, so it gets its own bootstrap token, not the runtime's.
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        _shellSurface = await ShellSurfaceAdapter.CreateAsync(runtime.Origin, token, WebViewFolder, !_settings.ShowInScreenRecordings);
        return _shellSurface;
    }

    ShellInputs IShellHost.CollectInputs()
    {
        // Each source is guarded: one failing store must not leave the shell without state.
        var now = DateTimeOffset.UtcNow;
        var routines = ShellSource<IReadOnlyList<Routine>>("routines", () => _work.Routines(), []);
        var jobs = ShellSource("jobs", () => _work.Jobs().OrderByDescending(j => j.LastRunAt ?? j.CreatedAt).Take(ShellStateBuilder.MaxJobs).Select(job =>
        {
            var steps = _taskRuns.LastOrDefault(r => r.JobId == job.Id)?.Events
                .Select(e => e.Message).Where(m => !string.IsNullOrWhiteSpace(m)).TakeLast(ShellStateBuilder.MaxBullets).ToList() ?? [];
            if (!_jobOutputs.TryGetValue(job.Id, out var files))
                _jobOutputs[job.Id] = files = ShellSource<IReadOnlyList<string>>("job outputs", () =>
                {
                    var output = _work.OutputFolder(job);
                    return _work.Outputs(job).Take(ShellStateBuilder.MaxFiles).Select(name => Path.Combine(output, name)).ToList();
                }, []);
            return new ShellJobInput(job, files, steps, steps.LastOrDefault(), routines.Any(r => r.JobId == job.Id && RoutineSchedule.IsDue(r, now)));
        }).ToList(), []);
        // History is cached only once it loaded; a failed load is retried on the next push.
        var chat = _shellTurns ?? ShellSource<IReadOnlyList<ConversationTurn>>("history", () =>
        {
            IReadOnlyList<ConversationTurn> turns = _settings.PersistConversationHistory ? ConversationHistoryStore.LoadTurns(maxTurns: 100) : [];
            _shellTurns = turns;
            return turns;
        }, []);
        return new ShellInputs(_settings, jobs, chat, ShellSource("connectors", _connectors.List, []), CurrentPhase(), now)
        {
            Theme = ShellSource("theme", GeneralBehaviour.ReadTheme, ShellTheme.Light),
            ReducedMotion = ShellSource("reduced motion", GeneralBehaviour.ReducedMotion, false),
            FallbackUserName = Environment.UserName,
            AccountDisplayName = GeneralBehaviour.AccountDisplayName,
            CurrentVersion = CurrentVersion,
        };
    }

    private static T ShellSource<T>(string name, Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log($"shell: input {name} failed: {ex.GetType().Name}");
            return fallback;
        }
    }

    private ShellPhase CurrentPhase() => _settings.Paused ? ShellPhase.Paused : _talk.State switch
    {
        CompanionVoiceState.Listening => ShellPhase.Listening,
        CompanionVoiceState.Processing => ShellPhase.Thinking,
        CompanionVoiceState.Responding or CompanionVoiceState.Pointing => ShellPhase.Speaking,
        _ => _taskCts is not null ? ShellPhase.Working : ShellPhase.Idle,
    };

    void IShellHost.ToggleTalk() => ToggleConversation();

    void IShellHost.SubmitText(string text) => _ = SubmitRequestAsync(text, agent: false, fromShell: true);

    void IShellHost.OpenJob(string id) => _jobOutputs.Remove(id);

    void IShellHost.StopJob(string id)
    {
        if (_activeJob?.Job.Id != id) { Log("shell: job.stop ignored, that job is not running"); return; }
        CancelTask();
        _taskControl.Resume();
    }

    void IShellHost.SetGeneral(string key, bool value)
    {
        switch (key)
        {
            case "showInTaskbar": _settings.ShowInTaskbar = value; ApplyTaskbarPresence(); break;
            case "showInRecordings": _settings.ShowInScreenRecordings = value; ApplyCaptureExclusion(); break;
            case "launchAtLogin":
                try { GeneralBehaviour.SetLaunchAtLogin(value); _settings.LaunchAtLogin = value; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                { Log("launch_at_login_error=" + ex.GetType().Name); }
                break;
            case "paused": SetPaused(value); return;
        }
        SettingsStore.Save(_settings);
    }

    void IShellHost.OpenFile(string path) => Launch(new ProcessStartInfo(path) { UseShellExecute = true });

    void IShellHost.RevealFile(string path) => Launch(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));

    void IShellHost.OpenUrl(string url) => OpenUrl(url);

    void IShellHost.CheckForUpdates() => _ = CheckUpdatesAsync(announce: false);

    void IShellHost.InstallUpdate() => _ = InstallUpdateAsync();

    void IShellHost.Quit() => ExitThread();

    void IShellHost.ShowClassic()
    {
        if (_lifetime.IsCancellationRequested) return; // quitting while the shell was still starting
        ApplyTaskbarPresence(); // the shell is gone: drop its taskbar button
        if (!_background) ShowClassic();
        _tray.ShowBalloonTip(5000, "OpenClicky", _background
            ? "The new shell could not start. Open the classic window from the tray."
            : "The new shell could not start, so the classic window is open.", ToolTipIcon.Warning);
    }

    void IShellHost.Trace(string message) => Log("shell: " + message);

    private static void OpenUrl(string url) => Launch(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void Launch(ProcessStartInfo start)
    {
        try { Process.Start(start)?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        { Log("shell_open_error=" + ex.GetType().Name); }
    }

    private void SetPaused(bool paused)
    {
        _settings.Paused = paused;
        SettingsStore.Save(_settings);
        if (paused) _hotkey.Stop();
        else
        {
            try { _hotkey.Start(); }
            catch (InvalidOperationException ex) { Log("hotkey_resume_error=" + ex.Message); }
        }
        if (_pauseItem is not null) _pauseItem.Text = paused ? "Resume" : "Pause";
        RequestShellPush();
    }

    private void ApplyTaskbarPresence()
    {
        if (_settings.ShowInTaskbar && _shell is { IsActive: true })
        {
            if (_taskbar is { IsDisposed: false }) return;
            _taskbar = new TaskbarPresence(() => _shell?.OpenNotch(),
                deactivated => _shellSurface is { } surface && !surface.Window.IsDisposed && deactivated == surface.Window.Handle);
            _taskbar.Show();
        }
        else
        {
            _taskbar?.Dispose();
            _taskbar = null;
        }
    }

    /// <summary>"Show in screen recordings" off excludes the shell and the buddy overlay windows that exist now.</summary>
    private void ApplyCaptureExclusion()
    {
        var exclude = !_settings.ShowInScreenRecordings;
        if (_shellSurface is not null) _shellSurface.Window.ExcludeFromCapture = exclude;
        foreach (var hwnd in new[] { _overlay.WindowHandle, _overlay.BuddyTipHandle })
            if (hwnd != 0 && !GeneralBehaviour.ApplyCaptureExclusion(hwnd, exclude)) Log("overlay_capture_affinity_failed");
    }

    private async Task CheckUpdatesAsync(bool announce)
    {
        _shell?.SetUpdate(new ShellUpdate(CurrentVersion, _release?.Version, UpdateState.Checking, 0));
        UpdateCheckResult? result;
        try { result = await new UpdateChecker(_updateHttp, CurrentVersion).CheckAsync(_lifetime.Token); }
        catch (OperationCanceledException) { return; }
        var state = result is null ? UpdateState.Error : result.Status == UpdateStatus.Available ? UpdateState.Available : UpdateState.UpToDate;
        _release = result?.Status == UpdateStatus.Available ? result.Release : null;
        // Each new release is announced through the tray once (shell or classic); clicking the balloon downloads it.
        if (state == UpdateState.Available && _release!.Version != _announcedRelease) announce = true;
        _shell?.SetUpdate(new ShellUpdate(CurrentVersion, _release?.Version, state, 0));
        if (!announce) return;
        if (state == UpdateState.Available)
        {
            _announcedRelease = _release!.Version;
            _tray.ShowBalloonTip(5000, UpdateAvailableTitle, $"OpenClicky {_release.Version} is available — click to download.", ToolTipIcon.Info);
        }
        else
            _tray.ShowBalloonTip(5000, "OpenClicky updates", state == UpdateState.UpToDate
                ? $"OpenClicky {CurrentVersion} is up to date."
                : "Could not reach GitHub to check for updates.", ToolTipIcon.Info);
    }

    /// <summary>Only the "update available" balloon carries this title, so a click on it (and no other) installs.</summary>
    private void OnBalloonClicked(object? sender, EventArgs e)
    {
        if (_tray.BalloonTipTitle == UpdateAvailableTitle) _ = InstallUpdateAsync();
    }

    /// <summary>Downloads the portable ZIP to %LOCALAPPDATA%\OpenClicky\updates, then shows it and the release page. A second call while one runs is ignored.</summary>
    private async Task InstallUpdateAsync()
    {
        if (_installingUpdate) { Log("update: install already running"); return; }
        _installingUpdate = true;
        try { await InstallUpdateCoreAsync(); }
        finally { _installingUpdate = false; }
    }

    private async Task InstallUpdateCoreAsync()
    {
        if (_release is not { } release) { await CheckUpdatesAsync(announce: true); return; }
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "updates");
        var progress = new Progress<double>(p => _shell?.SetUpdate(new ShellUpdate(CurrentVersion, release.Version, UpdateState.Downloading, Math.Clamp(p, 0, 1))));
        _shell?.SetUpdate(new ShellUpdate(CurrentVersion, release.Version, UpdateState.Downloading, 0));
        string? zip;
        try { zip = await new UpdateChecker(_updateHttp, CurrentVersion).DownloadAsync(release, folder, progress, _lifetime.Token); }
        catch (OperationCanceledException) { return; }
        _shell?.SetUpdate(new ShellUpdate(CurrentVersion, release.Version, zip is null ? UpdateState.Error : UpdateState.Ready, zip is null ? 0 : 1));
        if (zip is not null) Launch(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\""));
        OpenUrl(release.ReleasePage.AbsoluteUri);
    }

    private void DisposeShell()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _shellPushTimer.Dispose();
        _updateTimer.Dispose();
        _shell?.Dispose();
        _taskbar?.Dispose();
        _updateHttp.Dispose();
    }
}
