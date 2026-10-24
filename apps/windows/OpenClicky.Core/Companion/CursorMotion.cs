using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.CompanionCursor;

public static class CursorConstants
{
    /// <summary>DIP; the companion tip rests at the real cursor + this offset.</summary>
    public const float TipOffsetX = 35;
    public const float TipOffsetY = 25;
    public const float RestRotationDegrees = -35;
    public const float TriangleSide = 16;
    public const float CornerFraction = 0.09f;
    public const float GlowMax = 20;
    public const double Stiffness = 220;
    public const double Damping = 24;
    /// <summary>A physical cursor move of this many px cancels a return-to-cursor flight.</summary>
    public const float ReturnCancelDistance = 100;
}

/// <summary>Damped spring chasing a target. Semi-implicit Euler in fixed 1/240 s substeps; dt clamped to 0.1 s.</summary>
public sealed class CursorSpring
{
    const double SubStep = 1.0 / 240;
    const double MaxDt = 0.1;
    readonly double _k, _c;
    double _x, _y, _vx, _vy;

    public CursorSpring(Pt start, double stiffness = CursorConstants.Stiffness, double damping = CursorConstants.Damping)
    {
        _k = stiffness; _c = damping; _x = start.X; _y = start.Y;
    }

    public Pt Position => new((float)_x, (float)_y);
    public Pt Velocity => new((float)_vx, (float)_vy);

    public Pt Step(Pt target, double dtSeconds)
    {
        var left = Math.Clamp(double.IsFinite(dtSeconds) ? dtSeconds : 0, 0, MaxDt);
        while (left > 1e-9)
        {
            var h = Math.Min(SubStep, left);
            _vx += (_k * (target.X - _x) - _c * _vx) * h;
            _vy += (_k * (target.Y - _y) - _c * _vy) * h;
            _x += _vx * h;
            _y += _vy * h;
            left -= h;
        }
        return Position;
    }

    public void Snap(Pt p) { _x = p.X; _y = p.Y; _vx = _vy = 0; }
}

/// <summary>Curved fly-to path: quadratic Bezier arcing toward screen-up, ease-in-out cubic timing.</summary>
public sealed class FlightPath
{
    readonly Pt _from, _to, _control;

    public FlightPath(Pt from, Pt to)
    {
        _from = from; _to = to;
        double dx = to.X - from.X, dy = to.Y - from.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        DurationSeconds = Math.Clamp(0.25 + dist / 2400, 0.3, 0.9);
        double px = 0, py = 0;
        if (dist > 1e-6)
        {
            px = -dy / dist; py = dx / dist;
            if (py > 0 || (py == 0 && px < 0)) { px = -px; py = -py; } // toward screen-up
        }
        var off = 0.18 * dist;
        _control = new((float)((from.X + to.X) / 2 + px * off), (float)((from.Y + to.Y) / 2 + py * off));
    }

    public double DurationSeconds { get; }

    public Pt At(double t)
    {
        var u = Math.Clamp(t / DurationSeconds, 0, 1);
        if (u <= 0) return _from;
        if (u >= 1) return _to;
        var e = u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
        var a = (1 - e) * (1 - e); var b = 2 * (1 - e) * e; var c = e * e;
        return new((float)(a * _from.X + b * _control.X + c * _to.X), (float)(a * _from.Y + b * _control.Y + c * _to.Y));
    }
}

/// <summary>Detects a rapid back-and-forth cursor shake (the "detach" gesture).</summary>
public sealed class ShakeDetector
{
    const long WindowMs = 500, CooldownMs = 1000;
    const float MinSegment = 8, MinPath = 260;
    const int MinReversals = 6;
    readonly List<(Pt P, long Ms)> _samples = [];
    long _lastFire = long.MinValue;

    /// <summary>True once per shake; further shaking within the 1 s cooldown is ignored.</summary>
    public bool Add(Pt p, long ms)
    {
        _samples.Add((p, ms));
        _samples.RemoveAll(s => ms - s.Ms > WindowMs);
        if (_lastFire != long.MinValue && ms - _lastFire < CooldownMs) return false;

        float path = 0;
        for (var i = 1; i < _samples.Count; i++)
        {
            var a = _samples[i - 1].P; var b = _samples[i].P;
            path += MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        if (path < MinPath) return false;
        if (Math.Max(Reversals(s => s.X), Reversals(s => s.Y)) < MinReversals) return false;

        _lastFire = ms;
        _samples.Clear();
        return true;
    }

    /// <summary>Direction flips on one axis, ignoring wiggles shorter than <see cref="MinSegment"/>.</summary>
    int Reversals(Func<Pt, float> axis)
    {
        var count = 0; var dir = 0;
        var anchor = axis(_samples[0].P); var extreme = anchor;
        foreach (var (pt, _) in _samples.Skip(1))
        {
            var v = axis(pt);
            if (dir == 0)
            {
                if (Math.Abs(v - anchor) >= MinSegment) { dir = Math.Sign(v - anchor); extreme = v; }
            }
            else if (Math.Sign(v - extreme) == dir || v == extreme) extreme = v;
            else if (Math.Abs(extreme - v) >= MinSegment) { count++; dir = -dir; extreme = v; }
        }
        return count;
    }
}

public static class CursorShape
{
    /// <summary>Equilateral triangle, apex at <paramref name="tip"/> (index 0), body trailing behind it; at -35 deg the apex points up-left.</summary>
    public static Pt[] Triangle(Pt tip, float scale, float rotationDegrees)
    {
        var s = CursorConstants.TriangleSide * scale;
        var h = s * MathF.Sqrt(3) / 2;
        var r = rotationDegrees * MathF.PI / 180;
        float cos = MathF.Cos(r), sin = MathF.Sin(r);
        Pt At(float x, float y) => new(tip.X + x * cos - y * sin, tip.Y + x * sin + y * cos);
        return [tip, At(-s / 2, h), At(s / 2, h)];
    }
}

public static class BubbleLayout
{
    /// <summary>Small pill under-right of the tip: tip + (10, 18), clamped into the work area.</summary>
    public static Box Label(Pt tip, float w, float h, Box workArea) => Clamp(tip.X + 10, tip.Y + 18, w, h, workArea);

    /// <summary>Speech bubble beside the tip: tip + (22, -6), flipped left of the tip when it would cross the right edge, then clamped.</summary>
    public static Box Message(Pt tip, float w, float h, Box workArea)
    {
        var x = tip.X + 22;
        if (x + w > workArea.Right) x = tip.X - 22 - w;
        return Clamp(x, tip.Y - 6, w, h, workArea);
    }

    static Box Clamp(float x, float y, float w, float h, Box a) => new(
        Math.Max(a.X, Math.Min(x, a.Right - w)),
        Math.Max(a.Y, Math.Min(y, a.Bottom - h)),
        w, h);
}
