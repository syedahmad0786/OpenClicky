using System.Text.Json;
using System.Text.Json.Serialization;
using OpenClicky.Core.Browser;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ShellSettingsBuilderTests
{
    private static SettingsView Build(AppSettings? s = null, SettingsRuntime? r = null) =>
        ShellSettingsBuilder.Build(s ?? new AppSettings(), r ?? SettingsRuntime.Empty);

    private static string[] Keys(Hotkey h) => h.Keys.ToArray();

    [Fact]
    public void MultilingualStateComesFromTheProbe()
    {
        var v = Build(r: SettingsRuntime.Empty with { Multilingual = WhisperState.Downloading, MultilingualProgress = 0.4 });
        Assert.Equal(WhisperState.Downloading, v.Dictation.Multilingual);
        Assert.Equal(0.4, v.Dictation.MultilingualProgress);
    }

    [Fact]
    public void DefaultsMatchThePlan()
    {
        var v = Build();
        Assert.Equal(AccountProvider.Chatgpt, v.Account.Provider);
        Assert.Equal("windows-default", v.Voice.Voice);
        Assert.Equal(1, v.Voice.Speed);
        Assert.True(v.Voice.Captions);
        Assert.True(v.Voice.ReadReplies);
        Assert.True(v.Dictation.AutoDetect);
        Assert.Equal("en", v.Dictation.Language);
        Assert.Empty(v.Dictation.Dictionary);
        Assert.True(v.Dictation.Cleanup);
        Assert.Equal(WhisperState.Missing, v.Dictation.Multilingual);
        Assert.Equal(0, v.Dictation.MultilingualProgress);
        Assert.Equal(["Ctrl", "Alt"], Keys(v.Shortcuts.Talk));
        Assert.Equal(HotkeyTrigger.Hold, v.Shortcuts.Talk.Trigger);
        Assert.Equal(["Ctrl"], Keys(v.Shortcuts.Text));
        Assert.Equal(HotkeyTrigger.DoubleTap, v.Shortcuts.Text.Trigger);
        Assert.Equal(["Ctrl", "Win"], Keys(v.Shortcuts.Dictate));
        Assert.Equal(HotkeyTrigger.Hold, v.Shortcuts.Dictate.Trigger);
        Assert.Equal(["Ctrl", "Win"], Keys(v.Shortcuts.HandsFree));
        Assert.Equal(HotkeyTrigger.DoubleTap, v.Shortcuts.HandsFree.Trigger);
        Assert.Equal(new SettingsCursor("#3380FF", false, true, true), v.Cursor);
        Assert.True(v.Agents.SpeakAnnouncements);
        Assert.True(v.Agents.CursorUpdates);
        Assert.True(v.Agents.FolderIsDefault);
        Assert.Equal(AccessModeView.ConfirmEach, v.Access.Mode);
        Assert.Equal("http://127.0.0.1:8787", v.Advanced.ProxyUrl);
        Assert.Contains(v.Dictation.Languages, l => l.Code == "en");
    }

    [Fact]
    public void TalkShortcutShowsTalkHotkeyNotTheLegacyChord()
    {
        var s = new AppSettings
        {
            PushToTalk = new HotkeyChord(Win: false, Alt: false, Control: true, Shift: true, VirtualKey: 0x4B),
            TalkHotkey = new HotkeyBinding(["Ctrl", "Shift", "K"], HotkeyTrigger.Hold),
            TextHotkey = new HotkeyBinding(["F9"], HotkeyTrigger.DoubleTap),
        };
        var shown = Build(s).Shortcuts.Talk;
        Assert.Equal(["Ctrl", "Shift", "K"], Keys(shown));
        var hotkeys = ShellStateBuilder.Build(new ShellInputs(s, [], [], [], ShellPhase.Idle, DateTimeOffset.UtcNow)).Hotkeys;
        Assert.Equal(["F9"], hotkeys.Text);
        Assert.Equal(["Ctrl", "Win"], hotkeys.Dictate);
        Assert.Equal(["Ctrl", "Win"], hotkeys.HandsFree);
        Assert.Equal(HotkeyTrigger.Hold, shown.Trigger);
        Assert.Equal(ShellStateBuilder.Build(new ShellInputs(s, [], [], [], ShellPhase.Idle, DateTimeOffset.UtcNow)).Hotkeys.Talk, shown.Keys);
    }

    [Fact]
    public void SelectedDeviceAndVoiceSurviveTheListCaps()
    {
        var mics = Enumerable.Range(0, 60).Select(i => new Device("m" + i, "Mic " + i, DeviceState.Active)).ToList();
        var voices = Enumerable.Range(0, 120).Select(i => new InstalledVoice("v" + i, "Voice " + i)).ToList();
        var v = Build(new AppSettings { MicrophoneDeviceId = "m50", VoiceId = "v100" },
            new SettingsRuntime { CaptureDevices = mics, InstalledVoices = voices });
        Assert.Equal("m50", v.Microphone.DeviceId);
        Assert.Equal(32, v.Microphone.Devices.Count);
        Assert.Equal("m50", v.Microphone.Devices[0].Id);
        Assert.Equal("v100", v.Voice.Voice);
        Assert.Equal(64, v.Voice.Voices.Count);
        Assert.Contains(v.Voice.Voices, x => x.Id == "v100");

        // An unplugged saved device is listed first too.
        var gone = Build(new AppSettings { MicrophoneDeviceId = "m-gone" }, new SettingsRuntime { CaptureDevices = mics });
        Assert.Null(gone.Microphone.DeviceId);
        Assert.Equal(new Device("m-gone", "Unavailable microphone", DeviceState.Unavailable), gone.Microphone.Devices[0]);

        // An explicit over-cap page keeps its selection inside the cap as well.
        var page = SettingsView.Default with
        {
            Microphone = new SettingsMicrophone("m50", mics, WhisperState.Ready, 0),
            Voice = SettingsView.Default.Voice with { Voice = "v100", Voices = [.. voices.Select(x => new VoiceOption(x.Id, x.Name, "", true))] },
        };
        var clamped = ShellSettingsBuilder.Clamp(page);
        Assert.Equal(32, clamped.Microphone.Devices.Count);
        Assert.Contains(clamped.Microphone.Devices, d => d.Id == "m50");
        Assert.Equal(64, clamped.Voice.Voices.Count);
        Assert.Contains(clamped.Voice.Voices, x => x.Id == "v100");
    }

    [Fact]
    public void NonAccentCursorColourShowsTheDefault()
    {
        Assert.Equal("#3380FF", Build(new AppSettings { AccentColor = "#123456" }).Cursor.Colour);
        Assert.Equal("#FF4C4C", Build(new AppSettings { AccentColor = "#ff4c4c" }).Cursor.Colour);
    }

    [Fact]
    public void OldSettingsFileWithoutNewFieldsGetsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-old-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"speakReplies\": false, \"useLocalProxy\": true, \"agentAccess\": \"preview\", \"accentColor\": \"#FF4C4C\" }");
            var s = SettingsStore.Load(path);
            Assert.Equal("windows-default", s.VoiceId);
            Assert.Equal(1, s.SpeechSpeed);
            Assert.True(s.Captions);
            Assert.True(s.DictationAutoDetect);
            Assert.Empty(s.DictionaryWords);
            Assert.Equal(["Ctrl", "Alt"], s.TalkHotkey.Keys);
            Assert.True(s.ScreenHighlight && s.ShakeToDetach && s.SpeakJobAnnouncements && s.CursorJobUpdates);
            Assert.False(s.CursorHidden);
            Assert.Null(s.AgentFolder);
            var v = Build(s);
            Assert.False(v.Voice.ReadReplies);
            Assert.Equal(AccessModeView.Preview, v.Access.Mode);
            Assert.Equal("#FF4C4C", v.Cursor.Colour);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NewFieldsRoundTripThroughJson()
    {
        var s = new AppSettings
        {
            VoiceId = "alloy", SpeechSpeed = 0.75, Captions = false, DictionaryWords = ["Clicky"],
            TextHotkey = new HotkeyBinding(["F9"], HotkeyTrigger.DoubleTap), AgentFolder = @"C:\Work",
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, options), options)!;
        Assert.Equal("alloy", back.VoiceId);
        Assert.Equal(0.75, back.SpeechSpeed);
        Assert.False(back.Captions);
        Assert.Equal(["Clicky"], back.DictionaryWords);
        Assert.Equal(["F9"], back.TextHotkey.Keys);
        Assert.Equal(HotkeyTrigger.DoubleTap, back.TextHotkey.Trigger);
        Assert.Equal(@"C:\Work", back.AgentFolder);
    }

    [Fact]
    public void UnavailableDeviceIsFlagged()
    {
        // Saved microphone is not in the active list (unplugged): listed unavailable, selection falls back to System Default.
        var s = new AppSettings { MicrophoneDeviceId = "usb-mic", OutputDeviceId = "spk" };
        var runtime = new SettingsRuntime
        {
            CaptureDevices = [new Device("array-mic", "Array microphone", DeviceState.Active)],
            RenderDevices = [new Device("spk", "Speakers", DeviceState.Active)],
        };
        var v = Build(s, runtime);
        Assert.Null(v.Microphone.DeviceId);
        Assert.Contains(v.Microphone.Devices, d => d.Id == "usb-mic" && d.State == DeviceState.Unavailable);
        Assert.Contains(v.Microphone.Devices, d => d.Id == "array-mic" && d.State == DeviceState.Active);
        Assert.Equal("spk", v.Voice.OutputDeviceId);
        Assert.Equal("usb-mic", s.MicrophoneDeviceId);

        // Enumerated but unplugged: still listed once, still unavailable, still not selected.
        var unplugged = new SettingsRuntime { CaptureDevices = [new Device("usb-mic", "USB mic", DeviceState.Unavailable)] };
        v = Build(s, unplugged);
        Assert.Null(v.Microphone.DeviceId);
        Assert.Equal(DeviceState.Unavailable, Assert.Single(v.Microphone.Devices).State);

        // Plugged back in: selected again.
        v = Build(s, new SettingsRuntime { CaptureDevices = [new Device("usb-mic", "USB mic", DeviceState.Active)] });
        Assert.Equal("usb-mic", v.Microphone.DeviceId);
    }

    [Fact]
    public void VoicesListWindowsThenOpenAiNeedingAKey()
    {
        var runtime = new SettingsRuntime { InstalledVoices = [new InstalledVoice("win:david", "Microsoft David")] };
        var v = Build(new AppSettings { VoiceId = "alloy" }, runtime);
        Assert.Equal(["windows-default", "win:david"], v.Voice.Voices.Take(2).Select(x => x.Id));
        var alloy = Assert.Single(v.Voice.Voices, x => x.Id == "alloy");
        Assert.False(alloy.Available);
        Assert.Contains("OpenAI key", alloy.Detail);
        Assert.Equal(SettingsCatalog.OpenAiVoices.Count + 2, v.Voice.Voices.Count);

        v = Build(new AppSettings { VoiceId = "alloy" }, runtime with { OpenAiKeySet = true });
        Assert.True(Assert.Single(v.Voice.Voices, x => x.Id == "alloy").Available);

        // A voice that is no longer installed falls back to the default in the view, not in settings.
        var s = new AppSettings { VoiceId = "win:gone" };
        Assert.Equal("windows-default", Build(s, runtime).Voice.Voice);
        Assert.Equal("win:gone", s.VoiceId);
    }

    [Fact]
    public void SecretsNeverAppearInTheView()
    {
        var s = new AppSettings { OpenRouterApiKey = "sk-or-secret-value", ChatGptHostId = "urn:uuid:host" };
        var json = JsonSerializer.Serialize(Build(s, new SettingsRuntime { OpenRouterKeySet = true }), ShellContract.Options);
        Assert.DoesNotContain("sk-or-secret-value", json);
        Assert.Contains("\"openRouterKeySet\":true", json);
    }

    [Fact]
    public void ProxyUrlCredentialsAreStrippedFromTheView()
    {
        var s = new AppSettings { LocalProxyBaseUrl = "http://user:hunter2@127.0.0.1:8787/path" };
        var v = Build(s);
        Assert.Equal("http://127.0.0.1:8787/path", v.Advanced.ProxyUrl);
        Assert.DoesNotContain("hunter2", JsonSerializer.Serialize(v, ShellContract.Options));
        Assert.Equal("http://127.0.0.1:8787", Build(new AppSettings { LocalProxyBaseUrl = "http://evil.example.com" }).Advanced.ProxyUrl);
    }

    [Fact]
    public void AgentFolderFallsBackToTheDefault()
    {
        var runtime = new SettingsRuntime { DefaultAgentFolder = @"C:\Users\me\OpenClicky" };
        var v = Build(new AppSettings(), runtime);
        Assert.True(v.Agents.FolderIsDefault);
        Assert.Equal(@"C:\Users\me\OpenClicky", v.Agents.Folder);
        v = Build(new AppSettings { AgentFolder = @"D:\Agents" }, runtime);
        Assert.False(v.Agents.FolderIsDefault);
        Assert.Equal(@"D:\Agents", v.Agents.Folder);
    }

    [Fact]
    public void AccessModesMapToTheView()
    {
        Assert.Equal(AccessModeView.Full, Build(new AppSettings { AgentAccess = AgentAccessMode.FullAccess }).Access.Mode);
        Assert.Equal(AccessModeView.Auto, Build(new AppSettings { AgentAccess = AgentAccessMode.Auto }).Access.Mode);
        Assert.Equal(AccessModeView.Preview, Build(new AppSettings { AgentAccess = AgentAccessMode.Preview }).Access.Mode);
    }

    [Fact]
    public void OverCapViewIsClampedAndTheStateStillRoundTrips()
    {
        string Long(int n) => new('z', n);
        var devices = Enumerable.Range(0, 100).Select(i => new Device(i == 0 ? "" : "d" + i, Long(500), DeviceState.Active)).ToList();
        var huge = new SettingsView(
            new SettingsAccount(AccountProvider.Chatgpt, Long(500), Long(500), Long(500), [.. Enumerable.Repeat(Long(500), 300)], false, Long(500)),
            new SettingsVoice(Long(500), [.. Enumerable.Range(0, 300).Select(i => new VoiceOption("v" + i, Long(500), Long(500), true))], 99, true, true, Long(500), devices),
            new SettingsMicrophone(Long(500), devices, WhisperState.Downloading, 7),
            new SettingsDictation(true, Long(500), [.. Enumerable.Range(0, 500).Select(i => new DictationLanguage("c" + i, Long(500), Long(500)))],
                [.. Enumerable.Range(0, 500).Select(i => i + Long(100))], true, WhisperState.Downloading, 7),
            new SettingsShortcuts(
                new Hotkey([.. Enumerable.Repeat("Ctrl", 30)], HotkeyTrigger.Hold), new Hotkey([], HotkeyTrigger.Hold),
                new Hotkey([Long(100)], HotkeyTrigger.DoubleTap), SettingsView.Default.Shortcuts.HandsFree),
            new SettingsCursor("not-a-colour", false, true, true),
            new SettingsIntegrations(ChromeState.Waiting, [.. Enumerable.Range(0, 200).Select(i => new ConnectorView(i == 0 ? "" : "k" + i, Long(500), Long(500), ConnectorStatus.Connected, -3))]),
            new SettingsAgents(Long(9_000), false, true, true, [.. Enumerable.Range(0, 100).Select(i => new ReachItem("r" + i, Long(500), ReachState.Unknown))]),
            [.. Enumerable.Range(0, 500).Select(i => new RoutineView("r" + i, Long(500), Long(500), "not a date", "2026-10-24T10:00:00+02:00", RoutineStatus.Scheduled))],
            new SettingsAccess(AccessModeView.Auto, Long(50_000), true),
            new SettingsAdvanced(true, "http://user:pw@localhost:1/" + Long(5_000), false),
            new SettingsProactive(true, false, true, false, Long(500)));

        var clamped = ShellSettingsBuilder.Clamp(huge);
        Assert.Equal(64, clamped.Account.Models.Count);
        Assert.All(clamped.Account.Models, m => Assert.Equal(80, m.Length));
        Assert.Equal(64, clamped.Voice.Voices.Count);
        Assert.Equal(1.5, clamped.Voice.Speed);
        Assert.Equal(32, clamped.Voice.OutputDevices.Count);
        Assert.DoesNotContain(clamped.Microphone.Devices, d => d.Id.Length == 0);
        Assert.Equal(1, clamped.Microphone.WhisperProgress);
        Assert.Equal(128, clamped.Dictation.Languages.Count);
        Assert.Equal(50, clamped.Dictation.Dictionary.Count);
        Assert.All(clamped.Dictation.Dictionary, w => Assert.True(w.Length <= 60));
        Assert.Equal(1, clamped.Dictation.MultilingualProgress);
        Assert.Equal(6, clamped.Shortcuts.Talk.Keys.Count);
        Assert.Equal(["Ctrl"], Keys(clamped.Shortcuts.Text));
        Assert.Equal(24, clamped.Shortcuts.Dictate.Keys[0].Length);
        Assert.Equal("#3380FF", clamped.Cursor.Colour);
        Assert.Equal(64, clamped.Integrations.Connectors.Count);
        Assert.DoesNotContain(clamped.Integrations.Connectors, c => c.Key.Length == 0 || c.ToolCount < 0);
        Assert.Equal(16, clamped.Agents.Reach.Count);
        Assert.Equal(4_096, clamped.Agents.Folder.Length);
        Assert.Equal(100, clamped.Routines.Count);
        Assert.All(clamped.Routines, r => Assert.True(r.Next is null && r.Last!.EndsWith('Z')));
        Assert.Equal(2_000, clamped.Access.DenyList.Length);
        Assert.Equal("http://127.0.0.1:8787", clamped.Advanced.ProxyUrl); // over 200 chars: back to the default
        Assert.Equal(80, clamped.Proactive.CalendarConnector!.Length);

        // Through the whole shell state: still valid JSON that parses back to the same thing.
        var inputs = new ShellInputs(new AppSettings(), [], [], [], ShellPhase.Idle, DateTimeOffset.UtcNow) { SettingsPage = huge };
        var state = ShellStateBuilder.Build(inputs);
        var json = ShellContract.Serialize(state);
        Assert.Equal(json, ShellContract.Serialize(ShellContract.Deserialize(json)!));
        Assert.Equal(64, state.Settings.Voice.Voices.Count);
        Assert.True(json.Length < ShellStateBuilder.MaxStateChars);
    }

    [Fact]
    public void BrowserCardPassesThroughCapped()
    {
        string Long(int n) => new('z', n);
        var card = new BrowserCardView("edge", "Profile 2",
            [new BrowserOptionView("edge", "Edge", [new BrowserProfileView("Default", "Ahmad"), new BrowserProfileView("Profile 2", "Work")])],
            [new BrowserInstanceView("p1", "Work", true), new BrowserInstanceView("p2", "Home", false)], @"C:\ext");
        var view = Build(r: SettingsRuntime.Empty with { Browser = card });
        Assert.Equal(card.Browsers[0].Profiles, view.Integrations.Browser.Browsers[0].Profiles);
        Assert.Equal(("edge", "Profile 2", @"C:\ext"), (view.Integrations.Browser.BrowserId, view.Integrations.Browser.ProfileDir, view.Integrations.Browser.ExtensionFolder));
        Assert.Equal([true, false], view.Integrations.Browser.Instances.Select(i => i.Selected));
        Assert.Same(BrowserCardView.Empty, Build().Integrations.Browser);

        var huge = new BrowserCardView(Long(100), Long(500),
            [.. Enumerable.Range(0, 40).Select(i => new BrowserOptionView(i == 0 ? "" : "b" + i, Long(500),
                [.. Enumerable.Range(0, 100).Select(p => new BrowserProfileView(p == 0 ? "" : Long(500), Long(500)))]))],
            [.. Enumerable.Range(0, 20).Select(i => new BrowserInstanceView(i == 0 ? "" : Long(500), Long(500), i == 3))], Long(9_000));
        var clamped = Build(r: SettingsRuntime.Empty with { Browser = huge }).Integrations.Browser;
        Assert.Equal(32, clamped.BrowserId!.Length);
        Assert.Equal(260, clamped.ProfileDir!.Length);
        Assert.Equal(16, clamped.Browsers.Count);
        Assert.DoesNotContain(clamped.Browsers, b => b.Id.Length == 0 || b.Id.Length > 32 || b.Name.Length > 80);
        Assert.All(clamped.Browsers, b => { Assert.Equal(32, b.Profiles.Count); Assert.All(b.Profiles, p => Assert.True(p.Dir.Length is > 0 and <= 260 && p.Name.Length <= 160)); });
        Assert.Equal(8, clamped.Instances.Count);
        Assert.Contains(clamped.Instances, i => i.Selected); // the selected one survives the cap
        Assert.All(clamped.Instances, i => Assert.True(i.Id.Length is > 0 and <= 64 && i.Label.Length <= 160));
        Assert.Equal(4_096, clamped.ExtensionFolder.Length);
    }

    [Fact]
    public void ShellStateBuiltFromSettingsReflectsThem()
    {
        var settings = new AppSettings { SpeechSpeed = 0.5, CursorHidden = true };
        var state = ShellStateBuilder.Build(new ShellInputs(settings, [], [], [], ShellPhase.Idle, DateTimeOffset.UtcNow)
        {
            SettingsRuntime = new SettingsRuntime { Chrome = ChromeState.Connected },
        });
        Assert.Equal(0.5, state.Settings.Voice.Speed);
        Assert.True(state.Settings.Cursor.Hidden);
        Assert.Equal(ChromeState.Connected, state.Settings.Integrations.Chrome);
    }

    [Fact]
    public void ConnectorViewCarriesNoSecretFields()
    {
        var view = Build(r: SettingsRuntime.Empty with
        {
            Connectors = [new ConnectorView("notion", "Notion", "mcp.notion.com", ConnectorStatus.Connected, 12)
                { App = "notion", Label = new string('L', 80), Kind = ConnectorViewKind.Remote, Auth = ConnectorViewAuth.Browser, Since = "2026-10-20T09:00:00Z" }],
        });
        var json = JsonSerializer.SerializeToNode(view.Integrations, ShellContract.Options)!["connectors"]![0]!.AsObject();
        Assert.Equal(["key", "name", "host", "status", "toolCount", "app", "label", "kind", "auth", "since"], json.Select(p => p.Key));
        Assert.Equal(30, view.Integrations.Connectors[0].Label!.Length);
    }

    [Fact]
    public void CatalogDefaultsToTheBuiltInAppsAndIsClamped()
    {
        var def = Build().Integrations.Catalog;
        Assert.Equal(ConnectorCatalog.Apps.Select(a => a.Id), def.Select(c => c.Id));
        Assert.Equal(CatalogSetup.Folder, def.Single(c => c.Id == "obsidian").Setup);

        var huge = SettingsView.Default with
        {
            Integrations = SettingsView.Default.Integrations with
            {
                Catalog = [.. Enumerable.Range(0, 50).Select(i => new CatalogView("a" + i, new string('n', 200), new string('d', 500), CatalogSetup.SignIn, true))],
            },
        };
        var clamped = ShellSettingsBuilder.Clamp(huge).Integrations.Catalog;
        Assert.Equal(32, clamped.Count);
        Assert.All(clamped, c => Assert.True(c.Name.Length == 80 && c.Description.Length == 200));
    }

    [Fact]
    public void ProactiveMapsThrough()
    {
        var off = Build().Proactive;
        Assert.Equal(new SettingsProactive(false, false, false, false, null), off);
        var s = new AppSettings { Proactive = new() { MorningBrief = true, IntegrationNudges = true, CalendarConnector = "work_cal" } };
        Assert.Equal(new SettingsProactive(true, false, true, false, "work_cal"), Build(s).Proactive);
        Assert.Equal(off, Build(new AppSettings { Proactive = null! }).Proactive);
    }
}
