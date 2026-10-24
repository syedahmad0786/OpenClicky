using System.Text.Json;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Speech;

namespace OpenClicky.Core.Tests;

public class ShellControllerTests
{
    private const string Inside = @"C:\jobs\j1\output\report.md";

    private sealed class FakeHost : IShellHost
    {
        public List<string> Calls { get; } = [];
        public List<string> Files { get; } = [Inside];
        public bool ThrowOnCollect { get; set; }
        public ShellInputs CollectInputs() => ThrowOnCollect ? throw new InvalidOperationException("data source down") : new(new AppSettings(),
            [new ShellJobInput(new WorkJob("j1", "openclicky", "Report", "p", DateTimeOffset.UtcNow, "completed", DateTimeOffset.UtcNow), Files, [])],
            [], [], ShellPhase.Idle, DateTimeOffset.UtcNow);
        public void ToggleTalk() => Calls.Add("talk");
        public void SubmitText(string text) => Calls.Add("submit:" + text);
        public void StopJob(string id) => Calls.Add("stop:" + id);
        public void OpenJob(string id) => Calls.Add("job:" + id);
        public void SetGeneral(string key, bool value) => Calls.Add($"general:{key}={value}");
        public void OpenFile(string path) => Calls.Add("open:" + path);
        public void RevealFile(string path) => Calls.Add("reveal:" + path);
        public void OpenUrl(string url) => Calls.Add("url:" + url);
        public void CheckForUpdates() => Calls.Add("check");
        public void InstallUpdate() => Calls.Add("install");
        public void Quit() => Calls.Add("quit");
        public void ShowClassic() => Calls.Add("classic");
        public void Trace(string message) => Calls.Add("trace:" + message);
        public void SettingsSection(string section) => Calls.Add("section:" + section);
        public void PatchSetting(string key, JsonElement value) => Calls.Add($"patch:{key}={value.GetRawText()}");
        public void SettingsAction(string action, string? arg) => Calls.Add($"action:{action}:{arg}");
        public bool KeepCard { get; set; }
        public bool CardAction(string cardId, string actionId) { Calls.Add($"card:{cardId}:{actionId}"); return KeepCard; }
    }

    private sealed class FakeSurface : IShellSurface
    {
        public List<string> Posted { get; } = [];
        public event Action<string, string>? Message;
        public event Action? Failed;
        public void Fail() => Failed?.Invoke();
        public bool HasListeners => Message is not null || Failed is not null;
        public void Post(string json) => Posted.Add(json);
        public void SetNotch(ShellNotch notch) { }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public void Send(object message) { var json = JsonSerializer.Serialize(message); Message?.Invoke(JsonDocument.Parse(json).RootElement.GetProperty("kind").GetString()!, json); }
    }

    private static Task<(ShellController, FakeHost, FakeSurface)> Started(params string[] extraFiles) => Started(null, extraFiles);

    private static async Task<(ShellController, FakeHost, FakeSurface)> Started(Func<long>? clock, params string[] extraFiles)
    {
        var host = new FakeHost();
        host.Files.AddRange(extraFiles);
        var surface = new FakeSurface();
        var controller = new ShellController(host, () => Task.FromResult<IShellSurface>(surface), push => push(), clock);
        Assert.True(await controller.StartAsync());
        return (controller, host, surface);
    }

    [Fact]
    public async Task FileOpenOutsideStateIsRefused()
    {
        var (_, host, surface) = await Started();
        surface.Send(new { kind = "file.open", path = @"C:\Windows\System32\calc.exe" });
        surface.Send(new { kind = "file.reveal", path = @"C:\jobs\j1\output\..\..\secret.txt" });
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("open:") || c.StartsWith("reveal:"));
        Assert.Contains(host.Calls, c => c.StartsWith("trace:") && c.Contains("refused"));

        surface.Send(new { kind = "file.open", path = Inside });
        surface.Send(new { kind = "file.reveal", path = Inside });
        Assert.Contains("open:" + Inside, host.Calls);
        Assert.Contains("reveal:" + Inside, host.Calls);
    }

    [Fact]
    public async Task FallsBackToClassicWhenRuntimeFails()
    {
        var host = new FakeHost();
        var controller = new ShellController(host, () => throw new InvalidOperationException("runtime down"), push => push());
        Assert.False(await controller.StartAsync());
        Assert.False(controller.IsActive);
        Assert.Contains("classic", host.Calls);

        var asyncFailure = new ShellController(host, async () => { await Task.Yield(); throw new IOException("no webview"); }, push => push());
        Assert.False(await asyncFailure.StartAsync());
        Assert.Equal(2, host.Calls.Count(c => c == "classic"));
    }

    [Fact]
    public async Task QuittingWhileTheSurfaceStartsDisposesIt()
    {
        var host = new FakeHost();
        var surface = new FakeSurface();
        var pending = new TaskCompletionSource<IShellSurface>();
        var controller = new ShellController(host, () => pending.Task, push => push());
        var start = controller.StartAsync();
        controller.Dispose();
        pending.SetResult(surface);
        Assert.False(await start);
        Assert.True(surface.Disposed);
        Assert.Empty(surface.Posted);
        Assert.DoesNotContain("classic", host.Calls);
    }

    [Theory] // at most ShellStateBuilder.MaxFiles (6) files per job reach the state, Inside included
    [InlineData(@"C:\jobs\j1\output\setup.EXE", @"C:\jobs\j1\output\run.ps1", @"C:\jobs\j1\output\link.lnk", @"C:\jobs\j1\output\x.wsf", @"C:\jobs\j1\output\x.cpl")]
    [InlineData(@"C:\jobs\j1\output\x.js", @"C:\jobs\j1\output\x.appref-ms", @"C:\jobs\j1\output\noextension", @"C:\jobs\j1\output\notes.md.", @"C:\jobs\j1\output\x.settingcontent-ms")]
    public async Task NonAllowListedFilesAreRevealedNotOpened(params string[] risky)
    {
        var (_, host, surface) = await Started(risky);
        foreach (var path in risky) surface.Send(new { kind = "file.open", path });
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("open:"));
        Assert.Equal(risky.Select(p => "reveal:" + p), host.Calls.Where(c => c.StartsWith("reveal:")));
    }

    [Fact]
    public async Task AllowListedDocumentsAndMediaOpen()
    {
        string[] safe = [@"C:\jobs\j1\output\a.PDF", @"C:\jobs\j1\output\b.docx", @"C:\jobs\j1\output\c.Mp4", @"C:\jobs\j1\output\d.htm", @"C:\jobs\j1\output\e.csv"];
        var (_, host, surface) = await Started(safe);
        foreach (var path in safe) surface.Send(new { kind = "file.open", path });
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("reveal:"));
        Assert.Equal(safe.Select(p => "open:" + p), host.Calls.Where(c => c.StartsWith("open:")));
    }

    [Fact]
    public async Task FirstPushFailureFallsBackToClassic()
    {
        var host = new FakeHost { ThrowOnCollect = true };
        var surface = new FakeSurface();
        var controller = new ShellController(host, () => Task.FromResult<IShellSurface>(surface), push => push());
        Assert.False(await controller.StartAsync());
        Assert.False(controller.IsActive);
        Assert.True(surface.Disposed);
        Assert.False(surface.HasListeners);
        Assert.Contains("classic", host.Calls);
        Assert.Contains(host.Calls, c => c.StartsWith("trace:shell_push_error"));
    }

    [Fact]
    public async Task LaterPushFailureKeepsTheShell()
    {
        var (controller, host, surface) = await Started();
        host.ThrowOnCollect = true;
        controller.AddToast("x");
        Assert.True(controller.IsActive);
        Assert.DoesNotContain("classic", host.Calls);
        host.ThrowOnCollect = false;
        controller.AddToast("y");
        Assert.Contains("shell.state", surface.Posted[^1]);
    }

    [Fact]
    public async Task SurfaceFailureFallsBackToClassic()
    {
        var (controller, host, surface) = await Started();
        surface.Fail();
        Assert.False(controller.IsActive);
        Assert.True(surface.Disposed);
        Assert.False(surface.HasListeners);
        Assert.Equal(1, host.Calls.Count(c => c == "classic"));
        var posted = surface.Posted.Count;
        controller.AddToast("after");
        Assert.Equal(posted, surface.Posted.Count);
    }

    [Fact]
    public async Task LinkOpenAndUpdateCheckAreRateLimited()
    {
        long now = 10_000;
        var (_, host, surface) = await Started(() => now);
        surface.Send(new { kind = "link.open", target = "bug" });
        surface.Send(new { kind = "link.open", target = "whatsNew" });
        surface.Send(new { kind = "update.check" });
        surface.Send(new { kind = "update.check" });
        now += ShellController.IntentCooldownMs - 1;
        surface.Send(new { kind = "link.open", target = "bug" });
        now += 1;
        surface.Send(new { kind = "link.open", target = "whatsNew" });
        surface.Send(new { kind = "update.check" });
        Assert.Equal(["url:" + ShellController.BugUrl, "check", "url:" + ShellController.WhatsNewUrl, "check"], host.Calls.Where(c => !c.StartsWith("trace:")));
        Assert.Contains(host.Calls, c => c.Contains("rate-limited"));
    }

    [Fact]
    public async Task ReplyReportedAfterItsTurnWasCommittedIsNotRepeated()
    {
        var (controller, _, surface) = await Started();
        int Rows() { using var d = JsonDocument.Parse(surface.Posted[^1]); return d.RootElement.GetProperty("state").GetProperty("chat").GetProperty("rows").GetArrayLength(); }

        // ReportTaskResult order: CommitHistory (TurnCommitted) first, then ReportAssistantMessage (MessageReceived).
        controller.EndTurn("The report is ready.");
        controller.AddLiveRow(ChatRole.Assistant, "The report is ready.");
        Assert.Equal(0, Rows());

        controller.AddLiveRow(ChatRole.Assistant, "The report is ready."); // a genuinely new message is shown
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task ToastsAreNewestFirstAndDismissible()
    {
        var (controller, _, surface) = await Started();
        controller.AddToast("first");
        controller.AddToast("second");
        string[] Titles() { using var d = JsonDocument.Parse(surface.Posted[^1]); return d.RootElement.GetProperty("state").GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("title").GetString()!).ToArray(); }
        Assert.Equal(["second", "first"], Titles());
        using (var d = JsonDocument.Parse(surface.Posted[^1]))
        {
            var card = d.RootElement.GetProperty("state").GetProperty("cards")[0];
            Assert.Equal("toast", card.GetProperty("kind").GetString());
            Assert.Equal(6000, card.GetProperty("autoDismissMs").GetInt32());
            surface.Send(new { kind = "card.dismiss", cardId = card.GetProperty("id").GetString() });
        }
        Assert.Equal(["first"], Titles());
    }

    [Fact]
    public async Task PushesStateEnvelopeAndRoutesIntents()
    {
        var (controller, host, surface) = await Started();
        using (var first = JsonDocument.Parse(surface.Posted[^1]))
        {
            Assert.Equal("shell.state", first.RootElement.GetProperty("kind").GetString());
            Assert.Equal("j1", first.RootElement.GetProperty("state").GetProperty("featured").GetProperty("id").GetString());
        }

        surface.Send(new { kind = "text.submit", text = "hello" });
        surface.Send(new { kind = "talk.toggle" });
        surface.Send(new { kind = "general.set", key = "paused", value = true });
        surface.Send(new { kind = "link.open", target = "bug" });
        surface.Send(new { kind = "job.stop", id = "j1" });
        surface.Send(new { kind = "job.open", id = "j1" });
        surface.Send(new { kind = "text.submit", text = "" }); // invalid: dropped
        Assert.Equal(["submit:hello", "talk", "general:paused=True", "url:" + ShellController.BugUrl, "stop:j1", "job:j1"], host.Calls.Where(c => !c.StartsWith("trace:")));

        controller.OpenNotch();
        using var opened = JsonDocument.Parse(surface.Posted[^1]);
        Assert.True(opened.RootElement.GetProperty("state").GetProperty("notch").GetProperty("open").GetBoolean());
    }

    [Fact]
    public async Task StreamsAppendsAsDeltasAfterTheRowIsInState()
    {
        var (controller, _, surface) = await Started();
        controller.StreamAssistant("Hel", complete: false);
        using (var state = JsonDocument.Parse(surface.Posted[^1]))
            Assert.Equal("Hel", state.RootElement.GetProperty("state").GetProperty("chat").GetProperty("rows")[0].GetProperty("text").GetString());

        controller.StreamAssistant("Hello", complete: false);
        using (var delta = JsonDocument.Parse(surface.Posted[^1]))
        {
            Assert.Equal("chat.delta", delta.RootElement.GetProperty("kind").GetString());
            Assert.Equal("lo", delta.RootElement.GetProperty("text").GetString());
        }

        controller.StreamAssistant("Hello", complete: true);
        controller.AddLiveRow(ChatRole.Assistant, "Hello there");
        controller.EndTurn();
        using var final = JsonDocument.Parse(surface.Posted[^1]);
        Assert.Equal(0, final.RootElement.GetProperty("state").GetProperty("chat").GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task RoutesSettingsIntents()
    {
        var (controller, host, surface) = await Started();
        surface.Send(new { kind = "settings.section", section = "microphone" });
        surface.Send(new { kind = "settings.section", section = "billing" }); // not a section: dropped
        surface.Send(new { kind = "settings.patch", key = "voice.captions", value = false });
        surface.Send(new { kind = "settings.action", action = "secret.edit", arg = "openrouter" });
        surface.Send(new { kind = "settings.action", action = "testMic" });
        surface.Send(new { kind = "settings.action", action = "format.disk" }); // not allow-listed: dropped
        Assert.Equal(["section:microphone", "patch:voice.captions=false", "action:secret.edit:openrouter", "action:testMic:"],
            host.Calls.Where(c => !c.StartsWith("trace:")));
        Assert.Equal("microphone", controller.Notch.Section);
        using var state = JsonDocument.Parse(surface.Posted[^1]);
        Assert.Equal("microphone", state.RootElement.GetProperty("state").GetProperty("notch").GetProperty("section").GetString());
    }

    [Fact]
    public async Task OpeningTheSettingsTabTellsTheHostItsSection()
    {
        var (controller, host, surface) = await Started();
        surface.Send(new { kind = "notch.tab", tab = "settings" });
        controller.OpenNotch(NotchTab.Settings, "general");
        Assert.Equal(["section:general", "section:general"], host.Calls.Where(c => c.StartsWith("section:")));
        Assert.Equal(new ShellNotch(NotchTab.Settings, true, "general"), controller.Notch);
    }

    [Fact]
    public async Task TextModeOpensChatAndAsksThePageToFocusTheField()
    {
        var (controller, _, surface) = await Started();
        controller.OpenNotch(NotchTab.Chat);
        controller.PostComposerFocus();
        Assert.Equal(new ShellNotch(NotchTab.Chat, true, ShellContract.DefaultSection), controller.Notch);
        Assert.Equal(["""{"kind":"composer.focus"}"""], surface.Posted.Where(p => p.Contains("composer.focus")));
    }

    [Fact]
    public async Task PostsMicLevelsClamped()
    {
        var (controller, _, surface) = await Started();
        controller.PostMicLevel(0.25);
        controller.PostMicLevel(7);
        controller.PostMicLevel(double.NaN);
        Assert.Equal(["""{"kind":"mic.level","level":0.25}""", """{"kind":"mic.level","level":1}""", """{"kind":"mic.level","level":0}"""],
            surface.Posted.Where(p => p.Contains("mic.level")));
    }

    private static string[] CardIds(FakeSurface surface)
    {
        using var d = JsonDocument.Parse(surface.Posted.Last(p => p.Contains("shell.state")));
        return d.RootElement.GetProperty("state").GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToArray();
    }

    [Fact]
    public async Task CardsDedupeByIdNewestFirst()
    {
        var (controller, _, surface) = await Started();
        controller.AddCard(TalkCards.MutedSpeaker());
        controller.AddCard(TalkCards.MicTrouble());
        controller.AddCard(TalkCards.MutedSpeaker()); // replaced and moved to the front
        Assert.Equal([TalkCards.MutedSpeakerId, TalkCards.MicTroubleId], CardIds(surface));
        controller.RemoveCard(TalkCards.MicTroubleId);
        Assert.Equal([TalkCards.MutedSpeakerId], CardIds(surface));
    }

    [Fact]
    public async Task CardActionsReachTheHostOnlyWhenTheCardOffersThem()
    {
        var (controller, host, surface) = await Started();
        controller.AddCard(TalkCards.MutedSpeaker());
        controller.AddCard(TalkCards.MicTrouble());
        controller.AddToast("hello");
        var toast = CardIds(surface)[0];

        surface.Send(new { kind = "card.action", cardId = TalkCards.MutedSpeakerId, actionId = TalkCards.MicSettingsAction }); // not this card's action
        surface.Send(new { kind = "card.action", cardId = toast, actionId = "anything" }); // toasts carry no actions
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("card:"));
        Assert.Equal([TalkCards.MicTroubleId], CardIds(surface)); // both just dismissed

        host.KeepCard = true;
        surface.Send(new { kind = "card.action", cardId = TalkCards.MicTroubleId, actionId = TalkCards.MicSettingsAction });
        Assert.Equal([TalkCards.MicTroubleId], CardIds(surface)); // the host kept it
        host.KeepCard = false;
        surface.Send(new { kind = "card.action", cardId = TalkCards.MicTroubleId, actionId = TalkCards.MicSettingsAction });
        Assert.Equal(["card:mic-trouble:micSettings", "card:mic-trouble:micSettings"], host.Calls.Where(c => c.StartsWith("card:")));
        Assert.Empty(CardIds(surface));

        controller.AddCard(TalkCards.MutedSpeaker());
        surface.Send(new { kind = "card.dismiss", cardId = TalkCards.MutedSpeakerId });
        Assert.Empty(CardIds(surface));
        Assert.Equal(2, host.Calls.Count(c => c.StartsWith("card:"))); // dismiss never runs an action
    }

    [Fact]
    public async Task PostsCaptionWordsAndClear()
    {
        var (controller, _, surface) = await Started();
        controller.PostCaptionWord(" Hello ");
        controller.PostCaptionWord("   ");
        controller.PostCaptionWord(new string('a', 300));
        controller.ClearCaptions();
        Assert.Equal(["""{"kind":"caption.word","text":"Hello"}""", """{"kind":"caption.word","text":"%s"}""".Replace("%s", new string('a', 200)), """{"kind":"caption.clear"}"""],
            surface.Posted.Where(p => p.Contains("caption.")));
    }
}
