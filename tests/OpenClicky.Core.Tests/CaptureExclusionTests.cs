using OpenClicky.Core;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class CaptureExclusionTests
{
    private static ScreenCaptureResult Screen(int left, int top, bool cursor, string label) =>
        new([0xff, 0xd8], 100, 50, left, top, 1000, 500, cursor, label);

    [Fact]
    public void MonitorCap_KeepsCursorMonitorPlusThreeNearestAndRenumbersLabels()
    {
        // Cursor monitor sits at x=3000; nearest by centre are 2000, 4000 and 1000; the far ones (0, 5000) are dropped.
        var screens = new[]
        {
            Screen(3000, 0, true, "screen 1 of 5 - cursor"),
            Screen(0, 0, false, "screen 2 of 5 - far left"),
            Screen(1000, 0, false, "screen 3 of 5 - mid left"),
            Screen(2000, 0, false, "screen 4 of 5 - next left"),
            Screen(4000, 0, false, "screen 5 of 5 - next right"),
        };
        var capped = MonitorCap.Apply(screens);
        Assert.Equal([3000, 2000, 4000, 1000], capped.Select(s => s.MonitorLeft));
        Assert.Equal(["screen 1 of 4 - cursor", "screen 2 of 4 - next left", "screen 3 of 4 - next right", "screen 4 of 4 - mid left"], capped.Select(s => s.Label));
        Assert.True(capped[0].IsCursorMonitor);
    }

    [Fact]
    public void MonitorCap_LeavesAShortListUntouched()
    {
        var screens = new[] { Screen(0, 0, true, "screen 1 of 2 - a"), Screen(1000, 0, false, "screen 2 of 2 - b") };
        Assert.Same(screens, MonitorCap.Apply(screens));
    }

    [Fact]
    public void MonitorCap_UsesFirstMonitorWhenNoneIsMarkedAsCursor()
    {
        var screens = Enumerable.Range(0, 6).Select(i => Screen(i * 1000, 0, false, $"screen {i + 1} of 6 - x")).ToList();
        var capped = MonitorCap.Apply(screens);
        Assert.Equal(4, capped.Count);
        Assert.Equal(0, capped[0].MonitorLeft);
        Assert.Equal([0, 1000, 2000, 3000], capped.Select(s => s.MonitorLeft));
    }

    private sealed class FakeWindows
    {
        public Dictionary<nint, uint> Affinity = [];
        public HashSet<nint> RefuseSet = [];
        public HashSet<nint> Unreadable = [];
        public List<string> Calls = [];
        public uint? Get(nint h) => Unreadable.Contains(h) ? null : Affinity[h];
        public bool Set(nint h, uint v) { Calls.Add($"{h}={v:X}"); if (RefuseSet.Contains(h) && v == DisplayAffinityScope.ExcludeFromCapture) return false; Affinity[h] = v; return true; }
    }

    [Fact]
    public void AffinityScope_ExcludesDuringScopeAndRestoresExactPreviousValues()
    {
        var w = new FakeWindows { Affinity = { [1] = 0x0, [2] = 0x1 } }; // 2 had WDA_MONITOR before
        using (var scope = new DisplayAffinityScope([1, 2], w.Get, w.Set))
        {
            Assert.Equal(0x11u, w.Affinity[1]);
            Assert.Equal(0x11u, w.Affinity[2]);
            Assert.Equal(2, scope.ChangedCount);
        }
        Assert.Equal(0x0u, w.Affinity[1]);
        Assert.Equal(0x1u, w.Affinity[2]);
    }

    [Fact]
    public void AffinityScope_NeverTouchesAWindowThatIsAlreadyExcludedByTheUser()
    {
        var w = new FakeWindows { Affinity = { [1] = 0x11 } }; // "show in recordings" off
        using (var scope = new DisplayAffinityScope([1], w.Get, w.Set)) Assert.Equal(0, scope.ChangedCount);
        Assert.Empty(w.Calls);
        Assert.Equal(0x11u, w.Affinity[1]);
    }

    [Fact]
    public void AffinityScope_ReportsRefusedAndUnreadableWindowsAndRestoresTheRest()
    {
        var w = new FakeWindows { Affinity = { [1] = 0, [2] = 0, [3] = 0 }, RefuseSet = { 2 }, Unreadable = { 3 } };
        using (var scope = new DisplayAffinityScope([0, 1, 2, 3, 1], w.Get, w.Set))
        {
            Assert.Equal(new HashSet<nint> { 2, 3 }, scope.Failed);
            Assert.Equal(new HashSet<nint> { 1 }, scope.Excluded);
        }
        Assert.Equal(0u, w.Affinity[1]);
        Assert.Equal(["1=11", "2=11", "1=0"], w.Calls); // zero handle skipped, duplicate handle changed once, only the changed window restored
    }

    [Fact]
    public void AffinityScope_RestoreRunsOnceAndCountsFailures()
    {
        var restores = 0;
        var scope = new DisplayAffinityScope([1], _ => 0u, (_, v) => { if (v == 0) { restores++; return false; } return true; });
        scope.Dispose();
        scope.Dispose();
        Assert.Equal(1, restores);
        Assert.Equal(1, scope.RestoreFailures);
    }
}
