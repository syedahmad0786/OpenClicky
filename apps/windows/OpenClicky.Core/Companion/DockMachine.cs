using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.CompanionCursor;

public enum DockPhase
{
    /// <summary>Out and visible (Following, or pointing while docked).</summary>
    None,
    Bubble,
    FlyIn,
    Shrink,
    /// <summary>Docked and invisible.</summary>
    Gone,
    Grow,
}

/// <summary>What <see cref="DockMachine.Step"/> or <see cref="DockMachine.Interrupt"/> changed that the overlay must act on.</summary>
[Flags]
public enum DockEffect
{
    None = 0,
    /// <summary>Show the "i'll wait up here" bubble.</summary>
    BubbleStarted = 1,
    /// <summary>Take the dock bubble down (if it is still the line showing).</summary>
    BubbleEnded = 2,
    /// <summary>The companion must be placed at the notch point (it grows out from there).</summary>
    GrowStarted = 4,
}

/// <param name="WantsOut">A POINT or marks need the companion on screen even though it is docked.</param>
/// <param name="From">Where the companion is now (the start of a flight into the notch).</param>
public readonly record struct DockInput(CursorPresence Presence, bool WantsOut, bool CursorShown, bool ReducedMotion, Pt From, Pt Notch);

/// <summary>
/// The dock life cycle: a companion with nothing to do says it will wait (Bubble, 1.1 s), flies to the notch (FlyIn), shrinks
/// and fades (Shrink, 0.6 s) and stays hidden (Gone); a POINT, marks, or leaving Docked grows it back out of the notch (Grow,
/// 0.3 s). Reduced motion has no flight and fades in 120 ms. Pure: the overlay supplies time and acts on the effects.
/// </summary>
public sealed class DockMachine
{
    public const double BubbleSeconds = 1.1, ShrinkSeconds = 0.6, GrowSeconds = 0.3, ReducedSeconds = 0.12;
    double _start;
    FlightPath? _flight;
    bool _bubbleNext;

    public DockPhase Phase { get; private set; } = DockPhase.None;

    /// <summary>Whether the companion window is shown at all.</summary>
    public bool Visible => Phase != DockPhase.Gone;

    /// <summary>Bars, dots and bubbles are only drawn when the companion is not flying or shrinking into the notch.</summary>
    public bool Chrome => Phase is not (DockPhase.FlyIn or DockPhase.Shrink);

    /// <summary>
    /// Call when the presence setting changes: a companion that is on screen and is told to dock announces it first. A stale
    /// request (docking was never started) is dropped when the presence is anything but Docked.
    /// </summary>
    public void PresenceChanged(CursorPresence before, CursorPresence after, bool cursorShown)
    {
        if (before == CursorPresence.Following && after == CursorPresence.Docked && cursorShown) _bubbleNext = true;
        else if (after != CursorPresence.Docked) _bubbleNext = false;
    }

    /// <summary>Hidden for a capture mid-dock: docking is abandoned and the next appearance grows out of the notch.</summary>
    public DockEffect Interrupt()
    {
        if (Phase is not (DockPhase.Bubble or DockPhase.FlyIn or DockPhase.Shrink)) return DockEffect.None;
        var effect = Phase == DockPhase.Bubble ? DockEffect.BubbleEnded : DockEffect.None;
        Phase = DockPhase.Gone;
        return effect;
    }

    public DockEffect Step(double now, DockInput input)
    {
        if (input.Presence == CursorPresence.Off)
        {
            var ended = Phase == DockPhase.Bubble ? DockEffect.BubbleEnded : DockEffect.None;
            Phase = DockPhase.None;
            _bubbleNext = false;
            return ended;
        }

        var docked = input.Presence == CursorPresence.Docked;
        var wantOut = !docked || input.WantsOut;
        var fade = input.ReducedMotion;
        switch (Phase)
        {
            case DockPhase.None when !wantOut:
                if (!input.CursorShown) Phase = DockPhase.Gone;
                else if (_bubbleNext)
                {
                    _bubbleNext = false;
                    Begin(DockPhase.Bubble, now);
                    return DockEffect.BubbleStarted;
                }
                else StartFlyIn(now, input);
                break;
            case DockPhase.None when !input.CursorShown && docked:
                return StartGrow(now);
            case DockPhase.Bubble:
                if (wantOut) { Phase = DockPhase.None; return DockEffect.BubbleEnded; }
                if (now - _start >= BubbleSeconds) { StartFlyIn(now, input); return DockEffect.BubbleEnded; }
                break;
            case DockPhase.FlyIn:
                if (wantOut) Phase = DockPhase.None;
                else if (_flight is null || now - _start >= _flight.DurationSeconds) Begin(DockPhase.Shrink, now);
                break;
            case DockPhase.Shrink:
                if (wantOut) return StartGrow(now);
                if (now - _start >= (fade ? ReducedSeconds : ShrinkSeconds)) Phase = DockPhase.Gone;
                break;
            case DockPhase.Gone:
                if (wantOut) return StartGrow(now);
                break;
            case DockPhase.Grow:
                if (now - _start >= (fade ? ReducedSeconds : GrowSeconds)) Phase = DockPhase.None;
                break;
        }
        return DockEffect.None;
    }

    /// <summary>Where the companion is during FlyIn; null in every other phase.</summary>
    public Pt? FlightAt(double now) => Phase == DockPhase.FlyIn && _flight is { } flight ? flight.At(now - _start) : null;

    /// <summary>Triangle scale and window opacity (0..1) for the phase. Reduced motion only fades.</summary>
    public (float Scale, float Alpha) Look(double now, bool reducedMotion)
    {
        if (Phase is not (DockPhase.Shrink or DockPhase.Grow)) return (1, 1);
        var shrinking = Phase == DockPhase.Shrink;
        var seconds = reducedMotion ? ReducedSeconds : shrinking ? ShrinkSeconds : GrowSeconds;
        var p = (float)Math.Clamp((now - _start) / seconds, 0, 1);
        if (reducedMotion) return (1, shrinking ? 1 - p : p);
        var eased = p * p * (3 - 2 * p);
        return shrinking ? (Math.Max(0.02f, 1 - eased), 1 - p) : (Math.Max(0.02f, eased), 1);
    }

    void Begin(DockPhase phase, double now)
    {
        Phase = phase;
        _start = now;
    }

    void StartFlyIn(double now, DockInput input)
    {
        if (input.ReducedMotion) { Begin(DockPhase.Shrink, now); return; } // no flight: it fades where it is
        _flight = new FlightPath(input.From, input.Notch);
        Begin(DockPhase.FlyIn, now);
    }

    DockEffect StartGrow(double now)
    {
        Begin(DockPhase.Grow, now);
        return DockEffect.GrowStarted;
    }
}

public static class NotchPoint
{
    /// <summary>
    /// <paramref name="stored"/> if a live monitor still contains it; otherwise the top centre (+4 px) of the monitor nearest to
    /// it (so an unplugged or re-laid-out display cannot send the companion off screen). With no monitors, <paramref name="stored"/>
    /// or the origin.
    /// </summary>
    public static Pt Resolve(Pt? stored, IReadOnlyList<Box> monitors)
    {
        if (monitors.Count == 0) return stored ?? new Pt(0, 4);
        if (stored is { } point && float.IsFinite(point.X) && float.IsFinite(point.Y) && monitors.Any(m => m.Contains(point))) return point;
        var from = stored is { } p && float.IsFinite(p.X) && float.IsFinite(p.Y) ? p : new Pt(0, 0);
        var nearest = monitors.MinBy(m =>
        {
            var dx = Math.Max(Math.Max(m.X - from.X, 0), from.X - m.Right);
            var dy = Math.Max(Math.Max(m.Y - from.Y, 0), from.Y - m.Bottom);
            return dx * dx + dy * dy;
        });
        return new Pt(nearest.X + nearest.W / 2, nearest.Y + 4);
    }
}
