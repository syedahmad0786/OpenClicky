using System.Text.Json.Nodes;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ShellContractTests
{
    private static string FixtureJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenClicky.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "shell-state.v1.json"));
    }

    [Fact]
    public void FixtureRoundTrips()
    {
        var fixture = FixtureJson();
        var state = ShellContract.Deserialize(fixture);
        Assert.NotNull(state);
        Assert.Equal(12, state!.Cards.Select(c => c.Kind).Distinct().Count());
        Assert.Equal(7, state.Jobs.Select(j => j.State).Distinct().Count());

        var again = ShellContract.Serialize(state);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(fixture), JsonNode.Parse(again)), again);
    }

    [Fact]
    public void FixtureWithDrawingRowParses()
    {
        var state = ShellContract.Deserialize(FixtureJson())!;
        var row = Assert.Single(state.Chat.Rows, r => r.DrawingId is not null);
        Assert.Equal(ChatRole.Event, row.Role);
        Assert.Equal("0f1e2d3c4b5a69788796a5b4c3d2e1f0", row.DrawingId);
        Assert.Null(row.JobId);
        Assert.Contains("\"drawingId\":\"0f1e2d3c4b5a69788796a5b4c3d2e1f0\"", ShellContract.Serialize(state));
    }

    [Theory]
    [InlineData("""{"kind":"drawing.action","id":"d1","action":"replayWindow"}""", true)]
    [InlineData("""{"kind":"drawing.action","id":"d1","action":"replayFullScreen"}""", true)]
    [InlineData("""{"kind":"drawing.action","id":"d1","action":"replayBoard"}""", true)]
    [InlineData("""{"kind":"drawing.action","id":"d1","action":"print"}""", true)]
    [InlineData("""{"kind":"drawing.action","id":"d1","action":"remove"}""", true)]
    [InlineData("""{"kind":"drawing.action","id":"d1","action":"delete"}""", false)]
    [InlineData("""{"kind":"drawing.action","id":"d1"}""", false)]
    [InlineData("""{"kind":"drawing.action","action":"print"}""", false)]
    [InlineData("""{"kind":"drawing.action","id":"","action":"print"}""", false)]
    [InlineData("""{"kind":"drawing.action","id":7,"action":"print"}""", false)]
    public void DrawingActionIntentIsAllowListed(string json, bool valid) => Assert.Equal(valid, ShellIntentParser.TryParse(json, out _));

    [Fact]
    public void DrawingActionIdIsBounded()
    {
        Assert.True(ShellIntentParser.TryParse("{\"kind\":\"drawing.action\",\"id\":\"" + new string('a', 160) + "\",\"action\":\"print\"}", out _));
        Assert.False(ShellIntentParser.TryParse("{\"kind\":\"drawing.action\",\"id\":\"" + new string('a', 161) + "\",\"action\":\"print\"}", out _));
    }

    [Fact]
    public void NullsAndOptionalsSerializeAsContractExpects()
    {
        var state = ShellContract.Deserialize(FixtureJson())! with { Featured = null, Document = null };
        var node = JsonNode.Parse(ShellContract.Serialize(state))!;
        Assert.Null(node["featured"]);
        Assert.True(node.AsObject().ContainsKey("featured"));
        Assert.True(node.AsObject().ContainsKey("document"));
        Assert.False(node["jobs"]![0]!.AsObject().ContainsKey("summary"));
    }

    [Theory]
    [InlineData("""{"kind":"bootstrap.request"}""")]
    [InlineData("""{"kind":"ready"}""")]
    [InlineData("""{"kind":"surface.regions","rects":[{"x":0,"y":0,"w":10.5,"h":8,"r":10}]}""")]
    [InlineData("""{"kind":"surface.regions","rects":[]}""")]
    [InlineData("""{"kind":"surface.focus","wanted":true}""")]
    [InlineData("""{"kind":"notch.tab","tab":"chat"}""")]
    [InlineData("""{"kind":"notch.open","open":false}""")]
    [InlineData("""{"kind":"talk.toggle"}""")]
    [InlineData("""{"kind":"text.submit","text":"hello"}""")]
    [InlineData("""{"kind":"general.set","key":"paused","value":true}""")]
    [InlineData("""{"kind":"job.open","id":"j1"}""")]
    [InlineData("""{"kind":"job.stop","id":"j1"}""")]
    [InlineData("""{"kind":"job.dismiss","id":"j1"}""")]
    [InlineData("""{"kind":"file.open","path":"C:/a.txt"}""")]
    [InlineData("""{"kind":"file.reveal","path":"C:/a.txt"}""")]
    [InlineData("""{"kind":"card.action","cardId":"c","actionId":"a"}""")]
    [InlineData("""{"kind":"card.dismiss","cardId":"c"}""")]
    [InlineData("""{"kind":"update.check"}""")]
    [InlineData("""{"kind":"update.install"}""")]
    [InlineData("""{"kind":"link.open","target":"bug"}""")]
    [InlineData("""{"kind":"app.quit"}""")]
    [InlineData("""{"kind":"settings.section","section":"voice"}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed","value":1.25}""")]
    [InlineData("""{"kind":"settings.patch","key":"cursor.screenHighlight","value":false}""")]
    [InlineData("""{"kind":"settings.patch","key":"dictation.dictionary","value":["a","b"]}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.outputDeviceId","value":null}""")]
    [InlineData("""{"kind":"settings.patch","key":"shortcuts.talk","value":{"keys":["Ctrl","Alt"],"trigger":"hold"}}""")]
    [InlineData("""{"kind":"settings.action","action":"signIn"}""")]
    [InlineData("""{"kind":"settings.action","action":"secret.edit","arg":"openRouter"}""")]
    [InlineData("""{"kind":"settings.action","action":"routine.pause","arg":"r1"}""")]
    public void ValidIntentsParse(string json)
    {
        Assert.True(ShellIntentParser.TryParse(json, out var intent));
        Assert.Equal(System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("kind").GetString(), intent.Kind);
    }

    [Fact]
    public void IntentExposesTypedFields()
    {
        Assert.True(ShellIntentParser.TryParse("""{"kind":"general.set","key":"launchAtLogin","value":true}""", out var intent));
        Assert.Equal("launchAtLogin", intent.GetString("key"));
        Assert.True(intent.GetBool("value"));
        Assert.Null(intent.GetString("missing"));
        Assert.Null(intent.GetBool("key"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"kind":42}""")]
    [InlineData("""{}""")]
    [InlineData("""{"kind":"shell.explode"}""")]
    [InlineData("""{"kind":"shell.state"}""")]
    [InlineData("""{"kind":"text.submit"}""")]
    [InlineData("""{"kind":"text.submit","text":5}""")]
    [InlineData("""{"kind":"surface.regions","rects":[{"x":0,"y":0,"w":1,"h":1}]}""")]
    [InlineData("""{"kind":"surface.regions","rects":[{"x":"0","y":0,"w":1,"h":1,"r":1}]}""")]
    [InlineData("""{"kind":"surface.regions","rects":"x"}""")]
    [InlineData("""{"kind":"surface.focus","wanted":"yes"}""")]
    [InlineData("""{"kind":"notch.tab","tab":"nope"}""")]
    [InlineData("""{"kind":"notch.open"}""")]
    [InlineData("""{"kind":"general.set","key":"other","value":true}""")]
    [InlineData("""{"kind":"general.set","key":"paused","value":"true"}""")]
    [InlineData("""{"kind":"link.open","target":"https://evil.example"}""")]
    [InlineData("""{"kind":"job.stop"}""")]
    [InlineData("""{"kind":"file.open"}""")]
    [InlineData("""{"kind":"card.action","cardId":"c"}""")]
    [InlineData("""{"kind":"settings.section"}""")]
    [InlineData("""{"kind":"settings.section","section":"billing"}""")]
    [InlineData("""{"kind":"settings.section","section":5}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed"}""")]
    [InlineData("""{"kind":"settings.patch","value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":"speed","value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":"Voice.speed","value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed.","value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice..speed","value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed\n","value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.sp eed","value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":7,"value":1}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed","value":"\ud800"}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed","value":[[[[[1]]]]]}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed","value":{"a\ud800":1}}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed","value":["\ud800"]}""")]
    [InlineData("""{"kind":"settings.patch","key":"voice.speed","value":{"a":["ok",{"b":"\udc00"}]}}""")]
    [InlineData("""{"kind":"settings.action"}""")]
    [InlineData("""{"kind":"settings.action","action":"formatDisk"}""")]
    [InlineData("""{"kind":"settings.action","action":"signIn","arg":5}""")]
    [InlineData("""{"kind":"settings.action","action":"signIn","arg":"\ud800"}""")]
    [InlineData("""{"kind":"mic.level","level":0.5}""")]
    public void InvalidPayloadsAreRejected(string json) => Assert.False(ShellIntentParser.TryParse(json, out _));

    [Fact]
    public void UnknownOrOversizedKindIsRejected()
    {
        Assert.False(ShellIntentParser.TryParse("""{"kind":"window.open"}""", out _));
        Assert.False(ShellIntentParser.TryParse("{\"kind\":\"text.submit\",\"text\":\"" + new string('a', 128_000) + "\"}", out _));
        Assert.False(ShellIntentParser.TryParse("{\"kind\":\"ready\",\"pad\":\"" + new string('a', 128_000) + "\"}", out _));
        Assert.False(ShellIntentParser.TryParse("""{"kind":"text.submit","text":"%s"}""".Replace("%s", new string('a', 8_001)), out _));
        Assert.True(ShellIntentParser.TryParse("""{"kind":"text.submit","text":"%s"}""".Replace("%s", new string('a', 8_000)), out _));
    }

    [Fact]
    public void LoneSurrogatesAreRejectedNotThrown()
    {
        // JSON-escaped lone surrogate in a payload string, in kind, in an enum field, and a raw lone surrogate.
        Assert.False(ShellIntentParser.TryParse("""{"kind":"text.submit","text":"\ud800"}""", out _));
        Assert.False(ShellIntentParser.TryParse("""{"kind":"\ud800"}""", out _));
        Assert.False(ShellIntentParser.TryParse("""{"kind":"notch.tab","tab":"\ud800"}""", out _));
        Assert.False(ShellIntentParser.TryParse("{\"kind\":\"text.submit\",\"text\":\"\ud800\"}", out _));
        Assert.False(ShellIntentParser.TryParse("{\"kind\":\"ready\",\"x\":\"\udc00\"}", out _));
    }

    [Fact]
    public void IntentGetStringIsSafeOnLoneSurrogate()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"kind":"ready","name":"\ud800"}""");
        var intent = new ShellIntent("ready", doc.RootElement.Clone());
        Assert.Null(intent.GetString("name"));
    }

    [Fact]
    public void FixtureCoversEverySettingsSection()
    {
        var state = ShellContract.Deserialize(FixtureJson())!;
        var s = state.Settings;
        Assert.Equal("general", state.Notch.Section);
        Assert.Equal(AccountProvider.Chatgpt, s.Account.Provider);
        Assert.Equal("ahmad@example.com", s.Account.SignedInAs);
        Assert.Null(s.Voice.OutputDeviceId);
        Assert.False(s.Voice.Voices[1].Available);
        Assert.Equal(DeviceState.Unavailable, s.Microphone.Devices[1].State);
        Assert.Equal(WhisperState.Downloading, s.Microphone.Whisper);
        Assert.Equal(["OpenClicky", "Ahmad"], s.Dictation.Dictionary);
        Assert.True(s.Dictation.Cleanup);
        Assert.Equal(WhisperState.Missing, s.Dictation.Multilingual);
        Assert.Equal(HotkeyTrigger.DoubleTap, s.Shortcuts.HandsFree.Trigger);
        Assert.Equal(ChromeState.Waiting, s.Integrations.Chrome);
        Assert.Equal([ConnectorStatus.Connected, ConnectorStatus.NeedsSignIn], s.Integrations.Connectors.Select(c => c.Status));
        Assert.Null(s.Integrations.Connectors[1].ToolCount);
        Assert.Equal([ReachState.Allowed, ReachState.Unknown], s.Agents.Reach.Select(r => r.State));
        Assert.Equal([RoutineStatus.Scheduled, RoutineStatus.Paused], s.Routines.Select(r => r.Status));
        Assert.Null(s.Routines[0].Last);
        Assert.Equal(AccessModeView.ConfirmEach, s.Access.Mode);
        Assert.False(s.Advanced.UseLocalProxy);
    }

    [Fact]
    public void EverySidebarSectionAndActionIsAccepted()
    {
        Assert.Equal(16, ShellContract.Sections.Count);
        Assert.Equal(28, ShellIntentParser.SettingsActions.Count);
        foreach (var section in ShellContract.Sections)
            Assert.True(ShellIntentParser.TryParse($$"""{"kind":"settings.section","section":"{{section}}"}""", out _), section);
        foreach (var action in ShellIntentParser.SettingsActions)
            Assert.True(ShellIntentParser.TryParse($$"""{"kind":"settings.action","action":"{{action}}"}""", out _), action);
    }

    [Fact]
    public void SettingsPatchAndActionLimitsAreEnforced()
    {
        static string Patch(string key, string value) => "{\"kind\":\"settings.patch\",\"key\":\"" + key + "\",\"value\":" + value + "}";
        static string Action(string arg) => "{\"kind\":\"settings.action\",\"action\":\"signIn\",\"arg\":\"" + arg + "\"}";
        // key: at most 60 chars
        var k60 = "a." + new string('b', 58);
        Assert.Equal(60, k60.Length);
        Assert.True(ShellIntentParser.TryParse(Patch(k60, "1"), out _));
        Assert.False(ShellIntentParser.TryParse(Patch(k60 + "b", "1"), out _));
        // value: at most 4,000 chars serialized, at most 4 levels deep
        Assert.True(ShellIntentParser.TryParse(Patch("a.b", "\"" + new string('x', 3_998) + "\""), out _));
        Assert.False(ShellIntentParser.TryParse(Patch("a.b", "\"" + new string('x', 3_999) + "\""), out _));
        Assert.True(ShellIntentParser.TryParse(Patch("a.b", "[[[[1]]]]"), out _));
        Assert.False(ShellIntentParser.TryParse(Patch("a.b", "[[[[[1]]]]]"), out _));
        Assert.False(ShellIntentParser.TryParse(Patch("a.b", "{\"x\":{\"x\":{\"x\":{\"x\":{\"x\":1}}}}}"), out _));
        // arg: at most 512 chars
        Assert.True(ShellIntentParser.TryParse(Action(new string('a', 512)), out _));
        Assert.False(ShellIntentParser.TryParse(Action(new string('a', 513)), out _));
    }

    private static string Wire<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonSerializer.Serialize(value, ShellContract.Options).Trim('"');

    private static string[] Values<T>() where T : struct, Enum => Enum.GetValues<T>().Select(Wire).ToArray();

    [Fact]
    public void EnumValuesMatchTheSharedList()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenClicky.sln"))) dir = dir.Parent;
        var shared = JsonNode.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "shell-enums.v1.json")))!.AsObject();
        var actual = new Dictionary<string, string[]>
        {
            ["sections"] = ShellContract.Sections.ToArray(),
            ["actions"] = ShellIntentParser.SettingsActions.ToArray(),
            ["theme"] = Values<ShellTheme>(),
            ["phase"] = Values<ShellPhase>(),
            ["tab"] = Values<NotchTab>(),
            ["jobState"] = Values<JobState>(),
            ["jobColour"] = Values<JobColour>(),
            ["jobFileKind"] = Values<JobFileKind>(),
            ["chatRole"] = Values<ChatRole>(),
            ["cardKind"] = Values<NotchCardKind>(),
            ["cardActionStyle"] = Values<CardActionStyle>(),
            ["documentKind"] = Values<DocumentKind>(),
            ["updateState"] = Values<UpdateState>(),
            ["accountProvider"] = Values<AccountProvider>(),
            ["deviceState"] = Values<DeviceState>(),
            ["whisperState"] = Values<WhisperState>(),
            ["hotkeyTrigger"] = Values<HotkeyTrigger>(),
            ["chromeState"] = Values<ChromeState>(),
            ["connectorStatus"] = Values<ConnectorStatus>(),
            ["reachState"] = Values<ReachState>(),
            ["routineStatus"] = Values<RoutineStatus>(),
            ["accessMode"] = Values<AccessModeView>(),
            ["drawingActions"] = ShellIntentParser.DrawingActions.ToArray(),
        };
        Assert.Equal(shared.Select(p => p.Key).Order(), actual.Keys.Order());
        foreach (var (name, values) in actual)
            Assert.True(shared[name]!.AsArray().Select(v => v!.GetValue<string>()).SequenceEqual(values), name + ": " + string.Join(",", values));
    }

    [Fact]
    public void RegionCountAndDepthAreBounded()
    {
        string Rects(int n) => "{\"kind\":\"surface.regions\",\"rects\":[" + string.Join(",", Enumerable.Repeat("{\"x\":0,\"y\":0,\"w\":1,\"h\":1,\"r\":0}", n)) + "]}";
        Assert.True(ShellIntentParser.TryParse(Rects(64), out _));
        Assert.False(ShellIntentParser.TryParse(Rects(65), out _));
        var deep = "{\"kind\":\"ready\",\"x\":" + new string('[', 40) + new string(']', 40) + "}";
        Assert.False(ShellIntentParser.TryParse(deep, out _));
    }
}
