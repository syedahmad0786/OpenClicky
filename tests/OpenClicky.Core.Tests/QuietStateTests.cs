using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public sealed class QuietStateTests
{
    [Theory]
    [InlineData(2, "Busy")]
    [InlineData(3, "Full screen")]
    [InlineData(4, "Presenting")]
    [InlineData(6, "Quiet hours")]
    public void NotificationStatesThatMean(int state, string reason)
    {
        Assert.Equal(new QuietState(true, reason), QuietState.From(state, false));
        Assert.Equal(new QuietState(true, reason), QuietState.From(state, true));
    }

    [Fact]
    public void MicInUseByAnotherAppIsACall() => Assert.Equal(new QuietState(true, "On a call"), QuietState.From(5, true));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(99)]
    public void OtherwiseNotQuiet(int state) => Assert.Equal(new QuietState(false, null), QuietState.From(state, false));
}
