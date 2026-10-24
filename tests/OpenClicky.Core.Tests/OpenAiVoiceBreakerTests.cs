using OpenClicky.Core.Speech;

namespace OpenClicky.Core.Tests;

public class OpenAiVoiceBreakerTests
{
    [Fact]
    public void RejectedKeyIsSkippedForTheSessionButANewKeyIsTried()
    {
        var breaker = new OpenAiVoiceBreaker();
        Assert.True(breaker.ShouldTry(key: 1, nowMs: 0));
        breaker.Failed(key: 1, status: 401, nowMs: 0);
        Assert.False(breaker.ShouldTry(1, 0));
        Assert.False(breaker.ShouldTry(1, (long)TimeSpan.FromHours(5).TotalMilliseconds));
        Assert.True(breaker.ShouldTry(2, 10)); // the user saved another key
        breaker.Failed(2, 403, 10);
        Assert.False(breaker.ShouldTry(2, 20));
    }

    [Fact]
    public void NetworkFailurePausesTheOpenAiVoiceForFiveMinutes()
    {
        var breaker = new OpenAiVoiceBreaker();
        breaker.Failed(key: 1, status: null, nowMs: 1_000);
        Assert.False(breaker.ShouldTry(1, 1_000 + 299_999));
        Assert.False(breaker.ShouldTry(2, 1_000 + 1)); // a pause is about the network, not the key
        Assert.True(breaker.ShouldTry(1, 1_000 + 300_000));
        breaker.Failed(1, 500, 400_000);
        Assert.False(breaker.ShouldTry(1, 400_001));
        Assert.True(breaker.ShouldTry(1, 700_000));
    }
}
