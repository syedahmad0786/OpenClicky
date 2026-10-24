using System.Drawing.Imaging;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Overlay;

/// <summary>
/// The persistent drawing in its own layered window, sized to what the parts paint rather than the whole virtual screen. It
/// is re-presented only while a reveal or erase runs (at most every 33 ms) or after a change, and hides when empty, so a
/// still drawing costs nothing per frame. Reduced motion: no pen reveal; parts snap in and fade over 120 ms. UI thread only.
/// </summary>
internal sealed class DrawingWindow : IDisposable
{
    private const double FrameSeconds = 0.033, ReducedFadeSeconds = 0.12;
    private const long MaxPixels = DrawingRenderer.MaxInkPixels;

    private readonly LayeredWindow _window;
    private IReadOnlyList<DrawingRenderer.PartLayout> _layouts = [];
    private readonly Dictionary<(string Id, int Seq), (double Start, double Duration)> _reveals = [];
    private readonly List<(DrawingRenderer.PartLayout Layout, double Start, double Duration)> _erasing = [];
    private Bitmap? _bitmap;
    private bool _dirty;
    private double _lastPresent = double.NegativeInfinity;
    // The one spotlight lives SpotlightSeconds from when it appeared, then fades and is dropped (the document keeps it, so an
    // expired one is remembered and not laid out again). The one-shot timer wakes the owner's frame loop, which may be stopped.
    private (string Id, int Seq)? _spotKey, _spotExpired;
    private double _spotStart;
    private readonly System.Windows.Forms.Timer _expiry = new() { Interval = (int)(DrawingRenderer.SpotlightSeconds * 1000) + 50 };

    public DrawingWindow(Action<string>? log = null)
    {
        _window = new("OpenClicky Ink", log);
        _expiry.Tick += (_, _) =>
        {
            _expiry.Stop();
            Wake?.Invoke();
        };
    }

    public nint Handle => _window.Handle;

    public bool HasParts => _layouts.Count > 0;

    /// <summary>Called (UI thread) when a spotlight is due to start fading and the frame loop needs to run.</summary>
    public Action? Wake { get; set; }

    /// <summary>Replaces the drawing. Animation starts are relative to <paramref name="now"/> (seconds).</summary>
    public void Show(IReadOnlyList<DrawPart> parts, IReadOnlyList<PartAnimation> animations, double now)
    {
        _layouts = DrawingRenderer.Layout(parts);
        TrackSpotlight(now);
        foreach (var animation in animations)
        {
            var start = now + animation.Start.TotalSeconds;
            if (animation.Erase)
            {
                if (DrawingRenderer.Layout([animation.Part]) is [var erased]) _erasing.Add((erased, start, animation.Duration.TotalSeconds));
            }
            else _reveals[(animation.Part.Id, animation.Part.Seq)] = (start, animation.Duration.TotalSeconds);
        }
        var live = parts.Select(p => (p.Id, p.Seq)).ToHashSet();
        foreach (var key in _reveals.Keys.Where(k => !live.Contains(k)).ToArray()) _reveals.Remove(key);
        if (_layouts.Count == 0 && _erasing.Count == 0)
        {
            // Cleared, perhaps while hidden (Render does not run then): let the bitmap go now.
            _bitmap?.Dispose();
            _bitmap = null;
        }
        _dirty = true;
    }

    private static bool IsSpotlight(DrawingRenderer.PartLayout l) => l.Part.Op.Kind == DrawOpKind.Spotlight;

    /// <summary>A new spotlight starts its 30 s now; the one that already expired stays gone; none stops the timer.</summary>
    private void TrackSpotlight(double now)
    {
        var spot = _layouts.FirstOrDefault(IsSpotlight);
        var key = spot is null ? ((string, int)?)null : (spot.Part.Id, spot.Part.Seq);
        if (key is not null && key == _spotExpired)
        {
            _layouts = _layouts.Where(l => !IsSpotlight(l)).ToArray();
            key = null;
        }
        if (key == _spotKey) return;
        _spotKey = key;
        _expiry.Stop();
        if (key is null) return;
        _spotExpired = null;
        _spotStart = now;
        _expiry.Start();
    }

    /// <summary>Every reveal and erase ends now.</summary>
    public void Finish()
    {
        _reveals.Clear();
        _erasing.Clear();
        _dirty = true;
    }

    /// <summary>Paint again on the next frame (the window was hidden, or the accent changed).</summary>
    public void Invalidate() => _dirty = true;

    public void HideWindow() => _window.HideWindow();

    /// <summary>The pen tip of the part revealing now, for the cursor to ride; null when nothing is being drawn.</summary>
    public Pt? PenTip(double now, bool reducedMotion)
    {
        if (reducedMotion) return null;
        foreach (var layout in _layouts)
        {
            if (_reveals.TryGetValue((layout.Part.Id, layout.Part.Seq), out var r) && now >= r.Start && now < r.Start + r.Duration)
                return DrawingRenderer.PenTip(layout, (float)((now - r.Start) / r.Duration));
        }
        return null;
    }

    /// <summary>
    /// The drawn buttons that can be clicked now: fully revealed, at most three, in drawing order. Rects are virtual-screen
    /// pixels, rounded out to the pill that was painted.
    /// </summary>
    public void LiveButtons(List<(string Label, string Prompt, Rectangle Rect)> into)
    {
        into.Clear();
        foreach (var l in _layouts)
        {
            if (into.Count == DrawingRenderer.MaxButtons) break;
            if (l.ButtonRect is { } pill && !_reveals.ContainsKey((l.Part.Id, l.Part.Seq)))
                into.Add((l.Part.Op.Text ?? "", l.Part.Op.Args.GetValueOrDefault("prompt") ?? "", Rectangle.Round(pill)));
        }
    }

    /// <summary>Presents the drawing if it changed or is animating; true while an animation still runs.</summary>
    public bool Render(double now, Color accent, bool reducedMotion)
    {
        double End(double start, double duration) => start + (reducedMotion ? ReducedFadeSeconds : duration);
        foreach (var key in _reveals.Where(r => now >= End(r.Value.Start, r.Value.Duration)).Select(r => r.Key).ToArray())
        {
            _reveals.Remove(key);
            _dirty = true; // its last frame shows it whole
        }
        if (_erasing.RemoveAll(e => now >= End(e.Start, e.Duration)) > 0) _dirty = true;
        var animating = _reveals.Count > 0 || _erasing.Count > 0;
        var spotAlpha = 1f;
        if (_spotKey is { } spotKey && now - _spotStart > DrawingRenderer.SpotlightSeconds)
        {
            spotAlpha = DrawingRenderer.SpotlightAlpha(now - _spotStart, reducedMotion);
            if (spotAlpha <= 0)
            {
                _layouts = _layouts.Where(l => !IsSpotlight(l)).ToArray();
                _reveals.Remove(spotKey);
                _spotExpired = spotKey;
                _spotKey = null;
                spotAlpha = 1;
                _dirty = true;
            }
            else animating = true; // repainted at the animation pace while it fades
        }

        if (_layouts.Count == 0 && _erasing.Count == 0)
        {
            _window.HideWindow();
            _bitmap?.Dispose();
            _bitmap = null;
            _dirty = false;
            return false;
        }
        if (!_dirty && (!animating || now - _lastPresent < FrameSeconds)) return animating;

        var union = _layouts.Concat(_erasing.Select(e => e.Layout)).Select(l => l.Bounds).Aggregate(RectangleF.Union);
        var area = Rectangle.Intersect(Rectangle.FromLTRB((int)MathF.Floor(union.Left), (int)MathF.Floor(union.Top),
            (int)MathF.Ceiling(union.Right), (int)MathF.Ceiling(union.Bottom)), SystemInformation.VirtualScreen);
        // ponytail: a drawing over more than MaxPixels (two 4K monitors, 64 MB) shows only on the monitor holding most of
        // it; per-monitor ink windows if drawings spanning three big screens matter.
        if ((long)area.Width * area.Height > MaxPixels) area = Rectangle.Intersect(area, Screen.FromRectangle(area).Bounds);
        if (area.Width <= 0 || area.Height <= 0)
        {
            _window.HideWindow();
            _dirty = false;
            return animating;
        }
        if (_bitmap is null || _bitmap.Size != area.Size)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        }
        using (var g = Graphics.FromImage(_bitmap))
        {
            g.Clear(Color.Transparent);
            CursorRenderer.Prepare(g);
            g.TranslateTransform(-area.X, -area.Y);
            // A spotlight dims what is under it, so it goes first: every other part stays bright whenever it was added.
            foreach (var layout in _layouts.OrderBy(l => l.Part.Op.Kind == DrawOpKind.Spotlight ? 0 : 1))
            {
                var (progress, alpha) = State(layout.Part, now, reducedMotion);
                DrawingRenderer.Draw(g, layout, progress, IsSpotlight(layout) ? alpha * spotAlpha : alpha, accent);
            }
            foreach (var (layout, start, duration) in _erasing)
            {
                if (now < start) { DrawingRenderer.Draw(g, layout, 1, 1, accent); continue; }
                DrawingRenderer.Draw(g, layout, 1, (float)(1 - (now - start) / (reducedMotion ? ReducedFadeSeconds : duration)), accent);
            }
        }
        _window.Present(_bitmap, area.Location);
        _dirty = false;
        _lastPresent = now;
        return animating;
    }

    /// <summary>How far the pen has drawn a part, and its opacity: hidden before its start; reduced motion fades it in instead.</summary>
    private (float Progress, float Alpha) State(DrawPart part, double now, bool reducedMotion)
    {
        if (!_reveals.TryGetValue((part.Id, part.Seq), out var r)) return (1, 1);
        if (now < r.Start) return (0, 0);
        return reducedMotion
            ? (1, (float)Math.Min(1, (now - r.Start) / ReducedFadeSeconds))
            : ((float)Math.Min(1, (now - r.Start) / r.Duration), 1);
    }

    public void Dispose()
    {
        _expiry.Dispose();
        _window.Dispose();
        _bitmap?.Dispose();
    }
}
