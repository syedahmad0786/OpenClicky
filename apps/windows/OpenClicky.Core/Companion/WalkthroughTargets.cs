using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.CompanionCursor;

/// <summary>
/// What a walkthrough step asks the user to click (or, for a hover step, rest the pointer on), in virtual-screen px.
/// <paramref name="Frame"/> is the snapped control's bounds, or a zero-size box at <paramref name="Centre"/> when nothing snapped.
/// </summary>
public sealed record GuidedTarget(Box Frame, Pt Centre, float Radius, bool IsHover, string Label)
{
    public bool HasFrame => Frame.W > 0 && Frame.H > 0;

    /// <summary>Inside the frame grown by <see cref="WalkthroughTargetWatch.HitSlop"/>, or within <see cref="Radius"/> of the centre without a frame.</summary>
    public bool Hits(Pt p)
    {
        if (HasFrame) return Frame.Inflate(WalkthroughTargetWatch.HitSlop).Contains(p);
        float dx = p.X - Centre.X, dy = p.Y - Centre.Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

public enum TargetEvent { None, Advance, LeftGuide, Expired, GuideTimedOut }

/// <summary>
/// Watches the user's physical clicks and pointer against the armed walkthrough target. Pure and single-threaded (the tray
/// drives it on the UI thread); times are a monotonic millisecond clock.
/// </summary>
public sealed class WalkthroughTargetWatch
{
    public const int ArmMs = 120_000, GuideIdleMs = 600_000, HoverMs = 2_000, HitSlop = 6;
    /// <summary>A switch to another app this soon after an advance is the click's own doing (it opened that app).</summary>
    public const int SwitchGraceMs = 3_000;
    public const string StillThereStatus = "Still there? Click the highlighted control or say next step.";
    public const string ClickedElsewhereReason = "the user clicked outside the guided target";
    public const string SwitchedAppsReason = "the user switched apps";
    public const string TimedOutReason = "ten minutes passed without the user moving on";

    private GuidedTarget? _target;
    private long _armedAt;
    private bool _expiredSent;
    private long? _hoverSince;
    private long? _idleSince;
    private long? _lastAdvance;

    public GuidedTarget? Target => _target;

    /// <summary>The walkthrough is active: its idle clock starts now unless it already runs (it restarts at each advance).</summary>
    public void Activate(long nowMs) => _idleSince ??= nowMs;

    /// <summary>The idle clock starts over (an agent task is running: its time is not the user's idleness).</summary>
    public void RestartIdle(long nowMs) => _idleSince = nowMs;

    /// <summary>A new step's target. The idle clock starts here too if <see cref="Activate"/> was not called.</summary>
    public void Arm(GuidedTarget t, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(t);
        _target = t;
        _armedAt = nowMs;
        _expiredSent = false;
        _hoverSince = null;
        _idleSince ??= nowMs;
    }

    /// <summary>
    /// A physical button press at <paramref name="p"/>. On one of OpenClicky's own windows (a card, the shell, a drawn button)
    /// it is ignored; inside the target it advances; anywhere else it leaves the guide. Either way the target is used up.
    /// </summary>
    public TargetEvent OnClick(Pt p, bool onOwnWindow, long nowMs)
    {
        if (_target is not { } t || onOwnWindow) return TargetEvent.None;
        if (t.Hits(p)) return Advanced(nowMs);
        Disarm();
        return TargetEvent.LeftGuide;
    }

    /// <summary>The pointer's position (every 100 ms): resting inside a hover target for <see cref="HoverMs"/> advances.</summary>
    public TargetEvent OnPointer(Pt p, long nowMs)
    {
        if (_target is not { IsHover: true } t) return TargetEvent.None;
        if (!t.Hits(p)) { _hoverSince = null; return TargetEvent.None; }
        _hoverSince ??= nowMs;
        return nowMs - _hoverSince.Value >= HoverMs ? Advanced(nowMs) : TargetEvent.None;
    }

    /// <summary>
    /// <see cref="TargetEvent.GuideTimedOut"/> once <see cref="GuideIdleMs"/> pass without an advance; otherwise
    /// <see cref="TargetEvent.Expired"/> once per arm after <see cref="ArmMs"/> (the target stays armed).
    /// </summary>
    public TargetEvent Tick(long nowMs)
    {
        if (_idleSince is { } idle && nowMs - idle >= GuideIdleMs)
        {
            Reset();
            return TargetEvent.GuideTimedOut;
        }
        if (_target is null || _expiredSent || nowMs - _armedAt < ArmMs) return TargetEvent.None;
        _expiredSent = true;
        return TargetEvent.Expired;
    }

    /// <summary>True when the foreground may move to another app now without ending the guide.</summary>
    public bool InSwitchGrace(long nowMs) => _lastAdvance is { } t && nowMs - t < SwitchGraceMs;

    /// <summary>The target is gone (a step is loading, a task runs); the guide's clocks keep running.</summary>
    public void Disarm()
    {
        _target = null;
        _hoverSince = null;
    }

    /// <summary>The walkthrough ended: everything is forgotten.</summary>
    public void Reset()
    {
        Disarm();
        _idleSince = null;
        _lastAdvance = null;
    }

    private TargetEvent Advanced(long nowMs)
    {
        Disarm();
        _idleSince = _lastAdvance = nowMs;
        return TargetEvent.Advance;
    }
}

/// <summary>One element of the UI Automation chain under a point (the element first, then its ancestors).</summary>
public sealed record SnapCandidate(Box Bounds, bool Interactive, bool OwnProcess);

public static class TargetSnap
{
    public const float MaxMonitorShare = 0.25f;

    /// <summary>
    /// The first candidate that is interactive, contains <paramref name="centre"/>, is not OpenClicky's own and covers at most a
    /// quarter of <paramref name="monitor"/>'s area; otherwise null.
    /// </summary>
    public static Box? Choose(IReadOnlyList<SnapCandidate> chain, Pt centre, Box monitor)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var limit = monitor.W * monitor.H * MaxMonitorShare;
        foreach (var c in chain)
        {
            var b = c.Bounds;
            if (c.Interactive && !c.OwnProcess && Finite(b) && b.W > 0 && b.H > 0 && b.Contains(centre) && b.W * b.H <= limit)
                return b;
        }
        return null;
    }

    private static bool Finite(Box b) => float.IsFinite(b.X) && float.IsFinite(b.Y) && float.IsFinite(b.W) && float.IsFinite(b.H);
}

public static class GuidedTargets
{
    public const float MinRadius = 28, MaxRadius = 180;

    /// <summary>Radius = clamp(max(w, h) / 2 + 6, 28, 180) for a snapped frame; without one, a 28 px circle round the centre.</summary>
    public static GuidedTarget From(Pt centre, Box? snapped, string? label, bool isHover)
    {
        var text = label ?? "";
        if (snapped is { W: > 0, H: > 0 } frame && float.IsFinite(frame.W) && float.IsFinite(frame.H))
            return new(frame, centre, Math.Clamp(Math.Max(frame.W, frame.H) / 2 + WalkthroughTargetWatch.HitSlop, MinRadius, MaxRadius), isHover, text);
        return new(new Box(centre.X, centre.Y, 0, 0), centre, MinRadius, isHover, text);
    }
}
