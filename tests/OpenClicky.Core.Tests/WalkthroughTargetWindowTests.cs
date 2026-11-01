using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public sealed class WalkthroughTargetWindowTests
{
    [Fact]
    public void Track_AcceptsFreshExternalTargetAndRejectsOwnUi()
    {
        Assert.Equal((nint)22, WalkthroughTargetWindow.Track((nint)11, (nint)22, foregroundIsOpenClicky: false));
        Assert.Equal((nint)11, WalkthroughTargetWindow.Track((nint)11, (nint)33, foregroundIsOpenClicky: true));
        Assert.Equal((nint)11, WalkthroughTargetWindow.Track((nint)11, nint.Zero, foregroundIsOpenClicky: false));
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(22, true, true)]
    [InlineData(22, false, false)]
    public void MustRestore_OnlyWhenNoExternalTargetIsForeground(long foreground, bool ownUi, bool expected)
        => Assert.Equal(expected, WalkthroughTargetWindow.MustRestore((nint)foreground, ownUi));
}
