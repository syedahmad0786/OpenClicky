using System.Text.Json;
using System.Text.Json.Nodes;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class OnboardingContractTests
{
    private static JsonNode Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenClicky.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return JsonNode.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "onboarding.v1.json")))!;
    }

    private static string Wire(OnboardingStep step) => JsonNamingPolicy.CamelCase.ConvertName(step.ToString());

    private static ShellState BuildState() =>
        ShellStateBuilder.Build(new ShellInputs(new AppSettings(), [], [], [], ShellPhase.Idle, DateTimeOffset.UtcNow));

    [Fact]
    public void StepsAndActionsMatchSharedList()
    {
        var fixture = Fixture();
        Assert.Equal(fixture["steps"]!.AsArray().Select(n => n!.GetValue<string>()), Enum.GetValues<OnboardingStep>().Select(Wire));
        Assert.Equal(fixture["actions"]!.AsArray().Select(n => n!.GetValue<string>()), OnboardingContract.Actions);
    }

    [Theory]
    [InlineData("""{"kind":"onboarding","action":"next"}""", true)]
    [InlineData("""{"kind":"onboarding","action":"openScreenSettings"}""", true)]
    [InlineData("""{"kind":"onboarding","action":"nuke"}""", false)]
    [InlineData("""{"kind":"onboarding"}""", false)]
    [InlineData("""{"kind":"onboarding","action":1}""", false)]
    public void ParserAcceptsOnlyKnownActions(string json, bool valid) => Assert.Equal(valid, ShellIntentParser.TryParse(json, out _));

    [Fact]
    public void StateOmitsOnboardingWhenNull() =>
        Assert.DoesNotContain("\"onboarding\"", ShellContract.Serialize(BuildState()));

    [Fact]
    public void StateCarriesOnboarding() =>
        Assert.Contains("\"onboarding\":{\"step\":\"driveDemo\",\"index\":8,\"count\":12,\"stepDone\":true,\"busy\":false}",
            ShellContract.Serialize(BuildState() with { Onboarding = new OnboardingView(OnboardingStep.DriveDemo, 8, 12, true, false) }));

    private sealed class FakeHost : IShellHost
    {
        public List<string> Calls { get; } = [];
        public ShellInputs CollectInputs() => new(new AppSettings(), [], [], [], ShellPhase.Idle, DateTimeOffset.UtcNow);
        public void ToggleTalk() { }
        public void SubmitText(string text) { }
        public void StopJob(string id) { }
        public void OpenJob(string id) { }
        public void SetGeneral(string key, bool value) { }
        public void OpenFile(string path) { }
        public void RevealFile(string path) { }
        public void OpenUrl(string url) { }
        public void CheckForUpdates() { }
        public void InstallUpdate() { }
        public void Quit() { }
        public void ShowClassic() { }
        public void Trace(string message) { }
        public void SettingsSection(string section) { }
        public void PatchSetting(string key, JsonElement value) { }
        public void SettingsAction(string action, string? arg) { }
        public bool CardAction(string cardId, string actionId) => false;
        public void DrawingAction(string id, string action) { }
        public void OnboardingAction(string action) => Calls.Add("onboarding:" + action);
    }

    private sealed class FakeSurface : IShellSurface
    {
        public List<string> Posted { get; } = [];
        public event Action<string, string>? Message;
        public event Action? Failed { add { } remove { } }
        public void Post(string json) => Posted.Add(json);
        public void SetNotch(ShellNotch notch) { }
        public void Dispose() { }
        public void Send(string json) => Message?.Invoke("onboarding", json);
    }

    [Fact]
    public async Task ControllerRoutesOnboardingAndPushesView()
    {
        var host = new FakeHost();
        var surface = new FakeSurface();
        using var controller = new ShellController(host, () => Task.FromResult<IShellSurface>(surface), push => push());
        Assert.True(await controller.StartAsync());
        Assert.DoesNotContain("\"onboarding\"", surface.Posted[^1]);

        surface.Send("""{"kind":"onboarding","action":"skip"}""");
        surface.Send("""{"kind":"onboarding","action":"nuke"}""");
        Assert.Equal(["onboarding:skip"], host.Calls);

        controller.SetOnboarding(new OnboardingView(OnboardingStep.Screen, 3, 12, false, true));
        Assert.Contains("\"onboarding\":{\"step\":\"screen\",\"index\":3,\"count\":12,\"stepDone\":false,\"busy\":true}", surface.Posted[^1]);

        controller.SetOnboarding(null);
        Assert.DoesNotContain("\"onboarding\"", surface.Posted[^1]);
    }
}
