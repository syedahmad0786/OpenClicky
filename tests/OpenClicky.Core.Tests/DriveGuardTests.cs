using OpenClicky.Core.CompanionCursor;

namespace OpenClicky.Core.Tests;

public class DriveGuardTests
{
    private const int Low = 0x1000, Medium = 0x2000, High = 0x3000, SystemRid = 0x4000;

    [Fact]
    public void InputIntoOpenClickyItselfIsRefused()
    {
        Assert.False(DriveGuard.RefusesOwnTarget(confirmOpen: false, foregroundIsOwn: false, pointIsOwn: false));
        Assert.False(DriveGuard.RefusesOwnTarget(false, false, null)); // a key press has no point
        Assert.True(DriveGuard.RefusesOwnTarget(confirmOpen: true, foregroundIsOwn: false, pointIsOwn: false)); // any OpenClicky prompt is up
        Assert.True(DriveGuard.RefusesOwnTarget(false, foregroundIsOwn: true, null)); // typing would land in OpenClicky
        Assert.True(DriveGuard.RefusesOwnTarget(false, false, pointIsOwn: true)); // a click on an OpenClicky window
        Assert.True(DriveGuard.RefusesOwnTarget(false, foregroundIsOwn: true, pointIsOwn: true));
    }

    [Fact]
    public void AFocusedOpenClickyWindowDoesNotBlockClicksElsewhere()
    {
        // The notch composer keeps focus after a typed request: a click or scroll on another app still goes through.
        Assert.False(DriveGuard.RefusesOwnTarget(confirmOpen: false, foregroundIsOwn: true, pointIsOwn: false));
        // but never while an OpenClicky prompt is up.
        Assert.True(DriveGuard.RefusesOwnTarget(confirmOpen: true, foregroundIsOwn: true, pointIsOwn: false));
    }

    [Fact]
    public void HigherIntegrityRefuses()
    {
        Assert.True(DriveGuard.Refuses(High, Medium));
        Assert.True(DriveGuard.Refuses(SystemRid, High));
        Assert.True(DriveGuard.Refuses(Medium + 0x100, Medium)); // Medium Plus (uiAccess) is above Medium
    }

    [Fact]
    public void SameOrLowerAllowed()
    {
        Assert.False(DriveGuard.Refuses(Medium, Medium));
        Assert.False(DriveGuard.Refuses(Low, Medium));
        Assert.False(DriveGuard.Refuses(Medium, High));
    }

    [Fact]
    public void UnreadableIntegrityRefuses()
    {
        // An elevated app's token cannot be opened from a Medium process: input Windows would silently drop is refused instead.
        Assert.True(DriveGuard.Refuses(null, Medium));
        Assert.True(DriveGuard.Refuses(null, SystemRid));
    }
}
