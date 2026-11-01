using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.CompanionCursor;

/// <summary>Where the dashed click ring around a POINT target goes, and where its label sits. Pure; sizes are DIP x scale.</summary>
public static class ClickRingLayout
{
    public const double PulseSeconds = 1.6, FadeInSeconds = 0.12;
    public const float PulseAlphaMin = 0.75f, PulseInflateDip = 3, InflateDip = 6, MinHalfDip = 28, MaxHalfDip = 180, MaxCornerDip = 10, LabelGapDip = 8, LabelHeightDip = 23;

    /// <summary>
    /// With a <paramref name="controlFrame"/>: the frame grown by 6 DIP, corner radius min(10 DIP, h / 2). Without one: a circle of
    /// 28 DIP radius round <paramref name="centre"/>. Each half-extent is clamped to 28..180 DIP round the same middle.
    /// <c>LabelAnchor</c> is the label's bottom centre 8 DIP over the ring's top centre; <c>LabelBelow</c> flips it to the top
    /// centre 8 DIP under the ring when a <see cref="LabelHeightDip"/>-tall label would leave the work area's top edge.
    /// </summary>
    public static (Box Ring, float CornerRadius, Pt LabelAnchor, bool LabelBelow) From(Box? controlFrame, Pt centre, float dipScale, Box workArea)
    {
        var s = float.IsFinite(dipScale) && dipScale > 0 ? dipScale : 1;
        Box ring;
        float corner;
        if (controlFrame is { } frame && float.IsFinite(frame.X) && float.IsFinite(frame.Y) && float.IsFinite(frame.W) && float.IsFinite(frame.H))
        {
            var grown = frame.Inflate(InflateDip * s);
            var middle = grown.Centre;
            var halfW = Math.Clamp(grown.W / 2, MinHalfDip * s, MaxHalfDip * s);
            var halfH = Math.Clamp(grown.H / 2, MinHalfDip * s, MaxHalfDip * s);
            ring = new Box(middle.X - halfW, middle.Y - halfH, halfW * 2, halfH * 2);
            corner = Math.Min(MaxCornerDip * s, ring.H / 2);
        }
        else
        {
            var r = MinHalfDip * s;
            ring = new Box(centre.X - r, centre.Y - r, r * 2, r * 2);
            corner = r;
        }

        var gap = LabelGapDip * s;
        var below = ring.Y - gap - LabelHeightDip * s < workArea.Y;
        var anchor = new Pt(ring.X + ring.W / 2, below ? ring.Bottom + gap : ring.Y - gap);
        return (ring, corner, anchor, below);
    }

    /// <summary>
    /// The breathing pulse after the ring has been up for <paramref name="seconds"/>: opacity 0.75 to 1 and up to 3 DIP bigger on a
    /// 1.6 s cycle (smallest and faintest when it lands), times a 120 ms fade-in. Reduced motion: no pulse, only the fade-in.
    /// </summary>
    public static (float Alpha, float InflateDip) Pulse(double seconds, bool reducedMotion)
    {
        var t = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var fade = (float)Math.Min(1, t / FadeInSeconds);
        if (reducedMotion) return (fade, 0);
        var p = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * t / PulseSeconds));
        return ((PulseAlphaMin + (1 - PulseAlphaMin) * p) * fade, PulseInflateDip * p);
    }
}
