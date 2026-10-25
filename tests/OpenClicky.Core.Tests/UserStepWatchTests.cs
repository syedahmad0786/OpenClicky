using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public sealed class UserStepWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static UserStep Step(string? doneWhen = "/welcome", string? page = "https://example.com/login") => new("Sign in", page, doneWhen, Start);

    [Fact]
    public void DoneWhenAddressContainsText() =>
        Assert.Equal(UserStepStatus.Done, new UserStepWatch(Step("/WELCOME"), Start).Check(Start.AddSeconds(5), "https://example.com/welcome?x=1"));

    [Fact]
    public void WaitingOtherwise() =>
        Assert.Equal(UserStepStatus.Waiting, new UserStepWatch(Step(), Start).Check(Start.AddSeconds(5), "https://example.com/login"));

    [Fact]
    public void NullAddressIsWaiting() =>
        Assert.Equal(UserStepStatus.Waiting, new UserStepWatch(Step(), Start).Check(Start.AddSeconds(5), null));

    [Fact]
    public void TimedOutAfterSixHours()
    {
        var watch = new UserStepWatch(Step(), Start);
        Assert.Equal(UserStepStatus.Waiting, watch.Check(Start + UserStepWatch.Limit - TimeSpan.FromSeconds(1), null));
        Assert.Equal(UserStepStatus.TimedOut, watch.Check(Start + UserStepWatch.Limit, null));
    }

    [Fact]
    public void NoDoneTextNeverAutoCompletes()
    {
        foreach (var none in new string?[] { null, "", "  " })
            Assert.Equal(UserStepStatus.Waiting, new UserStepWatch(Step(none), Start).Check(Start.AddSeconds(5), "https://example.com/anything"));
    }

    [Fact]
    public void TimingIsAsSpecified()
    {
        Assert.Equal(TimeSpan.FromSeconds(2.5), UserStepWatch.Poll);
        Assert.Equal(TimeSpan.FromHours(6), UserStepWatch.Limit);
    }

    [Fact]
    public void StepRoundTripsAndKeepsAskedAtWhereTheStoreReadsIt()
    {
        var json = UserStepWatch.ToJson(Step());
        Assert.Contains("\"askedAt\"", json);
        Assert.Equal(Step(), UserStepWatch.FromJson(json));
        Assert.Null(UserStepWatch.FromJson(""));
        Assert.Null(UserStepWatch.FromJson("not json"));
        Assert.Null(UserStepWatch.FromJson("{\"step\":\"\"}"));
    }

    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("http://example.com/", "http://example.com/")]
    [InlineData("file:///C:/Windows/System32/calc.exe", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("ms-settings:privacy", null)]
    [InlineData("example.com", null)]
    [InlineData(null, null)]
    public void OnlyWebPagesOpen(string? page, string? expected) =>
        Assert.Equal(expected, UserStepWatch.PageToOpen(Step(page: page))?.AbsoluteUri);
}
