using OpenClicky.Agent;
using System.IO;
using Xunit;

namespace OpenClicky.Input.Tests;

public class InstalledAppLaunchVerifierTests
{
    [Fact]
    public void LimitedInformationProcessReadGetsExactCurrentExecutablePath()
    {
        var observed = AppLaunchVerifier.ObserveProcess((uint)Environment.ProcessId);

        Assert.NotNull(observed.ProcessPath);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(observed.ProcessPath!), ignoreCase: true);
        Assert.NotNull(observed.AppUserModelIdResult);
    }

    [Fact]
    public void AcceptedLaunchWithoutDestinationIsNotVerified() =>
        Assert.False(AppLaunchVerifier.Matches(null, "Microsoft.WindowsCalculator!App", null));

    [Fact]
    public void ClosedDestinationIsNotVerified() =>
        Assert.False(AppLaunchVerifier.Matches(null, "Microsoft.WindowsCalculator!App", new(0, null, null, null)));

    [Fact]
    public void DifferentForegroundWindowIsNotVerified() =>
        Assert.False(AppLaunchVerifier.Matches(null, "Microsoft.WindowsCalculator!App", new(42, "Calculator", null, "Other.App!Main")));

    [Fact]
    public void PackagedAppDisplayIdentityVerifiesForegroundWindow() =>
        Assert.True(AppLaunchVerifier.Matches(null, "Microsoft.WindowsCalculator!App", new(42, "Unrelated title", null, "Microsoft.WindowsCalculator!App")));

    [Fact]
    public void PackagedAppWithoutObservedIdentityIsNotVerified() =>
        Assert.False(AppLaunchVerifier.Matches(null, "Microsoft.WindowsCalculator!App", new(42, "Calculator", null, null)));

    [Fact]
    public void VisibleHostedChildWithExactIdentityVerifies()
    {
        var identity = AppLaunchVerifier.ResolveHostedIdentity("Microsoft.WindowsCalculator!App",
        [
            new(101, 11, true, "Microsoft.WindowsCalculator!App")
        ]);

        Assert.Equal("Microsoft.WindowsCalculator!App", identity);
        Assert.True(AppLaunchVerifier.Matches(null, "Microsoft.WindowsCalculator!App",
            new(42, "Calculator", null, identity)));
    }

    [Fact]
    public void HostedIdentityRejectsWrongHiddenAndInvalidCandidates()
    {
        var identity = AppLaunchVerifier.ResolveHostedIdentity("Microsoft.WindowsCalculator!App",
        [
            new(101, 11, true, "Other.App!Main"),
            new(102, 12, false, "Microsoft.WindowsCalculator!App"),
            new(0, 13, true, "Microsoft.WindowsCalculator!App"),
            new(103, 0, true, "Microsoft.WindowsCalculator!App")
        ]);

        Assert.Null(identity);
    }

    [Fact]
    public void UnrelatedCandidateOutsideForegroundTreeCannotBeSuppliedByResolver()
    {
        Assert.Null(AppLaunchVerifier.ResolveHostedIdentity("Microsoft.WindowsCalculator!App", []));
    }

    [Fact]
    public void ClassicAppRequiresExactExecutablePathWhenTitleDiffers()
    {
        const string expected = @"C:\Windows\System32\notepad.exe";
        Assert.True(AppLaunchVerifier.Matches(expected, null, new(42, "notes.txt", expected, null)));
        Assert.False(AppLaunchVerifier.Matches(expected, null, new(42, "Notepad", @"C:\Windows\System32\calc.exe", null)));
    }
}
