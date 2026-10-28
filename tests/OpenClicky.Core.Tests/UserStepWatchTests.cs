using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public sealed class UserStepWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static UserStep Step(string? doneWhen = "/welcome", string? page = "https://example.com/login") => new("Sign in", page, doneWhen, Start);

    [Fact]
    public void DoneWhenAddressContainsTextAfterANonMatchingRead()
    {
        var watch = new UserStepWatch(Step("/WELCOME"), Start);
        Assert.Equal(UserStepStatus.Waiting, watch.Check(Start.AddSeconds(2), null)); // an unreadable address is not a non-match
        Assert.Equal(UserStepStatus.Waiting, watch.Check(Start.AddSeconds(3), "https://example.com/login"));
        Assert.Equal(UserStepStatus.Done, watch.Check(Start.AddSeconds(5), "https://example.com/welcome?x=1"));
    }

    [Fact]
    public void AnAddressThatAlreadyMatchesWhenAskedDoesNotComplete()
    {
        var watch = new UserStepWatch(Step(), Start);
        Assert.Equal(UserStepStatus.Waiting, watch.Check(Start.AddSeconds(3), "https://example.com/welcome"));
        Assert.Equal(UserStepStatus.Waiting, watch.Check(Start.AddSeconds(6), "https://example.com/welcome"));
        // A resumed watch starts over: it too needs a non-matching read first.
        Assert.Equal(UserStepStatus.Waiting, new UserStepWatch(Step(), Start).Check(Start.AddHours(1), "https://example.com/welcome"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData(" /w ")]
    [InlineData("http")]
    [InlineData("HTTPS")]
    [InlineData("www")]
    [InlineData("https://")]
    [InlineData("https://www.")]
    public void ShortOrGenericDoneTextNeverAutoCompletes(string doneWhen)
    {
        var watch = new UserStepWatch(Step(doneWhen), Start);
        Assert.Equal(UserStepStatus.Waiting, watch.Check(Start.AddSeconds(3), "edge://newtab"));
        Assert.Equal(UserStepStatus.Waiting, watch.Check(Start.AddSeconds(6), "https://www.example.com/abc/w"));
    }

    [Fact]
    public void AfterThreeAutomaticContinuesADayOnlyImDoneCompletes()
    {
        var budget = new AutoContinueBudget();
        UserStepStatus Run(string jobId, DateTimeOffset at)
        {
            var watch = new UserStepWatch(Step(), at, budget, jobId);
            watch.Check(at.AddSeconds(3), "https://example.com/login");
            return watch.Check(at.AddSeconds(6), "https://example.com/welcome");
        }
        for (var i = 0; i < AutoContinueBudget.PerDay; i++) Assert.Equal(UserStepStatus.Done, Run("job1", Start));
        Assert.Equal(UserStepStatus.Waiting, Run("job1", Start)); // the fourth today waits for "I'm done"
        Assert.Equal(UserStepStatus.Done, Run("job2", Start)); // per job
        Assert.Equal(UserStepStatus.Done, Run("job1", Start.AddDays(1))); // and per day
        Assert.Equal(3, AutoContinueBudget.PerDay);
    }

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

    [Fact]
    public void ResumeWatchesAStepWithTimeLeft()
    {
        var (outcome, step) = UserStepWatch.Resume(UserStepWatch.ToJson(Step()), Start + UserStepWatch.Limit - TimeSpan.FromMinutes(1));
        Assert.Equal(UserStepResume.Watch, outcome);
        Assert.Equal(Step(), step);
    }

    [Fact]
    public void ResumeStopsAnExpiredStepWithoutWatchingIt()
    {
        Assert.Equal(UserStepResume.TimedOut, UserStepWatch.Resume(UserStepWatch.ToJson(Step()), Start + UserStepWatch.Limit).Outcome);
        Assert.Equal(UserStepResume.TimedOut, UserStepWatch.Resume(UserStepWatch.ToJson(Step()), Start.AddDays(3)).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"step\":\"\"}")]
    public void ResumeStopsAStepThatCannotBeRead(string? json) =>
        Assert.Equal((UserStepResume.Unreadable, (UserStep?)null), UserStepWatch.Resume(json, Start));

    [Fact]
    public void TimeoutSummaryIsAsSpecified() => Assert.Equal("Waited six hours for your step; stopped.", UserStepWatch.TimedOutSummary);
}
