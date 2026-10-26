using OpenClicky.Core.Browser;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

/// <summary>A Windows voice found on this machine.</summary>
public sealed record InstalledVoice(string Id, string Name);

/// <summary>
/// What the machine knows right now and AppSettings does not: devices, voices, models, sign-in, Whisper, connectors,
/// routines, privacy consent and Chrome. Filled by the host's probe; <see cref="Empty"/> is the nothing-known state.
/// Secrets never go in here, only whether they are saved.
/// </summary>
public sealed record SettingsRuntime
{
    public static SettingsRuntime Empty { get; } = new();

    public IReadOnlyList<Device> CaptureDevices { get; init; } = [];
    public IReadOnlyList<Device> RenderDevices { get; init; } = [];
    public IReadOnlyList<InstalledVoice> InstalledVoices { get; init; } = [];
    /// <summary>Email or name of the signed-in ChatGPT account; null when signed out.</summary>
    public string? SignedInAs { get; init; }
    public IReadOnlyList<string> Models { get; init; } = [];
    public bool OpenRouterKeySet { get; init; }
    public bool OpenAiKeySet { get; init; }
    public WhisperState Whisper { get; init; } = WhisperState.Missing;
    public double WhisperProgress { get; init; }
    public WhisperState Multilingual { get; init; } = WhisperState.Missing;
    public double MultilingualProgress { get; init; }
    public WhisperState Accurate { get; init; } = WhisperState.Missing;
    public double AccurateProgress { get; init; }
    public ChromeState Chrome { get; init; } = ChromeState.NotInstalled;
    /// <summary>Detected browsers, profiles and connected instances for the browser card.</summary>
    public BrowserCardView Browser { get; init; } = BrowserCardView.Empty;
    public IReadOnlyList<ConnectorView> Connectors { get; init; } = [];
    /// <summary>The built-in apps for the Integrations page; empty means <see cref="ConnectorCatalog.Apps"/>.</summary>
    public IReadOnlyList<CatalogView> Catalog { get; init; } = [];
    public IReadOnlyList<RoutineView> Routines { get; init; } = [];
    /// <summary>Windows privacy consent states (documents, pictures, contacts, calendar, microphone, camera, location).</summary>
    public IReadOnlyList<ReachItem> Reach { get; init; } = [];
    /// <summary>Where agents work when <see cref="AppSettings.AgentFolder"/> is empty.</summary>
    public string DefaultAgentFolder { get; init; } = "";
}

/// <summary>Maps AppSettings plus <see cref="SettingsRuntime"/> to the contract's <see cref="SettingsView"/>, and clamps any view to the contract caps.</summary>
public static class ShellSettingsBuilder
{
    public const int MaxModels = 64, MaxVoices = 64, MaxDevices = 32, MaxLanguages = 128, MaxConnectors = 64, MaxCatalog = 32, MaxReach = 16, MaxRoutines = 100, MaxHotkeyKeys = 6,
        MaxBrowsers = 16, MaxBrowserProfiles = 32, MaxBrowserInstances = 8;
    private const string UnavailableMicrophone = "Unavailable microphone";
    private const string UnavailableOutput = "Unavailable speaker";

    public static SettingsView Build(AppSettings s, SettingsRuntime runtime)
    {
        var account = new SettingsAccount(
            s.AuthProvider == AuthProvider.ChatGPT ? AccountProvider.Chatgpt : AccountProvider.Openrouter,
            runtime.SignedInAs, s.ChatGptModel ?? "", s.ChatGptQuickModel ?? "", runtime.Models,
            runtime.OpenRouterKeySet, s.OpenRouterModel ?? "");

        var proactive = s.Proactive ?? new Proactive.ProactiveSettings();
        var voices = Voices(runtime, s.VoiceId);
        var voice = voices.Any(v => v.Id == s.VoiceId) ? s.VoiceId : SettingsCatalog.WindowsDefaultVoice;
        var (outputId, outputs) = Devices(s.OutputDeviceId, runtime.RenderDevices, UnavailableOutput);
        var (micId, mics) = Devices(s.MicrophoneDeviceId, runtime.CaptureDevices, UnavailableMicrophone);

        var view = new SettingsView(
            account,
            new SettingsVoice(voice, voices, SettingsCatalog.SnapSpeed(s.SpeechSpeed), s.Captions, s.SpeakReplies, outputId, outputs),
            new SettingsMicrophone(micId, mics, runtime.Whisper, runtime.WhisperProgress)
            { Accurate = runtime.Accurate, AccurateProgress = runtime.AccurateProgress, UseAccurate = s.AccurateSpeech },
            new SettingsDictation(s.DictationAutoDetect, SettingsCatalog.Languages.Any(l => l.Code == s.DictationLanguage) ? s.DictationLanguage : "en", SettingsCatalog.Languages, s.DictionaryWords ?? [],
                s.DictationCleanup, runtime.Multilingual, runtime.MultilingualProgress),
            new SettingsShortcuts(
                HotkeyBinding.OrDefault(s.TalkHotkey, HotkeyBinding.DefaultTalk).ToView(),
                HotkeyBinding.OrDefault(s.TextHotkey, HotkeyBinding.DefaultText).ToView(),
                HotkeyBinding.OrDefault(s.DictateHotkey, HotkeyBinding.DefaultDictate).ToView(),
                HotkeyBinding.OrDefault(s.HandsFreeHotkey, HotkeyBinding.DefaultHandsFree).ToView()),
            new SettingsCursor(SettingsCatalog.SnapAccent(s.AccentColor), s.CursorHidden, s.ScreenHighlight, s.ShakeToDetach),
            new SettingsIntegrations(runtime.Chrome, runtime.Connectors)
            {
                Browser = runtime.Browser,
                Catalog = runtime.Catalog.Count > 0 ? runtime.Catalog
                    : [.. ConnectorCatalog.Apps.Select(a => new CatalogView(a.Id, a.Name, a.Description, a.Setup, a.MultiAccount))],
            },
            new SettingsAgents(
                string.IsNullOrWhiteSpace(s.AgentFolder) ? runtime.DefaultAgentFolder : s.AgentFolder,
                string.IsNullOrWhiteSpace(s.AgentFolder), s.SpeakJobAnnouncements, s.CursorJobUpdates, runtime.Reach),
            runtime.Routines,
            new SettingsAccess(AccessMode(s.AgentAccess), s.AgentDenyListExtra ?? "", s.PersistConversationHistory),
            new SettingsAdvanced(s.UseLocalProxy, s.LocalProxyBaseUrl, s.ForceGdiCapture),
            new SettingsProactive(proactive.MorningBrief, proactive.Suggestions, proactive.IntegrationNudges, proactive.MeetingCountdown, proactive.CalendarConnector));
        return Clamp(view);
    }

    public static AccessModeView AccessMode(AgentAccessMode mode) => mode switch
    {
        AgentAccessMode.Preview => AccessModeView.Preview,
        AgentAccessMode.Auto => AccessModeView.Auto,
        AgentAccessMode.FullAccess => AccessModeView.Full,
        _ => AccessModeView.ConfirmEach,
    };

    /// <summary>Windows default, installed voices, then the OpenAI voices (unavailable until an OpenAI key is saved).</summary>
    private static List<VoiceOption> Voices(SettingsRuntime runtime, string? selected)
    {
        var voices = new List<VoiceOption> { new(SettingsCatalog.WindowsDefaultVoice, "Windows default", "Installed voice", true) };
        voices.AddRange(Front(runtime.InstalledVoices, v => v.Id, selected)
            .Where(v => v.Id != SettingsCatalog.WindowsDefaultVoice && !SettingsCatalog.OpenAiVoices.Contains(v.Id))
            .Take(MaxVoices - 1 - SettingsCatalog.OpenAiVoices.Count)
            .Select(v => new VoiceOption(v.Id, v.Name, "Installed voice", true)));
        voices.AddRange(SettingsCatalog.OpenAiVoices.Select(name => new VoiceOption(
            name, char.ToUpperInvariant(name[0]) + name[1..], runtime.OpenAiKeySet ? Speech.VoiceCatalog.Descriptor(name) : "Needs your OpenAI key", runtime.OpenAiKeySet)));
        return voices;
    }

    /// <summary>
    /// The saved device is selected only while it is active. A saved device that is unplugged (listed unavailable) or
    /// missing is added to the list as unavailable and the selection falls back to System Default (null); the saved id
    /// is kept in settings so plugging it back in restores it.
    /// </summary>
    private static (string? Selected, List<Device> Devices) Devices(string? saved, IReadOnlyList<Device> found, string unavailableName)
    {
        var devices = found.ToList();
        if (string.IsNullOrWhiteSpace(saved)) return (null, devices);
        var match = devices.FirstOrDefault(d => d.Id == saved);
        if (match is null) match = new Device(saved, unavailableName, DeviceState.Unavailable);
        // The selected device goes first so the list cap can never push it out.
        return (match.State == DeviceState.Active ? saved : null, [match, .. devices.Where(d => d != match)]);
    }

    /// <summary>
    /// Brings any view inside the contract caps so one oversized or hand-edited field cannot invalidate the whole
    /// shell state: lists, strings, speed, hotkey length, dictionary, deny list and the proxy URL (credentials removed).
    /// </summary>
    public static SettingsView Clamp(SettingsView v)
    {
        var d = SettingsView.Default;
        var account = v.Account ?? d.Account;
        var voice = v.Voice ?? d.Voice;
        var mic = v.Microphone ?? d.Microphone;
        var dictation = v.Dictation ?? d.Dictation;
        var shortcuts = v.Shortcuts ?? d.Shortcuts;
        var cursor = v.Cursor ?? d.Cursor;
        var integrations = v.Integrations ?? d.Integrations;
        var agents = v.Agents ?? d.Agents;
        var access = v.Access ?? d.Access;
        var advanced = v.Advanced ?? d.Advanced;
        var proactive = v.Proactive ?? d.Proactive;
        var voices = WithId(voice.Voices, o => o.Id, MaxVoices, voice.Voice)
            .Select(o => new VoiceOption(Cut(o.Id, 160), Cut(o.Name, 80), Cut(o.Detail, 160), o.Available)).ToList();
        var languages = Take(dictation.Languages, MaxLanguages).Select(l => new DictationLanguage(Cut(l.Code, 16), Cut(l.Name, 80), Cut(l.Native, 80))).ToList();

        return new SettingsView(
            new SettingsAccount(account.Provider, CutOrNull(account.SignedInAs, 160), Cut(account.Model, 80), Cut(account.QuickModel, 80),
                Take(account.Models, MaxModels).Select(m => Cut(m, 80)).ToList(), account.OpenRouterKeySet, Cut(account.OpenRouterModel, 120)),
            new SettingsVoice(Cut(voice.Voice, 80), voices, Math.Clamp(double.IsFinite(voice.Speed) ? voice.Speed : 1, 0.5, 1.5),
                voice.Captions, voice.ReadReplies, CutOrNull(voice.OutputDeviceId, 160), Devs(voice.OutputDevices, voice.OutputDeviceId)),
            new SettingsMicrophone(CutOrNull(mic.DeviceId, 160), Devs(mic.Devices, mic.DeviceId), mic.Whisper,
                Math.Clamp(double.IsFinite(mic.WhisperProgress) ? mic.WhisperProgress : 0, 0, 1))
            {
                Accurate = mic.Accurate, UseAccurate = mic.UseAccurate,
                AccurateProgress = Math.Clamp(double.IsFinite(mic.AccurateProgress) ? mic.AccurateProgress : 0, 0, 1),
            },
            new SettingsDictation(dictation.AutoDetect, Cut(dictation.Language, 16), languages,
                SettingsCatalog.CleanDictionary(dictation.Dictionary), dictation.Cleanup, dictation.Multilingual,
                Math.Clamp(double.IsFinite(dictation.MultilingualProgress) ? dictation.MultilingualProgress : 0, 0, 1)),
            new SettingsShortcuts(
                Key(shortcuts.Talk, HotkeyBinding.DefaultTalk), Key(shortcuts.Text, HotkeyBinding.DefaultText),
                Key(shortcuts.Dictate, HotkeyBinding.DefaultDictate), Key(shortcuts.HandsFree, HotkeyBinding.DefaultHandsFree)),
            new SettingsCursor(ShellStateBuilder.IsHex(cursor.Colour) ? cursor.Colour!.ToUpperInvariant() : ShellStateBuilder.DefaultAccent,
                cursor.Hidden, cursor.ScreenHighlight, cursor.ShakeToDetach),
            new SettingsIntegrations(integrations.Chrome, WithId(integrations.Connectors, c => c.Key, MaxConnectors)
                .Select(c => new ConnectorView(Cut(c.Key, 160), Cut(c.Name, 80), Cut(c.Host, 255), c.Status, c.ToolCount is < 0 ? null : c.ToolCount)
                {
                    App = CutOrNull(c.App, 160), Label = CutOrNull(c.Label, 30), Kind = c.Kind, Auth = c.Auth, Since = Iso(c.Since),
                }).ToList())
            {
                Browser = ClampBrowser(integrations.Browser),
                Catalog = Take(integrations.Catalog, MaxCatalog)
                    .Select(a => new CatalogView(Cut(a.Id, 160), Cut(a.Name, 80), Cut(a.Description, 200), a.Setup, a.MultiAccount)).ToList(),
            },
            new SettingsAgents(Cut(agents.Folder, 4_096), agents.FolderIsDefault, agents.SpeakAnnouncements, agents.CursorUpdates,
                WithId(agents.Reach, r => r.Id, MaxReach)
                    .Select(r => new ReachItem(Cut(r.Id, 160), Cut(r.Label, 160), r.State)).ToList()),
            WithId(v.Routines, r => r.Id, MaxRoutines).Select(r => new RoutineView(Cut(r.Id, 160), Cut(r.Title, 160),
                Cut(r.Schedule, 160), Iso(r.Next), Iso(r.Last), r.Status, CutOrNull(r.Note, 200))).ToList(),
            new SettingsAccess(access.Mode, SettingsCatalog.CleanDenyList(access.DenyList), access.RememberHistory),
            new SettingsAdvanced(advanced.UseLocalProxy, SettingsCatalog.LoopbackUrl(advanced.ProxyUrl, stripUserInfo: true) ?? SettingsCatalog.DefaultProxyUrl,
                advanced.ForceGdiCapture),
            new SettingsProactive(proactive.MorningBrief, proactive.Suggestions, proactive.IntegrationNudges, proactive.MeetingCountdown,
                CutOrNull(proactive.CalendarConnector, 80)));

        static List<Device> Devs(IReadOnlyList<Device>? devices, string? selected) =>
            WithId(devices, x => x.Id, MaxDevices, selected).Select(x => new Device(Cut(x.Id, 160), Cut(x.Name, 160), x.State)).ToList();

        static Hotkey Key(Hotkey? hotkey, HotkeyBinding fallback)
        {
            var keys = (hotkey?.Keys ?? []).Take(MaxHotkeyKeys).Select(k => Cut(k ?? "", 24)).Where(k => k.Length > 0).ToList();
            return keys.Count == 0 ? fallback.ToView() : new Hotkey(keys, hotkey!.Trigger);
        }
    }

    /// <summary>Browser card caps: 16 browsers, 32 profiles each, 8 instances (the selected one never dropped); ids must be non-empty.</summary>
    private static BrowserCardView ClampBrowser(BrowserCardView? card)
    {
        if (card is null || ReferenceEquals(card, BrowserCardView.Empty)) return BrowserCardView.Empty;
        var instances = (card.Instances ?? []).Where(i => !string.IsNullOrEmpty(i.Id)).ToList();
        if (instances.Count > MaxBrowserInstances) instances = [.. instances.OrderByDescending(i => i.Selected).Take(MaxBrowserInstances)];
        return new BrowserCardView(
            CutOrNull(card.BrowserId, 32), CutOrNull(card.ProfileDir, 260),
            WithId(card.Browsers, b => b.Id, MaxBrowsers).Select(b => new BrowserOptionView(Cut(b.Id, 32), Cut(b.Name, 80),
                WithId(b.Profiles, p => p.Dir, MaxBrowserProfiles).Select(p => new BrowserProfileView(Cut(p.Dir, 260), Cut(p.Name, 160))).ToList())).ToList(),
            instances.Select(i => new BrowserInstanceView(Cut(i.Id, 64), Cut(i.Label, 160), i.Selected)).ToList(),
            Cut(card.ExtensionFolder, 4_096));
    }

    /// <summary>The entry whose id is <paramref name="selected"/> first (if present), then the rest in order, so a cap never drops the selection.</summary>
    private static IReadOnlyList<T> Front<T>(IReadOnlyList<T>? list, Func<T, string?> id, string? selected)
    {
        var items = list ?? [];
        var index = selected is null ? -1 : items.ToList().FindIndex(x => id(x) == selected);
        return index <= 0 ? items : [items[index], .. items.Where((_, i) => i != index)];
    }

    /// <summary>
    /// Entries with a non-empty id (the page schema requires one), at most <paramref name="max"/> of them in order;
    /// if the <paramref name="selected"/> entry would fall outside the cap it takes the last slot instead.
    /// </summary>
    private static List<T> WithId<T>(IReadOnlyList<T>? list, Func<T, string?> id, int max, string? selected = null)
    {
        var valid = (list ?? []).Where(x => !string.IsNullOrEmpty(id(x))).ToList();
        if (valid.Count <= max) return valid;
        var kept = valid.Take(max).ToList();
        if (selected is not null && !kept.Any(x => id(x) == selected) && valid.FirstOrDefault(x => id(x) == selected) is { } pick)
            kept[^1] = pick;
        return kept;
    }
    private static IEnumerable<T> Take<T>(IReadOnlyList<T>? list, int max) => (list ?? []).Take(max);
    private static string Cut(string? value, int max) => SettingsCatalog.Cut(value ?? "", max);
    /// <summary>Routine times must be ISO-8601 with an offset or the page schema rejects the whole state.</summary>
    private static string? Iso(string? value) => value is not null && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? ShellStateBuilder.Iso(at) : null;
    private static string? CutOrNull(string? value, int max) => value is null ? null : SettingsCatalog.Cut(value, max);
}
