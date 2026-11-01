using System.Text;
using OpenClicky.App;
using OpenClicky.Core;
using OpenClicky.Core.Dictation;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using OpenClicky.Input;
using NAudio.Wave;
using OpenClicky.Providers;
using OpenClicky.Agent;
using System.Diagnostics;
using System.Windows.Automation;
using System.IO;
using System.Net.Http;
using System.Reflection;

if (args.Length == 3 && args[0] == "--openwork-live-gate") return OpenWorkGateCheck.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
if (args.Length is 1 or 2 && args[0] == "--chatgpt-latency")
    return await ChatGptLatencyCheck.RunAsync(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
if (args.Length is 1 or 2 && args[0] == "--chatgpt-latency-production")
    return await ChatGptLatencyCheck.RunAsync(args.Length == 2 ? Path.GetFullPath(args[1]) : null, productionPath: true);
if (args.Length == 1 && args[0] == "--chatgpt-artifact-live") return await ChatGptArtifactLiveCheck.RunAsync();
if (args.Length == 1 && args[0] == "--chatgpt-artifact-live-actual") return await ChatGptArtifactLiveCheck.RunActualContextAsync();
if (args.Contains("--opencode")) return await CheckOpenCodeAsync();
if (args.Contains("--home-ui-state")) return CheckHomeUiState();
if (args.Contains("--companion-shape"))
{
    Exception? failure = null;
    var ui = new Thread(() =>
    {
        try
        {
            using var dock = new CompanionDockForm(() => { }, () => { }, () => { }, () => { }, () => { });
            if (dock.Region is null || dock.Region.IsVisible(0, 0) || !dock.Region.IsVisible(72, 34) || !dock.Region.IsVisible(72, 75))
                throw new InvalidOperationException("Passive companion shape must keep its circle/caption and leave unused desktop clickable.");
            dock.OfferHelp(() => { }, () => { });
            if (!dock.Region!.IsVisible(100, 130)) throw new InvalidOperationException("Help offer was clipped.");
            dock.DismissOffer();
            if (dock.Region!.IsVisible(100, 130)) throw new InvalidOperationException("Dismissed offer still covers the desktop.");
        }
        catch (Exception ex) { failure = ex; }
    });
    ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
    if (failure is not null) throw failure;
    Console.WriteLine("PASS companion circle/caption hit region, help offer and dismissal");
    return 0;
}
if (args.Contains("--reopen-signal")) return CheckReopenSignal();
if (args.Contains("--dictation-ui-state")) return DictationPanelCheck.Run();
if (args.Contains("--connectors")) return await ConnectorsCheck.RunAsync();
if (args.Contains("--clipboard-shelf")) return ClipboardShelfFormCheck.Run();
if (args.Contains("--clipboard-sequence")) return ClipboardSequenceCheck.Run();
if (args.Contains("--chatgpt-session-state"))
{
    // Diagnostic: which HasUsableSession condition fails. Prints flags only, never a token.
    var c = ChatGptCredentialStore.Load(new WindowsCredentialSecretStore());
    if (c is null) { Console.WriteLine("no saved ChatGPT credential"); return 1; }
    Console.WriteLine($"usable={c.HasUsableSession} access={!string.IsNullOrWhiteSpace(c.AccessToken)} refresh={!string.IsNullOrWhiteSpace(c.RefreshToken)} "
        + $"clientId={!string.IsNullOrWhiteSpace(c.ClientId)} dynamicClient={c.ClientId == "dynamic_agent_client"} subject={!string.IsNullOrWhiteSpace(c.Subject)} "
        + $"issuerOk={c.Issuer == "https://auth.openai.com"} planScope={c.Scopes?.Contains("chatgpt.tokens.use.direct") == true} scopes={c.Scopes?.Count ?? 0} "
        + $"expiresAt={c.AccessTokenExpiresAt:u} savedAt={c.SavedAt:u}");
    return 0;
}
if (args.Contains("--clipboard-private"))
{
    // Writes three test copies to the clipboard and puts the user's text back afterwards.
    string? failure = null;
    var ui = new Thread(() =>
    {
        var saved = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        try
        {
            var secret = new DataObject();
            secret.SetText("smoke-secret", TextDataFormat.UnicodeText);
            secret.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            Clipboard.SetDataObject(secret, copy: true);
            if (!OpenClicky.App.ClipboardCollector.IsMarkedPrivate()) failure = "a password-manager style copy was not seen as private";
            var noHistory = new DataObject();
            noHistory.SetText("smoke-no-history", TextDataFormat.UnicodeText);
            noHistory.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0u)));
            Clipboard.SetDataObject(noHistory, copy: true);
            if (failure is null && !OpenClicky.App.ClipboardCollector.IsMarkedPrivate()) failure = "CanIncludeInClipboardHistory=0 was not seen as private";
            Clipboard.SetText("smoke-plain", TextDataFormat.UnicodeText);
            if (failure is null && OpenClicky.App.ClipboardCollector.IsMarkedPrivate()) failure = "a plain copy was seen as private";
        }
        finally
        {
            if (saved is null) Clipboard.Clear(); else Clipboard.SetText(saved);
        }
    });
    ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
    if (failure is not null) { Console.Error.WriteLine("FAIL " + failure); return 1; }
    Console.WriteLine("PASS private copies (exclude flag, history off) are recognised; a plain copy is not; clipboard text restored");
    return 0;
}
if (args.Length == 2 && args[0] == "--agent-type")
{
    // The agent's own type_text tool, as a job or task runs it: after 8 s, into whatever is focused; reads the field back.
    await Task.Delay(8000);
    var field = AutomationElement.FocusedElement;
    string? Read() => field.TryGetCurrentPattern(ValuePattern.Pattern, out var v) ? ((ValuePattern)v).Current.Value
        : field.TryGetCurrentPattern(TextPattern.Pattern, out var t) ? ((TextPattern)t).DocumentRange.GetText(-1) : null;
    var before = Read();
    var typed = await new CompositeAgentExecutor().ExecuteAsync(new(AgentToolNames.TypeText,
        new Dictionary<string, string> { ["text"] = args[1] }, "smoke: agent type_text"), userConfirmed: true);
    await Task.Delay(200);
    var exact = DictationInsertionProof.IsExactContiguousChange(before, Read(), args[1]);
    Console.WriteLine($"{Process.GetProcessById(field.Current.ProcessId).ProcessName}: {typed.Message}; exact={exact}");
    return typed.Success && exact ? 0 : 1;
}
if (args.Length is 1 or 2 && args[0] == "--focused-field")
{
    // Diagnostic: after 8 s, what UIA exposes for the focused control and whether dictation captures it; with text, insert it.
    await Task.Delay(8000);
    var focused = AutomationElement.FocusedElement;
    var c = focused.Current;
    var hasValue = focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp);
    Console.WriteLine($"process={Process.GetProcessById(c.ProcessId).ProcessName} type={c.ControlType.ProgrammaticName} class={c.ClassName} framework={c.FrameworkId} "
        + $"password={c.IsPassword} value={hasValue} readOnly={(hasValue ? ((ValuePattern)vp).Current.IsReadOnly : (bool?)null)} "
        + $"text={focused.TryGetCurrentPattern(TextPattern.Pattern, out _)} editable={(bool)focused.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty)}");
    var inner = focused.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));
    if (inner is not null)
        Console.WriteLine($"inner focus: type={inner.Current.ControlType.ProgrammaticName} name={inner.Current.Name} patterns="
            + string.Join(",", inner.GetSupportedPatterns().Select(p => p.ProgrammaticName)));
    var captured = DictationTarget.TryCapture(out var field, out var failure, out var why);
    Console.WriteLine($"capture={captured} failure={failure} readable={field?.CanVerifyText} {why}");
    if (args.Length == 2 && field is not null)
        Console.WriteLine((await field.InsertAsync(args[1], new CompositeAgentExecutor(), requireCurrentFocus: true)).Message);
    return captured ? 0 : 1;
}
if (args.Contains("--automatic-dictation")) return AutomaticDictationCheck.Run();
if (args.Contains("--whisper-warm-reuse")) return await WhisperWarmReuseCheck.RunAsync();
if (args.Length == 3 && args[0] == "--visual-workspace") return VisualWorkspaceCheck.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
if (args.Contains("--demo-dictation"))
{
    var ui = new Thread(RunDictationUiFixture);
    ui.SetApartmentState(ApartmentState.STA);
    ui.Start();
    ui.Join();
    return 0;
}
if (args.Contains("--demo-home"))
{
    var ui = new Thread(() =>
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        HomeForm? home = null;
        home = new HomeForm(new AppSettings(), (text, agent) =>
        {
            home!.AddMessage("Fixture receipt", $"Mode: {(agent ? "Agent" : "Ask")}; request: {text}");
            return Task.CompletedTask;
        }, () => { }, () => { }, () => Task.CompletedTask, () => Task.CompletedTask,
            () => { }, _ => { }, () => Task.CompletedTask, () => { }, () => Task.CompletedTask,
            () => { }, () => { });
        home.Text = "OpenClicky acceptance fixture";
        home.SetConnection("UI fixture · no model or desktop actions · existing records untouched");
        if (args.Contains("--demo-rich"))
        {
            home.AddMessage("Fixture", "Collect copied links, review the list, then paste them together. This is a local layout fixture.");
            home.ShowIntelligentUi(new("Copied links flow", "Collect, review, and paste links together",
                [new("Status", "Ready to collect", "A local visual fixture; no clipboard data is read.", "neutral")],
                [new("collect", "Collect links", "Copy each link into the shelf.", "current"),
                 new("review", "Review links", "Check the collected links for accuracy and duplicates.", "next"),
                 new("paste", "Paste together", "Use Copy all, then paste into your chosen destination.", "next")],
                [new("collect", "review", ""), new("review", "paste", "")],
                [new("Plan a link collector", "Help me plan a link collector.")]));
        }
        Application.Run(home);
    });
    ui.SetApartmentState(ApartmentState.STA);
    ui.Start();
    ui.Join();
    return 0;
}
if (args.Contains("--installed-apps"))
{
    var executor = new OpenClicky.Agent.CompositeAgentExecutor();
    var result = await executor.ExecuteAsync(new("list_apps", new Dictionary<string,string> { ["query"] = "Notepad" }, "Find harmless launch fixture"), false);
    if (!result.Success || !result.Message.Contains("app_id")) Fail("Installed app catalogue did not return Notepad.");
    var blocked = await executor.ExecuteAsync(new("open_app", new Dictionary<string,string> { ["app_id"] = "invalid" }, "Blocked probe"), false);
    if (blocked.Success || !blocked.RequiresConfirmation) Fail("App launch bypassed confirmation.");
    var invalid = await executor.ExecuteAsync(new("open_app", new Dictionary<string,string> { ["app_id"] = "invalid" }, "Invalid catalogue id"), true);
    if (invalid.Success) Fail("Unknown app id launched.");
    var invalidQuery = await executor.ExecuteAsync(new("list_apps", new Dictionary<string,string>(), null), false);
    if (invalidQuery.Success) Fail("Empty app search was accepted.");
    Console.WriteLine("PASS installed app lookup; missing query, missing approval and unknown id rejected; no app launched");
    return 0;
}

if (args.Contains("--speech")) return await CheckSpeechAsync();
if (args.Contains("--whisper")) return await CheckWhisperAsync();
if (args.Contains("--whisper-compare")) return await CompareWhisperModelsAsync();
if (args.Contains("--download-voice"))
{
    var last = -1;
    await LocalWhisperModel.DownloadAsync(WhisperModels.English, new Progress<int>(percent => { if (percent / 10 != last) { last = percent / 10; Console.WriteLine($"Voice model: {percent}%"); } }));
    Console.WriteLine("PASS voice model size and SHA-256 verified");
    return 0;
}
if (args.Contains("--microphones"))
{
    foreach (var device in NAudioMicrophoneRecorder.ListDevices()) Console.WriteLine(device.Name + " [" + (device.Id ?? "Windows default") + "]");
    return 0;
}
if (args.Contains("--capture-selected-mic"))
{
    var settings = SettingsStore.Load();
    using var microphone = new NAudioMicrophoneRecorder(() => settings.MicrophoneDeviceId);
    microphone.Start();
    await Task.Delay(700);
    var snapshot = microphone.SnapshotToWav();
    await Task.Delay(300);
    var final = microphone.StopToWav();
    using var reader = new WaveFileReader(new MemoryStream(final));
    if (snapshot.Length <= 44 || reader.Length == 0 || reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16 || microphone.IsRecording)
        Fail("Selected microphone did not produce a usable in-memory snapshot and final WAV.");
    Console.WriteLine($"PASS selected microphone capture: {NAudioMicrophoneRecorder.DeviceName(settings.MicrophoneDeviceId)}; snapshot_bytes={snapshot.Length}; final_samples={reader.Length / 2}; audio discarded");
    return 0;
}
if (args.Contains("--aec-selected-mic")) return await CheckSelectedMicrophoneAecAsync();

// Local smoke: CredWrite for small secrets + DPAPI fallback for OAuth-sized blobs.
Console.WriteLine("OpenClicky secret-store smoke starting…");

var store = new WindowsCredentialSecretStore();
const string smallName = "_smoke-small";
const string largeName = "_smoke-large-oauth";
const string namedSmoke = "chatgpt-oauth-smoke";

try
{
    store.Delete(smallName);
    store.Delete(largeName);
    store.Delete(namedSmoke);

    store.Set(smallName, "openclicky-small-secret");
    var smallRoundTrip = store.Get(smallName);
    if (smallRoundTrip != "openclicky-small-secret")
    {
        Fail($"small round-trip mismatch: '{smallRoundTrip}'");
    }

    Console.WriteLine("OK small CredWrite/CredRead round-trip");

    // Mimic ChatGPT OAuth JSON (three JWT-sized fields) — exceeds 2560-byte CredMan blob limit.
    var large = "{\"email\":\"smoke@example.com\",\"accessToken\":\"ACCESS_"
        + new string('A', 1800)
        + "\",\"refreshToken\":\"REFRESH_"
        + new string('B', 1800)
        + "\",\"idToken\":\"ID_"
        + new string('C', 1800)
        + "\",\"clientId\":\"client_smoke\",\"scopes\":[\"chatgpt.tokens.use.direct\"]}";

    var unicodeBytes = Encoding.Unicode.GetByteCount(large);
    Console.WriteLine($"large_payload unicode_bytes={unicodeBytes} utf8_bytes={Encoding.UTF8.GetByteCount(large)}");
    if (unicodeBytes <= 2560)
    {
        Fail("test payload too small to exercise DPAPI fallback");
    }

    store.Set(largeName, large);
    var largeRoundTrip = store.Get(largeName);
    if (largeRoundTrip != large)
    {
        Fail("large OAuth-sized round-trip mismatch (DPAPI path)");
    }

    var dpapiPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenClicky",
        "secrets",
        largeName + ".dpapi");
    if (!File.Exists(dpapiPath))
    {
        Fail($"expected DPAPI file missing: {dpapiPath}");
    }

    Console.WriteLine($"OK large DPAPI round-trip file={dpapiPath}");

    store.Set(namedSmoke, large);
    if (store.Get(namedSmoke) != large)
    {
        Fail("named oauth-smoke round-trip failed");
    }

    store.Delete(namedSmoke);
    store.Delete(smallName);
    store.Delete(largeName);

    if (store.Get(smallName) is not null || store.Get(largeName) is not null || store.Get(namedSmoke) is not null)
    {
        Fail("delete did not clear secrets");
    }

    Console.WriteLine("OpenClicky secret-store smoke PASSED");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    try
    {
        store.Delete(smallName);
        store.Delete(largeName);
        store.Delete(namedSmoke);
    }
    catch
    {
        // ignore cleanup errors
    }

    return 1;
}

static void RunDictationUiFixture()
{
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    var sandboxExe = ResolveSandboxExe() ?? throw new InvalidOperationException("Build OpenClicky.Sandbox Release before starting the dictation UI fixture.");
    using var sandbox = Process.Start(new ProcessStartInfo(sandboxExe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(sandboxExe)! })
        ?? throw new InvalidOperationException("Could not start the dictation sandbox.");
    DictationTarget? target = null;
    var executor = new CompositeAgentExecutor();
    HomeForm? home = null;
    home = new HomeForm(new AppSettings(), (_, _) => Task.CompletedTask, () => { }, () => { },
        () => Task.CompletedTask, () => Task.CompletedTask, () => { }, _ => { }, () => Task.CompletedTask,
        () => { }, () => Task.CompletedTask, () => { }, () => { },
        beginDictation: async () =>
        {
            home!.MinimizeForRequest();
            var window = await WaitForSandboxWindowAsync(sandbox.Id, TimeSpan.FromSeconds(10));
            var edit = window?.FindFirst(TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.NameProperty, "Type target")));
            if (edit is null) { home.Show(); home.RestoreFromMinimized(); home.SetStatus("Fixture target was not found."); return; }
            var point = edit.GetClickablePoint();
            await executor.ExecuteAsync(new(AgentToolNames.Click, new Dictionary<string, string>
            {
                ["x"] = ((int)Math.Round(point.X)).ToString(), ["y"] = ((int)Math.Round(point.Y)).ToString(),
            }, "User started dictation fixture"), userConfirmed: true);
            await Task.Delay(200);
            if (!DictationTarget.TryCapture(out target, out var error) || target is null)
            { home.Show(); home.RestoreFromMinimized(); home.SetStatus(error); return; }
            home.Show(); home.RestoreFromMinimized(); home.Activate();
            home.BeginDictation(target.AppTitle, target.ControlName);
            home.AcceptDictationTranscript("deterministic local dictation draft");
        },
        toggleDictationRecording: () =>
        {
            home!.SetDictationResult("Deterministic fixture transcript is already loaded; no microphone was opened.");
            return Task.CompletedTask;
        },
        insertDictation: async draft =>
        {
            if (target is null) { home!.SetDictationResult("Start the fixture first."); return; }
            var result = await target.InsertAsync(draft, executor);
            home!.SetDictationResult(result.Message);
            if (result.InputAttempted) target = null;
        },
        cancelDictation: () => { target = null; home!.EndDictation(); });
    home.Text = "OpenClicky dictation acceptance fixture";
    home.SetConnection("Fixture · click Dictate into app, review raw/draft, then Insert · no microphone, model, settings or saved data");
    try { Application.Run(home); }
    finally { if (!sandbox.HasExited) sandbox.Kill(entireProcessTree: true); }
}

static string? ResolveSandboxExe()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
    {
        foreach (var path in new[]
        {
            Path.Combine(dir.FullName, "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe"),
            Path.Combine(dir.FullName, "apps", "windows", "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe"),
        }) if (File.Exists(path)) return path;
    }
    return null;
}

static async Task<AutomationElement?> WaitForSandboxWindowAsync(int processId, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var window = AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "OpenClicky Agent Sandbox"),
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId)));
        if (window is not null) return window;
        await Task.Delay(150);
    }
    return null;
}

static void Fail(string message)
{
    throw new InvalidOperationException(message);
}

static async Task<int> CheckSelectedMicrophoneAecAsync()
{
    var settings = SettingsStore.Load();
    var recorderType = typeof(HomeForm).Assembly.GetType("OpenClicky.App.EchoCancelledMicrophoneRecorder")
        ?? throw new InvalidOperationException("The production echo-cancelled recorder is unavailable.");
    var factory = new Func<string?>(() => settings.MicrophoneDeviceId);
    var logs = new List<string>();
    IMicrophoneRecorder? microphone = null;
    byte[] captured = [];
    try
    {
        microphone = Activator.CreateInstance(recorderType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [factory, new Action<string>(logs.Add)], culture: null) as IMicrophoneRecorder
            ?? throw new InvalidOperationException("The production echo-cancelled recorder could not be created.");
        microphone.Start();
        await Task.Delay(1000);
        var reportsAec = microphone.SupportsEchoCancellation;
        captured = microphone.StopToWav();
        using var reader = new WaveFileReader(new MemoryStream(captured, writable: false));
        if (reader.Length == 0 || reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1
            || reader.WaveFormat.BitsPerSample != 16 || microphone.IsRecording)
            Fail("Production communications capture did not return 16 kHz mono PCM16 audio.");
        var aec = reportsAec ? "active" : "unavailable";
        Console.WriteLine($"PASS production communications capture: {NAudioMicrophoneRecorder.DeviceName(settings.MicrophoneDeviceId)}; aec={aec}; sample_rate=16000; channels=1; bits=16; samples={reader.Length / 2}; audio discarded");
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"UNSUPPORTED production communications capture: {NAudioMicrophoneRecorder.DeviceName(settings.MicrophoneDeviceId)}; {ex.Message}; audio discarded");
        return 0;
    }
    finally
    {
        microphone?.Dispose();
        Array.Clear(captured);
    }
}

static int CheckReopenSignal()
{
    var name = @"Local\OpenClicky.Test.Reopen." + Guid.NewGuid().ToString("N");
    using var host = new EventWaitHandle(false, EventResetMode.AutoReset, name);
    using var secondLaunch = new EventWaitHandle(false, EventResetMode.AutoReset, name);
    using var received = new AutoResetEvent(false);
    var callbacks = 0;
    // A second launch during startup must remain queued until the UI listener is ready.
    secondLaunch.Set();
    var listener = OpenClicky.App.Program.RegisterReopenRequest(host, () =>
    {
        Interlocked.Increment(ref callbacks);
        received.Set();
    });
    try
    {
        if (!received.WaitOne(TimeSpan.FromSeconds(5))) Fail("Startup reopen request was lost.");
        secondLaunch.Set();
        if (!received.WaitOne(TimeSpan.FromSeconds(5))) Fail("Later reopen request was lost.");
        if (Volatile.Read(ref callbacks) != 2) Fail("Unexpected reopen callback count.");
    }
    finally
    {
        using var stopped = new ManualResetEvent(false);
        if (listener.Unregister(stopped)) stopped.WaitOne(TimeSpan.FromSeconds(5));
    }
    Console.WriteLine("PASS queued and repeated named-event reopen requests; no app, microphone, or user data opened");
    return 0;
}

static int CheckHomeUiState()
{
    var state = new HomeUiState();
    state.ObserveWindowState(FormWindowState.Maximized);
    state.ObserveWindowState(FormWindowState.Minimized);
    if (state.RestoreWindowState != FormWindowState.Maximized)
        Fail("Minimizing discarded the maximized restore state.");

    if (state.MessageAppended(chatSelected: false, windowVisible: true, readerAtLatest: false))
        Fail("A hidden Chat requested an immediate scroll.");
    if (!state.ConsumeDeferredChatScroll() || state.ConsumeDeferredChatScroll())
        Fail("Hidden Chat append did not schedule exactly one deferred scroll.");
    if (state.MessageAppended(chatSelected: true, windowVisible: true, readerAtLatest: false))
        Fail("A visible reader above the latest message was forced to scroll.");
    if (!state.MessageAppended(chatSelected: true, windowVisible: true, readerAtLatest: true))
        Fail("A visible reader at the latest message did not follow the append.");

    var minimizedAtLatest = new HomeUiState();
    minimizedAtLatest.WindowWillHide(chatSelected: true, readerAtLatest: true);
    if (minimizedAtLatest.MessageAppended(chatSelected: true, windowVisible: false, readerAtLatest: false)
        || !minimizedAtLatest.WindowBecameVisible(chatSelected: true)
        || minimizedAtLatest.WindowBecameVisible(chatSelected: true))
        Fail("A minimized reader at the latest message did not receive exactly one deferred scroll on restore.");

    var minimizedWhileReadingOlder = new HomeUiState();
    minimizedWhileReadingOlder.WindowWillHide(chatSelected: true, readerAtLatest: false);
    if (minimizedWhileReadingOlder.MessageAppended(chatSelected: true, windowVisible: false, readerAtLatest: false)
        || minimizedWhileReadingOlder.WindowBecameVisible(chatSelected: true))
        Fail("A minimized reader deliberately above the latest message was forced to scroll on restore.");

    var startup = new HomeUiState();
    if (!startup.ConsumeInitialChatScroll(chatSelected: true, hasMessages: true)
        || startup.ConsumeInitialChatScroll(chatSelected: true, hasMessages: true))
        Fail("Startup history did not request exactly one initial Chat scroll.");

    var old = new TaskRunRecord("old", "previous", DateTimeOffset.UnixEpoch, "completed", [], "ConfirmEach");
    var current = new TaskRunRecord("current", "new", DateTimeOffset.UnixEpoch.AddMinutes(1), "paused", [], "ConfirmEach");
    if (TaskRunSelection.Resolve([old], "old", [old, current]) != "current")
        Fail("A newly inserted task did not replace the historical selection.");
    if (TaskRunSelection.Resolve([old, current], "old", [old, current with { Status = "executing" }]) != "old")
        Fail("An existing task update replaced the explicit historical selection.");

    foreach (var terminal in new[] { AgentProgressStates.Completed, AgentProgressStates.Stopped, AgentProgressStates.Failed })
    {
        if (TaskProgressUpdate.ShouldApply(terminal, AgentProgressStates.Paused))
            Fail($"A pause was allowed to overwrite terminal task status {terminal}.");
    }
    if (!TaskProgressUpdate.ShouldApply(AgentProgressStates.Executing, AgentProgressStates.Paused))
        Fail("A real active-task pause was discarded.");
    if (TaskProgressUpdate.ShouldApply(AgentProgressStates.Completed, AgentProgressStates.Stopped)
        || TaskProgressUpdate.ShouldApply(AgentProgressStates.Failed, AgentProgressStates.Stopped))
        Fail("A later stop was allowed to relabel a terminal task.");
    if (!TaskProgressUpdate.ShouldApply(AgentProgressStates.Executing, AgentProgressStates.Stopped)
        || !TaskProgressUpdate.ShouldApply(AgentProgressStates.Completed, AgentProgressStates.Completed))
        Fail("Normal active-to-terminal or same-terminal progress was discarded.");

    Console.WriteLine("PASS Home restore/scroll state, task selection and terminal progress guards");
    return 0;
}

static async Task<int> CheckSpeechAsync()
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (LocalSpeechSttClient.InstalledLanguages().Length == 0) Fail("No installed Windows speech language.");
        using var wav = new MemoryStream();
        using (var voice = new SpeechSynthesizer())
        {
            voice.SetOutputToWaveStream(wav);
            voice.Speak("Hello there. Please open the window.");
        }
        var transcript = await new LocalSpeechSttClient().TranscribeAsync(wav.ToArray(), timeout.Token);
        if (!transcript.Contains("window", StringComparison.OrdinalIgnoreCase)) Fail("Windows recognition did not recognize the fixture sentence: " + transcript);
        Console.WriteLine("PASS local Windows recognition from synthesized WAV: " + transcript);
        var speechLog = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var tts = new OpenClicky.App.Speech.SpeechOutput(() => new AppSettings(), new WindowsCredentialSecretStore(), _ => { }, speechLog.Enqueue) { Enabled = false };
        await tts.SpeakAsync("This should remain silent.");
        tts.Enabled = true;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await tts.SpeakAsync(string.Join(" ", Enumerable.Repeat("Testing cancellation of a spoken reply.", 30)), cancel.Token).WaitAsync(TimeSpan.FromSeconds(5));
            Fail("Speech was not cancelled.");
        }
        catch (OperationCanceledException) { }
        if (clock.Elapsed > TimeSpan.FromSeconds(5)) Fail("Speech cancellation took too long.");
        Console.WriteLine("PASS real Windows TTS cancellation in " + clock.ElapsedMilliseconds + "ms");

        // Real playback on the default device: words arrive in order, sentences are played one after another, the end is raised.
        var words = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var ended = 0;
        tts.WordSpoken += words.Enqueue;
        tts.SpeechEnded += () => Interlocked.Increment(ref ended);
        speechLog.Clear();
        await tts.SpeakAsync("Hello there. Two short sentences.").WaitAsync(TimeSpan.FromSeconds(20));
        if (string.Join(" ", words) != "Hello there Two short sentences") Fail("Words were not raised in order: " + string.Join("|", words));
        if (speechLog.Count(l => l.StartsWith("speech_play_start voice=windows")) != 2 || speechLog.Count(l => l == "speech_play_end") != 2 || ended != 1)
            Fail("Playback did not run once per sentence: " + string.Join(" ; ", speechLog));
        await tts.PlayTestSoundAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (!speechLog.Contains("speech_test_sound_end")) Fail("Test sound did not play: " + string.Join(" ; ", speechLog));
        Console.WriteLine("PASS real playback: " + string.Join(" ; ", speechLog));
        using (var endpoints = new NAudio.CoreAudioApi.MMDeviceEnumerator())
            foreach (var endpoint in endpoints.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
            {
                var number = OpenClicky.App.Speech.RenderDevices.WaveOutNumber(endpoint.ID);
                if (number < 0) Fail("No WaveOut device for the active output " + endpoint.FriendlyName);
                Console.WriteLine($"PASS output device {endpoint.FriendlyName} -> WaveOut {number}");
            }
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}

/// <summary>Speaks fixed phrases with Windows TTS and transcribes each with base.en and small.en (both must be installed); prints text and warm timing.</summary>
static async Task<int> CompareWhisperModelsAsync()
{
    string[] phrases = ["Draw me how a car engine works.", "Ask Ahmad to open OpenClicky and connect Grok.", "Circle the engine oil line in Notepad."];
    using var runtime = new LocalWhisperRuntime(_ => { });
    foreach (var model in new[] { WhisperModels.English, WhisperModels.SmallEnglish })
    {
        if (!LocalWhisperModel.IsAvailable(model)) { Console.WriteLine($"SKIP {model.Id}: not installed"); continue; }
        var plan = new WhisperDecodePlan(model, "en");
        await runtime.WarmAsync(model);
        foreach (var phrase in phrases)
        {
            using var wav = new MemoryStream();
            using (var voice = new SpeechSynthesizer())
            {
                voice.SetOutputToAudioStream(wav, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                voice.Speak(phrase);
            }
            var bytes = NAudioMicrophoneRecorder.PcmToWav(wav.ToArray(), new WaveFormat(16000, 16, 1));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var turn = new LocalWhisperSttClient(new VoiceFixture(bytes), runtime, plan);
            var text = await turn.TranscribeAsync(bytes, CancellationToken.None);
            Console.WriteLine($"{model.Id,-14} {clock.ElapsedMilliseconds,5} ms  {text}");
        }
    }
    return 0;
}

static async Task<int> CheckWhisperAsync()
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var wav = new MemoryStream();
        using (var voice = new SpeechSynthesizer())
        {
            voice.SetOutputToAudioStream(wav, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            voice.Speak("Explain what is on my screen.");
        }
        var bytes = NAudioMicrophoneRecorder.PcmToWav(wav.ToArray(), new WaveFormat(16000,16,1));
        var modelLoads = 0;
        var english = new WhisperDecodePlan(WhisperModels.English, "en");
        using var runtime = new LocalWhisperRuntime(message =>
        {
            if (message.StartsWith("local_whisper_model_load_ms=", StringComparison.Ordinal)) Interlocked.Increment(ref modelLoads);
        });
        var coldClock = System.Diagnostics.Stopwatch.StartNew();
        string cold;
        using (var coldTurn = new LocalWhisperSttClient(new VoiceFixture(bytes), runtime, english))
            cold = await coldTurn.TranscribeAsync(bytes, timeout.Token);
        var coldMs = coldClock.ElapsedMilliseconds;
        var warmClock = System.Diagnostics.Stopwatch.StartNew();
        string warm;
        using (var warmTurn = new LocalWhisperSttClient(new VoiceFixture(bytes), runtime, english))
            warm = await warmTurn.TranscribeAsync(bytes, timeout.Token);
        var warmMs = warmClock.ElapsedMilliseconds;
        if (!cold.Contains("what is on my screen", StringComparison.OrdinalIgnoreCase)
            || !warm.Contains("what is on my screen", StringComparison.OrdinalIgnoreCase))
            Fail($"Whisper shared-runtime request mismatch: cold={cold}; warm={warm}");
        if (modelLoads != 1) Fail($"Whisper model loaded {modelLoads} times across two turns; expected one.");
        Console.WriteLine($"PASS shared real Whisper runtime model_loads={modelLoads} cold_transcription_ms={coldMs} warm_transcription_ms={warmMs}. Synthesized WAV only; this is not microphone acceptance.");
        string prompted;
        using (var dictionaryTurn = new LocalWhisperSttClient(new VoiceFixture(bytes), runtime, english, WhisperPrompt.FromDictionary(["OpenClicky", "Ahmad"])))
            prompted = await dictionaryTurn.TranscribeAsync(bytes, timeout.Token);
        if (!prompted.Contains("what is on my screen", StringComparison.OrdinalIgnoreCase)) Fail("Whisper with a dictionary prompt mismatch: " + prompted);
        Console.WriteLine("PASS real Whisper with a dictionary prompt: " + prompted);
        var fixture = new VoiceFixture(bytes);
        using var live = new LocalWhisperSttClient(fixture, runtime, english);
        var preview = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        live.TranscriptChanged += text => { if (text.Length > 0) preview.TrySetResult(text); };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        live.StartListening();
        var partial = await preview.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var final = await live.FinishListeningAsync(timeout.Token);
        if (!final.Contains("what is on my screen", StringComparison.OrdinalIgnoreCase)) Fail("Whisper request mismatch: " + final);
        Console.WriteLine($"PASS Whisper live preview and final ({clock.ElapsedMilliseconds}ms): {partial} => {final}");
        using var silence = new MemoryStream();
        using (var writer = new WaveFileWriter(new LeaveOpenStream(silence), new WaveFormat(16000,16,1))) writer.Write(new byte[32000],0,32000);
        if (await live.TranscribeAsync(silence.ToArray(), timeout.Token) != "") Fail("Silence produced words.");
        Console.WriteLine("PASS silence produces no fabricated words");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await live.TranscribeAsync(bytes, cancelled.Token); Fail("Cancelled inference ran."); }
        catch (OperationCanceledException) { Console.WriteLine("PASS cancelled inference"); }
        using var inFlight = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        clock.Restart();
        try { await live.TranscribeAsync(bytes, inFlight.Token); Fail("Running inference ignored cancellation."); }
        catch (OperationCanceledException) { Console.WriteLine($"PASS native inference cancellation in {clock.ElapsedMilliseconds}ms"); }
        if (clock.Elapsed > TimeSpan.FromSeconds(5)) Fail("Native inference cancellation was too slow.");
        var humanPath = Path.Combine("artifacts", "research", "jfk.wav");
        if (File.Exists(humanPath))
        {
            clock.Restart();
            var human = await live.TranscribeAsync(await File.ReadAllBytesAsync(humanPath), timeout.Token);
            if (!human.Contains("what you can do for your country", StringComparison.OrdinalIgnoreCase)) Fail("Human reference speech mismatch: " + human);
            Console.WriteLine($"PASS human reference speech ({clock.ElapsedMilliseconds}ms): {human}");
        }
        var floats = new byte[48000 * 2 * 4];
        for (var i=0; i < floats.Length / 4; i++) BitConverter.GetBytes((float)(0.1*Math.Sin(i*0.04))).CopyTo(floats, i*4);
        var converted = NAudioMicrophoneRecorder.PcmToWav(floats, WaveFormat.CreateIeeeFloatWaveFormat(48000,2));
        using var convertedReader = new WaveFileReader(new MemoryStream(converted));
        if (convertedReader.WaveFormat.SampleRate != 16000 || convertedReader.WaveFormat.Channels != 1 || convertedReader.WaveFormat.BitsPerSample != 16 || convertedReader.Length == 0) Fail("Microphone resampling failed.");
        Console.WriteLine("PASS 48 kHz stereo float microphone converted to 16 kHz mono PCM");
        using var missing = new NAudioMicrophoneRecorder(() => "openclicky-missing-test-endpoint");
        try { missing.Start(); Fail("Missing microphone silently used another device."); }
        catch (Exception ex) when (ex is not InvalidOperationException || !ex.Message.StartsWith("Missing microphone silently")) { Console.WriteLine("PASS missing microphone fails visibly"); }
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}

static async Task<int> CheckOpenCodeAsync()
{
    var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bun", "bin", "opencode.exe");
    if (!File.Exists(executable)) { Console.Error.WriteLine("OpenCode is not installed at the tested location."); return 1; }
    var workspace = Path.Combine(Path.GetTempPath(), "OpenClicky-OpenCode-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspace);
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workspace };
    foreach (var argument in new[] { "serve", "--pure", "--hostname", "127.0.0.1", "--port", port.ToString() }) start.ArgumentList.Add(argument);
    start.Environment["OPENCODE_SERVER_PASSWORD"] = password;
    start.Environment["OPENCODE_CONFIG_CONTENT"] = "{\"permission\":{\"*\":\"deny\"}}";
    foreach (var name in new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME" }) start.Environment[name] = Path.Combine(workspace, name);
    using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated OpenCode.");
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var client = new OpenCodeTaskClient($"http://127.0.0.1:{port}", password: password);
        OpenCodeHealth? health = null;
        while (health is null)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (process.HasExited) throw new InvalidOperationException("Isolated OpenCode exited before health check.");
            try { health = await client.GetHealthAsync(timeout.Token); }
            catch (HttpRequestException) { await Task.Delay(300, timeout.Token); }
        }
        if (!health.Healthy) throw new InvalidOperationException("OpenCode reported unhealthy.");
        var session = await client.CreateSessionAsync("OpenClicky isolated protocol check", timeout.Token);
        var messages = await client.GetMessagesAsync(session.Id, timeout.Token);
        if (messages.Messages.Count != 0 || messages.Status.Type != "idle") throw new InvalidOperationException("Unexpected new-session state.");
        await client.AbortAsync(session.Id, timeout.Token);
        Console.WriteLine($"PASS live OpenCode {health.Version}: protected loopback health, create session, empty messages/status and abort. No model prompt, credentials or account access.");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    finally
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        // Keep the isolated receipt directory for inspection; never touch the user's OpenCode data.
        Console.WriteLine("Isolated OpenCode stopped; test workspace: " + workspace);
    }
}

sealed class VoiceFixture(byte[] bytes) : IMicrophoneRecorder
{
    public bool IsRecording { get; private set; }
    public void Start() => IsRecording = true;
    public byte[] SnapshotToWav() => IsRecording ? bytes : [];
    public byte[] StopToWav() { IsRecording = false; return bytes; }
    public void Dispose() => IsRecording = false;
}

sealed class LeaveOpenStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position=value; }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] b,int o,int c) => inner.Read(b,o,c);
    public override void Write(byte[] b,int o,int c) => inner.Write(b,o,c);
    public override long Seek(long o,SeekOrigin s) => inner.Seek(o,s);
    public override void SetLength(long n) => inner.SetLength(n);
    protected override void Dispose(bool disposing) { }
}
