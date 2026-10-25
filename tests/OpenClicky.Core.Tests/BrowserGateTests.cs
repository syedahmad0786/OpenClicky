using OpenClicky.Core.Browser;
using OpenClicky.Core.Shell;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class BrowserGateTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)] // disconnected in the card: a host still on the pipe gets no work
    [InlineData(false, true, false)]
    public void OffersSessionOnlyWhenConnectedAndRegistered(bool connected, bool registered, bool offered) =>
        Assert.Equal(offered, BrowserGate.OffersSession(connected, registered));

    [Theory]
    [InlineData(false, true, 1, true, ChromeState.NoBrowser)]
    [InlineData(true, false, 1, true, ChromeState.NotInstalled)]
    [InlineData(true, true, 0, false, ChromeState.Waiting)]
    [InlineData(true, true, 1, true, ChromeState.Working)]
    [InlineData(true, true, 2, false, ChromeState.Connected)]
    public void CardStateFollowsDetectionRegistrationAndInstances(bool detected, bool registered, int instances, bool working, ChromeState expected) =>
        Assert.Equal(expected, BrowserGate.CardState(detected, registered, instances, working));
}
