using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class OverlayRespondingModeTests
{
    [Fact]
    public void Resolve_WhenPointingActive_KeepsPointing()
    {
        Assert.Equal(OverlaySpokenVisualMode.Pointing, OverlayRespondingMode.Resolve(pointingActive: true));
    }

    [Fact]
    public void Resolve_WhenNotPointing_UsesResponding()
    {
        Assert.Equal(OverlaySpokenVisualMode.Responding, OverlayRespondingMode.Resolve(pointingActive: false));
    }
}
