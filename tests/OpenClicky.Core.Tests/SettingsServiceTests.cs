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
        public List<string> Traces { get; } = [];
        public Action<AppSettings>? DuringSave { get; set; }
        public SettingsService Service { get; }

        public Rig() => Service = new SettingsService(Settings,
            s => { if (FailSave) throw new IOException("disk full"); DuringSave?.Invoke(Settings); Saved.Add(s); },
            mode => { Asked.Add(mode); return Accept; },
            Toasts.Add, () => Pushes++, Applied.Add, Traces.Add);
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
    public void ResetHotkeysRestoresAllFourDefaults()
    {
        var rig = new Rig();
        rig.Settings.TextHotkey = new HotkeyBinding(["Alt", "T"], HotkeyTrigger.Hold);
        rig.Settings.TalkHotkey = new HotkeyBinding(["Win", "Alt", "Space"], HotkeyTrigger.Hold);
        Assert.True(rig.Service.Change(SettingsActionArgs.ResetHotkeys));
        Assert.True(rig.Settings.TalkHotkey.SameChord(HotkeyBinding.DefaultTalk));
        Assert.True(rig.Settings.TextHotkey.SameChord(HotkeyBinding.DefaultText));
        Assert.True(rig.Settings.DictateHotkey.SameChord(HotkeyBinding.DefaultDictate));
        Assert.True(rig.Settings.HandsFreeHotkey.SameChord(HotkeyBinding.DefaultHandsFree));
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

    [Theory]
    [InlineData("openrouter", SecretNames.OpenRouterApiKey)]
    [InlineData("openai", SecretNames.OpenAiApiKey)]
    [InlineData("OpenAI", null)]
    [InlineData("chatgpt", null)]
    [InlineData(null, null)]
    public void SecretEditAcceptsOnlyKnownKeys(string? arg, string? secret) =>
        Assert.Equal(secret, SettingsActionArgs.SecretFor(arg)?.Name);

    [Fact]
    public void OpenAiKeyIsStoredUnderItsOwnName()
    {
        Assert.Equal("openai-api-key", SecretNames.OpenAiApiKey);
        Assert.Contains("OpenAI", SettingsActionArgs.SecretFor("openai")!.Value.Title);
    }

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
    [InlineData(null, "Basic dXNlcjpwdw==", "Authorization", "Basic dXNlcjpwdw==")]
    [InlineData(null, "Token abc", "Authorization", "Token abc")]
    [InlineData(null, "bearer abc", "Authorization", "bearer abc")]
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

        var resumed = SettingsActionArgs.Resume(paused with { ConsecutiveFailures = 3 }, Now, TimeZoneInfo.Utc);
        Assert.False(resumed.IsPaused);
        Assert.Equal(0, resumed.ConsecutiveFailures); // a routine paused after 3 failures gets three fresh tries
        // A stale next-run time is not replayed on resume: it moves to the next slot after now.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), resumed.NextRunAt);
        Assert.False(RoutineSchedule.IsDue(resumed, Now));
    }

    [Fact]
    public void RoutineViewsUseJobTitlesScheduleAndStatus()
    {
        var job = new WorkJob("j1", "openclicky", "Morning brief", "p", Now, WorkJobStates.Ready, "red", "");
        var running = job with { Id = "j2", Title = "Inbox", Status = WorkJobStates.Running };
        Routine[] routines =
        [
            Daily(),
            Daily(paused: true) with { Id = "r2", LastOutcome = "Paused after 3 failed runs" },
            Daily(failures: 2) with { Id = "r3" },
            Daily() with { Id = "r4", JobId = "j2" },
            Daily() with { Id = "r5", JobId = "gone" },
            Daily(failures: 1) with { Id = "r6" },
            Daily() with { Id = "r7", JobId = "j2" },
        ];
        var views = SettingsActionArgs.RoutineViews(routines, [job, running], waitingForNetwork: new HashSet<string> { "r6", "r7", "r2" });
        Assert.Equal(["r1", "r2", "r3", "r4", "r6", "r7"], views.Select(v => v.Id));
        Assert.Equal("Morning brief", views[0].Title);
        Assert.Equal("Every day at 09:00", views[0].Schedule);
        Assert.Equal(ShellStateBuilder.Iso(Now.AddDays(-3)), views[0].Next);
        Assert.Equal(ShellStateBuilder.Iso(Now.AddDays(-4)), views[0].Last);
        // Paused wins, then running, then waiting for the network, then failed.
        Assert.Equal([RoutineStatus.Scheduled, RoutineStatus.Paused, RoutineStatus.Failed, RoutineStatus.Running, RoutineStatus.Waiting, RoutineStatus.Running],
            views.Select(v => v.Status));
        Assert.Null(views[1].Next); // a paused routine has no next run
        Assert.Equal("Paused after 3 failed runs", views[1].Note);
        Assert.Equal("Waiting for the internet", views[4].Note);
        Assert.Null(views[0].Note);
        Assert.Null(views[2].Note);
        Assert.Null(views[3].Note);
        Assert.Equal(views.Select(v => v.Status).Take(4), SettingsActionArgs.RoutineViews(routines, [job, running]).Select(v => v.Status).Take(4));
    }

    [Theory]
    [InlineData("talk", "shortcuts.talk")]
    [InlineData("text", "shortcuts.text")]
    [InlineData("dictate", "shortcuts.dictate")]
    [InlineData("handsFree", "shortcuts.handsFree")]
    [InlineData("other", null)]
    [InlineData(null, null)]
    public void HotkeyRowsMapToPatchKeys(string? row, string? key) => Assert.Equal(key, SettingsActionArgs.HotkeyKey(row));

    [Fact]
    public void RecordedBindingBecomesAPatchThatApplies()
    {
        var s = new AppSettings();
        var value = SettingsActionArgs.HotkeyPatchValue(new HotkeyBinding(["Ctrl", "Shift"], HotkeyTrigger.DoubleTap));
        Assert.Equal("""{"keys":["Ctrl","Shift"],"trigger":"doubleTap"}""", value.GetRawText());
        Assert.Equal(PatchResult.Applied, SettingsPatch.TryApply(s, "shortcuts.handsFree", value, out _));
        Assert.True(s.HandsFreeHotkey.SameChord(new HotkeyBinding(["Ctrl", "Shift"], HotkeyTrigger.DoubleTap)));
        Assert.Equal(OpenClicky.Core.Input.ShortcutAction.HandsFree, SettingsActionArgs.HotkeyAction("handsFree"));
        Assert.Null(SettingsActionArgs.HotkeyAction("nope"));
    }

    [Fact]
    public void BindingDisplayNameIsNotPersisted()
    {
        Assert.Equal("Ctrl+Alt", HotkeyBinding.DefaultTalk.DisplayName);
        Assert.DoesNotContain("isplayName", JsonSerializer.Serialize(HotkeyBinding.DefaultTalk));
    }

    [Fact]
    public void LiveReapplyMismatchIsTraced()
    {
        var rig = new Rig();
        // The live settings change between the copy and the re-apply: Alt+T is taken by Dictate by then.
        rig.DuringSave = live => live.DictateHotkey = new HotkeyBinding(["Alt", "T"], HotkeyTrigger.Hold);
        Assert.Equal(PatchResult.Applied, rig.Service.Patch("shortcuts.text", Json(new { keys = new[] { "Alt", "T" }, trigger = "hold" })));
        Assert.Equal("settings_reapply_mismatch key=shortcuts.text result=Rejected", Assert.Single(rig.Traces));
    }

    [Theory]
    [InlineData("  sk-new  ", "sk-new")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void TypedSecretIsOnlyANewNonEmptyValue(string? typed, string? expected) =>
        Assert.Equal(expected, SettingsActionArgs.TypedSecret(typed));

    [Fact]
    public void UserMessagesDropTheParameterSuffix()
    {
        Assert.Equal("Give the connector a name.", SettingsActionArgs.UserMessage(new ArgumentException("Give the connector a name.", "displayName")));
        Assert.Equal("Up to 20.", SettingsActionArgs.UserMessage(new InvalidOperationException("Up to 20.")));
        Assert.False(SettingsActionArgs.TryParseConnector("""{"name":"X","url":"http://evil.example/mcp","auth":"none"}""", out _, out var error));
        Assert.DoesNotContain("(Parameter", error);
    }
}
