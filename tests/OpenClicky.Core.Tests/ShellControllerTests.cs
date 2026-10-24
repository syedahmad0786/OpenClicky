using System.Text.Json;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ShellControllerTests
{
    private const string Inside = @"C:\jobs\j1\output\report.md";

    private sealed class FakeHost : IShellHost
    {
        public List<string> Calls { get; } = [];
        public List<string> Files { get; } = [Inside];
        public ShellInputs CollectInputs() => new(new AppSettings(),
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
    }

    private sealed class FakeSurface : IShellSurface
    {
        public List<string> Posted { get; } = [];
        public event Action<string, string>? Message;
        public void Post(string json) => Posted.Add(json);
        public void SetNotch(ShellNotch notch) { }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public void Send(object message) { var json = JsonSerializer.Serialize(message); Message?.Invoke(JsonDocument.Parse(json).RootElement.GetProperty("kind").GetString()!, json); }
    }

    private static async Task<(ShellController, FakeHost, FakeSurface)> Started(params string[] extraFiles)
    {
        var host = new FakeHost();
        host.Files.AddRange(extraFiles);
        var surface = new FakeSurface();
        var controller = new ShellController(host, () => Task.FromResult<IShellSurface>(surface), push => push());
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

    [Fact]
    public async Task ExecutableFilesAreRevealedNotOpened()
    {
        string[] risky = [@"C:\jobs\j1\output\setup.EXE", @"C:\jobs\j1\output\run.ps1", @"C:\jobs\j1\output\link.lnk"];
        var (_, host, surface) = await Started(risky);
        foreach (var path in risky) surface.Send(new { kind = "file.open", path });
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("open:"));
        Assert.Equal(risky.Select(p => "reveal:" + p), host.Calls.Where(c => c.StartsWith("reveal:")));
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
}
