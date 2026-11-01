using System.Text.Json;
using OpenClicky.Core.Proactive;

namespace OpenClicky.Core.Tests;

public class SuggestionScheduleTests
{
    private static DateTimeOffset At(int h, int m = 0, int day = 25) => new(2026, 10, day, h, m, 0, TimeSpan.Zero);
    private static Suggestion S(string id, DateTimeOffset expires) => new(id, "t" + id, "d", "p", expires.AddHours(-24), expires);

    [Fact]
    public void DueOnceADayInHours()
    {
        var s = new SuggestionState();
        Assert.False(SuggestionSchedule.IsDue(s, At(8, 59)));
        Assert.True(SuggestionSchedule.IsDue(s, At(9)));
        Assert.True(SuggestionSchedule.IsDue(s, At(19, 59)));
        Assert.False(SuggestionSchedule.IsDue(s, At(20)));

        s.Failures = 2;
        s.NextAttempt = At(9, 30);
        SuggestionSchedule.Succeeded(s, At(10), [S("a", At(10, 0, 26))]);
        Assert.Equal(("2026-10-25", 0, (DateTimeOffset?)null), (s.Day, s.Failures, s.NextAttempt));
        Assert.Equal("a", Assert.Single(s.Pending).Id);
        Assert.False(SuggestionSchedule.IsDue(s, At(10, 1)));
        Assert.False(SuggestionSchedule.IsDue(s, At(19, 59)));
        Assert.False(SuggestionSchedule.IsDue(s, At(8, 59, 26)));
        Assert.True(SuggestionSchedule.IsDue(s, At(9, 0, 26)));
    }

    [Fact]
    public void BacksOffOnFailure()
    {
        var s = new SuggestionState();
        var now = At(9);
        int[] waits = [5, 15, 60, 180, 180];
        for (var i = 0; i < waits.Length; i++)
        {
            SuggestionSchedule.Failed(s, now);
            Assert.Equal(i + 1, s.Failures);
            Assert.Equal(now.AddMinutes(waits[i]), s.NextAttempt);
            Assert.False(SuggestionSchedule.IsDue(s, now.AddMinutes(waits[i]).AddSeconds(-1)));
            Assert.True(SuggestionSchedule.IsDue(s, now.AddMinutes(waits[i])));
            now = now.AddMinutes(waits[i]);
        }
        Assert.Null(s.Day);
    }

    [Fact]
    public void BackoffRestartsNextDay()
    {
        var s = new SuggestionState();
        for (var i = 0; i < 4; i++) SuggestionSchedule.Failed(s, At(19));
        Assert.Equal((4, At(19).AddMinutes(180)), (s.Failures, s.NextAttempt!.Value));
        SuggestionSchedule.Failed(s, At(9, 0, 26));
        Assert.Equal((1, At(9, 5, 26)), (s.Failures, s.NextAttempt!.Value));
    }

    [Fact]
    public void DeclineKeepsNewestTwenty()
    {
        var s = new SuggestionState();
        for (var i = 0; i < 25; i++) SuggestionSchedule.Decline(s, "idea " + i);
        SuggestionSchedule.Decline(s, "IDEA 24");
        SuggestionSchedule.Decline(s, "  ");
        Assert.Equal(20, s.Declined.Count);
        Assert.Equal("idea 5", s.Declined[0]);
        Assert.Equal("idea 24", s.Declined[^1]);
    }

    [Fact]
    public void PruneExpiredDropsOld()
    {
        var s = new SuggestionState { Pending = [S("old", At(9)), S("edge", At(10)), S("new", At(11))] };
        SuggestionSchedule.PruneExpired(s, At(10));
        Assert.Equal(["new"], s.Pending.Select(p => p.Id));
    }

    [Fact]
    public void StateRoundTripsThroughJson()
    {
        var s = new SuggestionState { Day = "2026-10-25", Failures = 1, NextAttempt = At(9, 5), Pending = [S("a", At(11))], Declined = ["x"] };
        var back = JsonSerializer.Deserialize<SuggestionState>(JsonSerializer.Serialize(s))!;
        Assert.Equal((s.Day, s.Failures, s.NextAttempt), (back.Day, back.Failures, back.NextAttempt));
        Assert.Equal(s.Pending, back.Pending);
        Assert.Equal(s.Declined, back.Declined);
        var empty = JsonSerializer.Deserialize<SuggestionState>("{}")!;
        Assert.Empty(empty.Pending);
        Assert.Empty(empty.Declined);
    }

    [Fact]
    public async Task TalkSourceNullWithoutClient()
    {
        Assert.Null(await new TalkClientSuggestionSource(() => (null, "m")).CompleteAsync("sys", "user", CancellationToken.None));

        var talk = new FakeTalk();
        var reply = await new TalkClientSuggestionSource(() => (talk, "model-x")).CompleteAsync("sys", "user", CancellationToken.None);
        Assert.Equal("reply", reply);
        Assert.Equal(("sys", "user", "model-x", 0, 0), (talk.System, talk.User, talk.Model, talk.HistoryCount, talk.ScreenCount));
    }

    private sealed class FakeTalk : ITalkClient
    {
        public string? System, User, Model;
        public int HistoryCount = -1, ScreenCount = -1;

        public Task<string> CompleteVisionTalkAsync(string systemPrompt, IReadOnlyList<(string role, string text)> history, string userTranscript,
            IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken cancellationToken = default)
        {
            (System, User, Model, HistoryCount, ScreenCount) = (systemPrompt, userTranscript, model, history.Count, screens.Count);
            return Task.FromResult("reply");
        }
    }
}
