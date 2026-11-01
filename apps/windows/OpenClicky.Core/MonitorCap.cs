using System.Text.RegularExpressions;

namespace OpenClicky.Core;

/// <summary>A visible, non-minimised, non-cloaked top-level window of another process: its title and on-screen bounds.</summary>
public readonly record struct VisibleWindow(string Title, (int X, int Y, int Width, int Height) Bounds);

/// <summary>Decides which screenshots a Talk turn sends: never a display showing a protected window, at most four.</summary>
public static class MonitorCap
{
    /// <summary>
    /// ponytail: a fixed cap of 4 (cursor monitor + the monitor holding the foreground window + nearest others) keeps the
    /// request size bounded on big desk setups; make it a setting if anyone needs more.
    /// </summary>
    public const int Max = 4;

    private static readonly Regex Numbering = new(@"^screen \d+ of \d+", RegexOptions.Compiled);

    /// <summary>
    /// Returns at most <paramref name="max"/> monitors: the cursor monitor first, then the monitor holding most of the
    /// foreground window, then the others nearest to the cursor monitor. Labels that carry "screen K of N" are renumbered
    /// to the returned order so the model's :screenK marks stay in step with the list. A list already within the cap is
    /// returned untouched.
    /// </summary>
    public static IReadOnlyList<ScreenCaptureResult> Apply(IReadOnlyList<ScreenCaptureResult> screens, int max = Max,
        (int X, int Y, int Width, int Height)? foregroundBounds = null)
    {
        if (screens.Count <= max) return screens;
        var origin = screens.FirstOrDefault(s => s.IsCursorMonitor) ?? screens[0];
        var (ox, oy) = Centre(origin);
        var kept = new List<ScreenCaptureResult> { origin };
        if (foregroundBounds is { } window && max > 1)
        {
            var holder = screens.Where(s => !ReferenceEquals(s, origin))
                .Select(s => (Screen: s, Area: Overlap(s, window)))
                .Where(x => x.Area > 0)
                .OrderByDescending(x => x.Area)
                .Select(x => x.Screen)
                .FirstOrDefault();
            if (holder is not null && Overlap(holder, window) > Overlap(origin, window)) kept.Add(holder);
        }
        kept.AddRange(screens.Where(s => !kept.Any(k => ReferenceEquals(k, s)))
            .OrderBy(s => { var (x, y) = Centre(s); return (x - ox) * (x - ox) + (y - oy) * (y - oy); })
            .ThenBy(s => s.MonitorLeft)
            .Take(max - kept.Count));
        return Renumber(kept);
    }

    /// <summary>
    /// Drops every screen that intersects a window whose title matches the deny list, on any display (the protected-windows
    /// promise). The rest keep their order with "screen K of N" renumbered. Empty means nothing may be sent.
    /// </summary>
    public static IReadOnlyList<ScreenCaptureResult> WithoutProtectedWindows(IReadOnlyList<ScreenCaptureResult> screens,
        IEnumerable<VisibleWindow> windows, IEnumerable<string>? extraFragments = null)
    {
        var extras = extraFragments?.ToList();
        var protectedBounds = windows.Where(w => AgentDenyList.IsDenied(w.Title, extras)).Select(w => w.Bounds).ToList();
        if (protectedBounds.Count == 0) return screens;
        var kept = screens.Where(s => !protectedBounds.Any(b => Overlap(s, b) > 0)).ToList();
        return kept.Count == screens.Count ? screens : Renumber(kept);
    }

    private static List<ScreenCaptureResult> Renumber(List<ScreenCaptureResult> kept) =>
        kept.Select((s, i) => s with { Label = Numbering.Replace(s.Label, $"screen {i + 1} of {kept.Count}") }).ToList();

    private static (double X, double Y) Centre(ScreenCaptureResult s) =>
        (s.MonitorLeft + s.MonitorWidth / 2.0, s.MonitorTop + s.MonitorHeight / 2.0);

    private static long Overlap(ScreenCaptureResult s, (int X, int Y, int Width, int Height) w)
    {
        var width = Math.Min((long)s.MonitorLeft + s.MonitorWidth, (long)w.X + w.Width) - Math.Max(s.MonitorLeft, w.X);
        var height = Math.Min((long)s.MonitorTop + s.MonitorHeight, (long)w.Y + w.Height) - Math.Max(s.MonitorTop, w.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}
