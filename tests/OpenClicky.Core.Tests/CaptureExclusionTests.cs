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

    [Fact]
    public void MonitorCap_AlwaysKeepsTheForegroundWindowMonitorSecond()
    {
        // Cursor on monitor 0; the foreground window sits on the far monitor 5, which plain distance would drop.
        var screens = Enumerable.Range(0, 6).Select(i => Screen(i * 1000, 0, i == 0, $"screen {i + 1} of 6 - x")).ToList();
        var capped = MonitorCap.Apply(screens, MonitorCap.Max, (5100, 50, 600, 400));
        Assert.Equal([0, 5000, 1000, 2000], capped.Select(s => s.MonitorLeft));
        Assert.Equal(["screen 1 of 4 - x", "screen 2 of 4 - x", "screen 3 of 4 - x", "screen 4 of 4 - x"], capped.Select(s => s.Label));
    }

    [Fact]
    public void MonitorCap_ForegroundWindowOnTheCursorMonitorOrOffScreenChangesNothing()
    {
        var screens = Enumerable.Range(0, 6).Select(i => Screen(i * 1000, 0, i == 0, $"screen {i + 1} of 6 - x")).ToList();
        var plain = MonitorCap.Apply(screens).Select(s => s.MonitorLeft).ToList();
        Assert.Equal(plain, MonitorCap.Apply(screens, MonitorCap.Max, (100, 100, 300, 300)).Select(s => s.MonitorLeft));
        Assert.Equal(plain, MonitorCap.Apply(screens, MonitorCap.Max, (-9000, 0, 300, 300)).Select(s => s.MonitorLeft));
    }

    private sealed class FakeCapture(Func<IReadOnlyList<ScreenCaptureResult>>? onCapture = null) : IScreenCaptureService
    {
        public int Calls;
        public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        { Calls++; return Task.FromResult(onCapture?.Invoke() ?? [Screen(0, 0, true, "screen 1")]); }
    }

    private static OwnWindowsExcludedCapture Wrapper(FakeCapture inner, FakeWindows w, IReadOnlyList<OwnWindow> own, Action hide, List<TimeSpan> settles) =>
        new(inner, () => own, w.Get, w.Set, hide, _ => { }, delay: (t, _) => { settles.Add(t); return Task.CompletedTask; });

    [Fact]
    public async Task OwnWindowsCapture_ExcludesWhileCapturingThenRestoresAfterASettle()
    {
        var w = new FakeWindows { Affinity = { [1] = 0, [2] = 0x1 } };
        var seen = new List<uint>();
        var inner = new FakeCapture(() => { seen.AddRange(w.Affinity.Values); return [Screen(0, 0, true, "screen 1")]; });
        var settles = new List<TimeSpan>();
        var hidden = 0;
        var capture = Wrapper(inner, w, [new(1, false), new(2, true)], () => hidden++, settles);

        var result = await capture.CaptureAllMonitorsJpegAsync();

        Assert.Single(result);
        Assert.Equal([0x11u, 0x11u], seen);
        Assert.Equal(0u, w.Affinity[1]);
        Assert.Equal(0x1u, w.Affinity[2]);
        Assert.Equal([TimeSpan.FromMilliseconds(120)], settles);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public async Task OwnWindowsCapture_FailedOverlayHandleHidesTheOverlayButOtherFailuresDoNot()
    {
        var w = new FakeWindows { Affinity = { [1] = 0, [2] = 0 }, RefuseSet = { 1, 2 } };
        var hidden = 0;
        await Wrapper(new FakeCapture(), w, [new(1, false)], () => hidden++, []).CaptureAllMonitorsJpegAsync();
        Assert.Equal(0, hidden);
        await Wrapper(new FakeCapture(), w, [new(1, false), new(2, true)], () => hidden++, []).CaptureAllMonitorsJpegAsync();
        Assert.Equal(1, hidden);
    }

    [Fact]
    public async Task OwnWindowsCapture_NothingToChangeMeansNoSettle()
    {
        var w = new FakeWindows { Affinity = { [1] = 0x11 } }; // already excluded by the user's setting
        var settles = new List<TimeSpan>();
        await Wrapper(new FakeCapture(), w, [new(1, false)], () => { }, settles).CaptureAllMonitorsJpegAsync();
        await Wrapper(new FakeCapture(), w, [], () => { }, settles).CaptureAllMonitorsJpegAsync();
        Assert.Empty(settles);
        Assert.Empty(w.Calls);
    }

    [Fact]
    public async Task OwnWindowsCapture_RestoresWhenTheCaptureThrowsOrIsCancelled()
    {
        var w = new FakeWindows { Affinity = { [1] = 0 } };
        var throwing = Wrapper(new FakeCapture(() => throw new InvalidOperationException()), w, [new(1, false)], () => { }, []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwing.CaptureAllMonitorsJpegAsync());
        Assert.Equal(0u, w.Affinity[1]);

        using var cts = new CancellationTokenSource();
        var cancelled = new OwnWindowsExcludedCapture(new FakeCapture(), () => [new(1, false)], w.Get, w.Set, () => { }, _ => { },
            delay: (_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.CaptureAllMonitorsJpegAsync(cts.Token));
        Assert.Equal(0u, w.Affinity[1]);
    }

    [Fact]
    public async Task OwnWindowsCapture_OverlappingCapturesNeverRestoreMidCapture()
    {
        var w = new FakeWindows { Affinity = { [1] = 0 } };
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var slow = new OwnWindowsExcludedCapture(new GatedCapture(entered, release.Task), () => [new(1, false)], w.Get, w.Set, () => { }, _ => { },
            delay: (_, _) => Task.CompletedTask);
        var first = slow.CaptureAllMonitorsJpegAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = slow.CaptureAllMonitorsJpegAsync();
        await Task.Delay(100);
        Assert.False(second.IsCompleted);
        Assert.Equal(0x11u, w.Affinity[1]);   // still excluded for the first capture; the second has not touched anything
        Assert.Equal(["1=11"], w.Calls);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(0u, w.Affinity[1]);
        Assert.Equal(["1=11", "1=0", "1=11", "1=0"], w.Calls);
    }

    private sealed class GatedCapture(TaskCompletionSource entered, Task release) : IScreenCaptureService
    {
        private int _first = 1;
        public async Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        {
            if (Interlocked.Exchange(ref _first, 0) == 1) { entered.SetResult(); await release; }
            return [Screen(0, 0, true, "screen 1")];
        }
    }
}
