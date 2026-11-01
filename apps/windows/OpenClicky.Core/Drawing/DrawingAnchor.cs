namespace OpenClicky.Core.Drawing;

/// <summary>
/// Which window the drawing lines up with: the last foreground window that is not OpenClicky's, taken when a turn captures the
/// screen (or, failing that, when a drawing appears). OpenClicky's own windows never become the anchor and never make the
/// drawing stale; another window coming to the front, or the anchor moving or resizing, does. Not thread-safe (UI timer).
/// </summary>
public sealed class DrawingAnchor
{
    private (nint Window, (int X, int Y, int Width, int Height)? Bounds)? _last, _anchor;
    private bool _shown;

    /// <summary>A turn captured the screen with <paramref name="window"/> in front.</summary>
    public void Capture(nint window, (int X, int Y, int Width, int Height)? bounds, bool ours)
    {
        Observe(window, bounds, ours);
        _anchor = _last;
    }

    /// <summary>One watch tick: true when a drawing is up and no longer lines up with its anchor, so it should be cleared.</summary>
    public bool IsStale(nint window, (int X, int Y, int Width, int Height)? bounds, bool ours, bool hasDrawing)
    {
        var foreign = Observe(window, bounds, ours);
        if (!hasDrawing)
        {
            if (_shown) _anchor = null; // cleared: the next drawing takes a fresh anchor
            _shown = false;
            return false;
        }
        if (!_shown) { _shown = true; _anchor ??= _last; }
        return foreign && _anchor is { } a && (a.Window != window || a.Bounds != bounds);
    }

    private bool Observe(nint window, (int X, int Y, int Width, int Height)? bounds, bool ours)
    {
        var foreign = window != 0 && !ours;
        if (foreign) _last = (window, bounds);
        return foreign;
    }
}
