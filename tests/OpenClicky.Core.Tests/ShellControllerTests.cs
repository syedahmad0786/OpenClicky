using System.Text.Json;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ShellControllerTests
{
    private const string Inside = @"C:\jobs\j1\output\report.md";

    private sealed class FakeHost : IShellHost
    {
        public List<string> Calls { get; } = [];
        public ShellInputs CollectInputs() => new(new AppSettings(),
            [new ShellJobInput(new WorkJob("j1", "openclicky", "Report", "p", DateTimeOffset.UtcNow, "completed", DateTimeOffset.UtcNow), [Inside], [])],
            [], [], ShellPhase.Idle, DateTimeOffset.UtcNow);
        public void ToggleTalk() => Calls.Add("talk");
        public void SubmitText(string text) => Calls.Add("submit:" + text);
        public void StopJob(string id) => Calls.Add("stop:" + id);
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
        public void Dispose() { }
        public void Send(object message) { var json = JsonSerializer.Serialize(message); Message?.Invoke(JsonDocument.Parse(json).RootElement.GetProperty("kind").GetString()!, json); }
    }

    private static async Task<(ShellController, FakeHost, FakeSurface)> Started()
    {
        var host = new FakeHost();
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
        surface.Send(new { kind = "text.submit", text = "" }); // invalid: dropped
        Assert.Equal(["submit:hello", "talk", "general:paused=True", "url:" + ShellController.BugUrl, "stop:j1"], host.Calls.Where(c => !c.StartsWith("trace:")));

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
