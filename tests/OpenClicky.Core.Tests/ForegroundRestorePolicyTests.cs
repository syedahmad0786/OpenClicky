using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class ForegroundRestorePolicyTests
{
    [Fact]
    public void ResolveTarget_PrefersValidForeignPrior()
    {
        var prior = (nint)0x1000;
        var shell = (nint)0x2000;
        Assert.Equal(prior, ForegroundRestorePolicy.ResolveTarget(prior, priorIsWindow: true, priorIsCurrentProcess: false, shell));
    }

    [Fact]
    public void ResolveTarget_RejectsOurProcess()
    {
        var prior = (nint)0x1000;
        var shell = (nint)0x2000;
        Assert.Equal(0, ForegroundRestorePolicy.ResolveTarget(prior, priorIsWindow: true, priorIsCurrentProcess: true, shell));
    }

    [Fact]
    public void ResolveTarget_RejectsInvalidPrior()
    {
        var prior = (nint)0x1000;
        var shell = (nint)0x2000;
        Assert.Equal(0, ForegroundRestorePolicy.ResolveTarget(prior, priorIsWindow: false, priorIsCurrentProcess: false, shell));
    }

    [Fact]
    public void ResolveTarget_RejectsOurProcessEvenWithoutShell()
    {
        var prior = (nint)0x1000;
        Assert.Equal(0, ForegroundRestorePolicy.ResolveTarget(prior, priorIsWindow: true, priorIsCurrentProcess: true, shell: 0));
    }

    [Fact]
    public void ResolveTarget_ZeroWhenNothingUsable()
    {
        Assert.Equal(0, ForegroundRestorePolicy.ResolveTarget(0, priorIsWindow: false, priorIsCurrentProcess: false, shell: 0));
    }
}
