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
    public void RegionCountAndDepthAreBounded()
    {
        string Rects(int n) => "{\"kind\":\"surface.regions\",\"rects\":[" + string.Join(",", Enumerable.Repeat("{\"x\":0,\"y\":0,\"w\":1,\"h\":1,\"r\":0}", n)) + "]}";
        Assert.True(ShellIntentParser.TryParse(Rects(64), out _));
        Assert.False(ShellIntentParser.TryParse(Rects(65), out _));
        var deep = "{\"kind\":\"ready\",\"x\":" + new string('[', 40) + new string(']', 40) + "}";
        Assert.False(ShellIntentParser.TryParse(deep, out _));
    }
}
