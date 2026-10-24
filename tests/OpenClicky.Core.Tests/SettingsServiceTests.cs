using System.Text.Json;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class SettingsServiceTests
{
    private sealed class Rig
    {
        public AppSettings Settings { get; } = new();
        public List<AppSettings> Saved { get; } = [];
        public List<string> Toasts { get; } = [];
        public List<AgentAccessMode> Asked { get; } = [];
        public List<string> Applied { get; } = [];
        public int Pushes { get; private set; }
        public bool Accept { get; set; }
        public bool FailSave { get; set; }
        public SettingsService Service { get; }

        public Rig() => Service = new SettingsService(Settings,
            s => { if (FailSave) throw new IOException("disk full"); Saved.Add(s); },
            mode => { Asked.Add(mode); return Accept; },
            Toasts.Add, () => Pushes++, Applied.Add);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void FullAccessRequiresConfirmation()
    {
        var rig = new Rig { Accept = false };
        Assert.Equal(PatchResult.RequiresConfirmation, rig.Service.Patch("access.mode", Json("full")));
        Assert.Equal([AgentAccessMode.FullAccess], rig.Asked);
        Assert.Equal(AgentAccessMode.ConfirmEach, rig.Settings.AgentAccess);
        Assert.Empty(rig.Saved);
        Assert.Equal(1, rig.Pushes); // re-pushed so the page shows the old mode again

        rig.Accept = true;
        Assert.Equal(PatchResult.Applied, rig.Service.Patch("access.mode", Json("full")));
        Assert.Equal(AgentAccessMode.FullAccess, rig.Settings.AgentAccess);
        Assert.Equal(AgentAccessMode.FullAccess, Assert.Single(rig.Saved).AgentAccess);
        Assert.Equal(["access.mode"], rig.Applied);
    }

    [Fact]
    public void SaferModesNeedNoConfirmation()
    {
        var rig = new Rig();
        Assert.Equal(PatchResult.Applied, rig.Service.Patch("access.mode", Json("preview")));
        Assert.Empty(rig.Asked);
        Assert.Equal(AgentAccessMode.Preview, rig.Settings.AgentAccess);
    }

    [Fact]
    public void PatchPersistsThenAppliesThenPushes()
    {
        var rig = new Rig();
        rig.Settings.OpenRouterApiKey = "sk-secret";
        Assert.Equal(PatchResult.Applied, rig.Service.Patch("voice.captions", Json(false)));
        var saved = Assert.Single(rig.Saved);
        Assert.False(saved.Captions);
        Assert.Null(saved.OpenRouterApiKey); // the saved copy never carries the secret
        Assert.False(rig.Settings.Captions);
        Assert.Equal("sk-secret", rig.Settings.OpenRouterApiKey); // the live settings keep it
        Assert.Equal(1, rig.Pushes);
        Assert.Empty(rig.Toasts);
    }

    [Fact]
    public void RejectedPatchToastsAndRepushesWithoutSaving()
    {
        var rig = new Rig();
        Assert.Equal(PatchResult.Rejected, rig.Service.Patch("voice.speed", Json(3)));
        Assert.Empty(rig.Saved);
        Assert.Equal(1, rig.Settings.SpeechSpeed);
        Assert.Contains("Speed", Assert.Single(rig.Toasts));
        Assert.Equal(1, rig.Pushes);
    }

    [Fact]
    public void ConflictingHotkeyToastsTheConflict()
    {
        var rig = new Rig();
        Assert.Equal(PatchResult.Rejected, rig.Service.Patch("shortcuts.text", Json(new { keys = new[] { "Ctrl", "Win" }, trigger = "hold" })));
        Assert.Contains("Dictate", Assert.Single(rig.Toasts));
    }

    [Fact]
    public void FailedSaveLeavesSettingsUnchanged()
    {
        var rig = new Rig { FailSave = true };
        Assert.Equal(PatchResult.Rejected, rig.Service.Patch("voice.captions", Json(false)));
        Assert.True(rig.Settings.Captions);
        Assert.Contains("disk full", Assert.Single(rig.Toasts));
        Assert.Empty(rig.Applied);
    }

    [Fact]
    public void ChangeSavesACopyThenAppliesAndPushes()
    {
        var rig = new Rig();
        Assert.True(rig.Service.Change(s => s.AgentFolder = @"D:\Agents"));
        Assert.Equal(@"D:\Agents", rig.Settings.AgentFolder);
        Assert.Equal(@"D:\Agents", Assert.Single(rig.Saved).AgentFolder);
        rig.FailSave = true;
        Assert.False(rig.Service.Change(s => s.AgentFolder = null));
        Assert.Equal(@"D:\Agents", rig.Settings.AgentFolder);
    }

    [Fact]
    public void ResetHotkeysRestoresDefaultsAndKeepsTalk()
    {
        var rig = new Rig();
        rig.Settings.TextHotkey = new HotkeyBinding(["Alt", "T"], HotkeyTrigger.Hold);
        var talk = rig.Settings.TalkHotkey;
        Assert.True(rig.Service.Change(SettingsActionArgs.ResetHotkeys));
        Assert.True(rig.Settings.TextHotkey.SameChord(HotkeyBinding.DefaultText));
        Assert.True(rig.Settings.DictateHotkey.SameChord(HotkeyBinding.DefaultDictate));
        Assert.True(rig.Settings.HandsFreeHotkey.SameChord(HotkeyBinding.DefaultHandsFree));
        Assert.Same(talk, rig.Settings.TalkHotkey);
    }

    [Theory]
    [InlineData("documents", "documentsLibrary", "ms-settings:privacy-documents")]
    [InlineData("pictures", "picturesLibrary", "ms-settings:privacy-pictures")]
    [InlineData("contacts", "contacts", "ms-settings:privacy-contacts")]
    [InlineData("calendar", "appointments", "ms-settings:privacy-calendar")]
    [InlineData("microphone", "microphone", "ms-settings:privacy-microphone")]
    [InlineData("camera", "webcam", "ms-settings:privacy-webcam")]
    [InlineData("location", "location", "ms-settings:privacy-location")]
    public void ReachIdsMapToConsentKeysAndSettingsPages(string id, string consentKey, string uri)
    {
        var target = Assert.Single(SettingsActionArgs.Reach, r => r.Id == id);
        Assert.Equal(consentKey, target.ConsentKey);
        Assert.Equal(uri, SettingsActionArgs.PrivacyUri(id));
    }

    [Fact]
    public void UnknownReachIdHasNoSettingsPage()
    {
        Assert.Null(SettingsActionArgs.PrivacyUri("ms-settings:windowsupdate"));
        Assert.Null(SettingsActionArgs.PrivacyUri(null));
        Assert.Equal(7, SettingsActionArgs.Reach.Count);
    }

    [Theory]
    [InlineData("Allow", ReachState.Allowed)]
    [InlineData("Deny", ReachState.Denied)]
    [InlineData("Prompt", ReachState.Unknown)]
    [InlineData(null, ReachState.Unknown)]
    public void ConsentValuesMapToReachStates(string? value, ReachState expected) =>
        Assert.Equal(expected, SettingsActionArgs.ConsentState(value));

    [Fact]
    public void VoiceShortIdsAreShortStableAndDistinct()
    {
        var token = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\" + new string('X', 300);
        var id = SettingsActionArgs.VoiceShortId(token);
        Assert.StartsWith("win:", id);
        Assert.Equal(20, id.Length);
        Assert.True(id.Length <= SettingsCatalog.MaxVoiceLength);
        Assert.Equal(id, SettingsActionArgs.VoiceShortId(token));
        Assert.NotEqual(id, SettingsActionArgs.VoiceShortId(token + "Y"));
        Assert.Matches("^win:[0-9a-f]{16}$", id);
    }

    [Fact]
    public void ConnectorArgParsesValidDrafts()
    {
        Assert.True(SettingsActionArgs.TryParseConnector("""{"name":" Linear ","url":"https://mcp.linear.app/sse","auth":"apiKey"}""", out var draft, out _));
        Assert.Equal(new ConnectorDraft("Linear", "https://mcp.linear.app/sse", ConnectorAuth.ApiKey), draft);
        Assert.True(SettingsActionArgs.TryParseConnector("""{"name":"Local","url":"http://127.0.0.1:3000/mcp","auth":"none"}""", out draft, out _));
        Assert.Equal(ConnectorAuth.None, draft!.Auth);
        Assert.True(SettingsActionArgs.TryParseConnector("""{"name":"Gmail","url":"https://x.example/mcp","auth":"browser"}""", out draft, out _));
        Assert.Equal(ConnectorAuth.Browser, draft!.Auth);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"name":"","url":"https://x.example","auth":"none"}""")]
    [InlineData("""{"name":"X","url":"http://evil.example/mcp","auth":"none"}""")]
    [InlineData("""{"name":"X","url":"https://user:pw@x.example/mcp","auth":"none"}""")]
    [InlineData("""{"name":"X","url":"https://x.example/mcp","auth":"oauth"}""")]
    [InlineData("""{"name":1,"url":"https://x.example/mcp","auth":"none"}""")]
    [InlineData("""{"url":"https://x.example/mcp","auth":"none"}""")]
    public void ConnectorArgRejectsBadDrafts(string? arg)
    {
        Assert.False(SettingsActionArgs.TryParseConnector(arg, out var draft, out var error));
        Assert.Null(draft);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ConnectorArgRejectsOversizeAndLongNames()
    {
        var longName = new string('n', 61);
        Assert.False(SettingsActionArgs.TryParseConnector($$"""{"name":"{{longName}}","url":"https://x.example/mcp","auth":"none"}""", out _, out _));
        var huge = $$"""{"name":"X","url":"https://x.example/{{new string('p', 600)}}","auth":"none"}""";
        Assert.False(SettingsActionArgs.TryParseConnector(huge, out _, out _));
    }

    [Theory]
    [InlineData(null, "abc", "Authorization", "Bearer abc")]
    [InlineData("", "Bearer abc", "Authorization", "Bearer abc")]
    [InlineData("authorization", "abc", "authorization", "Bearer abc")]
    [InlineData("X-Api-Key", "abc", "X-Api-Key", "abc")]
    public void ApiKeyHeadersDefaultToBearerAuthorization(string? name, string secret, string header, string value) =>
        Assert.Equal((header, value), SettingsActionArgs.ApiKeyHeader(name, secret));

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static Routine Daily(bool paused = false, int failures = 0) =>
        new("r1", "j1", RoutineCadence.Daily, 9 * 60, 0, 0, paused, Now.AddDays(-3), Now.AddDays(-4), "", failures);

    [Fact]
    public void PauseAndResumeRecomputeTheNextRun()
    {
        var paused = SettingsActionArgs.Pause(Daily());
        Assert.True(paused.IsPaused);
        Assert.False(RoutineSchedule.IsDue(paused, Now));

        var resumed = SettingsActionArgs.Resume(paused, Now, TimeZoneInfo.Utc);
        Assert.False(resumed.IsPaused);
        // A stale next-run time is not replayed on resume: it moves to the next slot after now.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), resumed.NextRunAt);
        Assert.False(RoutineSchedule.IsDue(resumed, Now));
    }

    [Fact]
    public void RoutineViewsUseJobTitlesScheduleAndStatus()
    {
        var job = new WorkJob("j1", "openclicky", "Morning brief", "p", Now, WorkJobStates.Ready);
        var running = job with { Id = "j2", Title = "Inbox", Status = WorkJobStates.Running };
        Routine[] routines =
        [
            Daily(),
            Daily(paused: true) with { Id = "r2" },
            Daily(failures: 2) with { Id = "r3" },
            Daily() with { Id = "r4", JobId = "j2" },
            Daily() with { Id = "r5", JobId = "gone" },
        ];
        var views = SettingsActionArgs.RoutineViews(routines, [job, running]);
        Assert.Equal(["r1", "r2", "r3", "r4"], views.Select(v => v.Id));
        Assert.Equal("Morning brief", views[0].Title);
        Assert.Equal("Every day at 09:00", views[0].Schedule);
        Assert.Equal(ShellStateBuilder.Iso(Now.AddDays(-3)), views[0].Next);
        Assert.Equal(ShellStateBuilder.Iso(Now.AddDays(-4)), views[0].Last);
        Assert.Equal([RoutineStatus.Scheduled, RoutineStatus.Paused, RoutineStatus.Failed, RoutineStatus.Running], views.Select(v => v.Status));
        Assert.Null(views[1].Next); // a paused routine has no next run
    }

    [Theory]
    [InlineData(new[] { 0xA2, 0x5B }, new[] { "Ctrl", "Win" })]
    [InlineData(new[] { 0x11, 0x12, 0x4B }, new[] { "Ctrl", "Alt", "K" })]
    [InlineData(new[] { 0xA0, 0x20 }, new[] { "Shift", "Space" })]
    [InlineData(new[] { 0x7B }, new[] { "F12" })]
    [InlineData(new[] { 0x35, 0xA5 }, new[] { "5", "Alt" })]
    public void VirtualKeysMapToShortcutTokens(int[] keys, string[] tokens) =>
        Assert.Equal(tokens, SettingsActionArgs.KeyTokens(keys));

    [Fact]
    public void UnsupportedVirtualKeysMapToNull() => Assert.Equal([null], SettingsActionArgs.KeyTokens([0x1B]));

    [Theory]
    [InlineData("talk", "shortcuts.talk")]
    [InlineData("text", "shortcuts.text")]
    [InlineData("dictate", "shortcuts.dictate")]
    [InlineData("handsFree", "shortcuts.handsFree")]
    [InlineData("other", null)]
    [InlineData(null, null)]
    public void HotkeyRowsMapToPatchKeys(string? row, string? key) => Assert.Equal(key, SettingsActionArgs.HotkeyKey(row));

    [Fact]
    public void RecordedChordKeepsTheRowTrigger()
    {
        var s = new AppSettings();
        var value = SettingsActionArgs.RecordedHotkey(s, "handsFree", [0xA2, 0xA0]);
        Assert.Equal("""{"keys":["Ctrl","Shift"],"trigger":"doubleTap"}""", value!.Value.GetRawText());
        Assert.Null(SettingsActionArgs.RecordedHotkey(s, "nope", [0xA2]));
    }
}
