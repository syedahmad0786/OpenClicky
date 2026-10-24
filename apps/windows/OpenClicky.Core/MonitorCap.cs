using System.Text.RegularExpressions;

namespace OpenClicky.Core;

/// <summary>Bounds the number of screenshots a Talk turn sends.</summary>
public static class MonitorCap
{
    /// <summary>
    /// ponytail: a fixed cap of 4 (cursor monitor + 3 nearest by centre distance) keeps the request size bounded on big
    /// desk setups; make it a setting if anyone needs more.
    /// </summary>
    public const int Max = 4;

    private static readonly Regex Numbering = new(@"^screen \d+ of \d+", RegexOptions.Compiled);

    /// <summary>
    /// Returns the cursor monitor followed by its nearest neighbours, at most <paramref name="max"/>. Labels that carry
    /// "screen K of N" are renumbered to the returned order so the model's :screenK marks stay in step with the list.
    /// A list already within the cap is returned untouched.
    /// </summary>
    public static IReadOnlyList<ScreenCaptureResult> Apply(IReadOnlyList<ScreenCaptureResult> screens, int max = Max)
    {
        if (screens.Count <= max) return screens;
        var origin = screens.FirstOrDefault(s => s.IsCursorMonitor) ?? screens[0];
        var (ox, oy) = Centre(origin);
        var kept = new List<ScreenCaptureResult> { origin };
        kept.AddRange(screens.Where(s => !ReferenceEquals(s, origin))
            .OrderBy(s => { var (x, y) = Centre(s); return (x - ox) * (x - ox) + (y - oy) * (y - oy); })
            .ThenBy(s => s.MonitorLeft)
            .Take(max - 1));
        return kept.Select((s, i) => s with { Label = Numbering.Replace(s.Label, $"screen {i + 1} of {kept.Count}") }).ToList();
    }

    private static (double X, double Y) Centre(ScreenCaptureResult s) =>
        (s.MonitorLeft + s.MonitorWidth / 2.0, s.MonitorTop + s.MonitorHeight / 2.0);
}
