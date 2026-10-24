using System.Text.Json;

namespace OpenClicky.Core.Surfaces;

/// <summary>A page-reported hit region in CSS pixels, relative to the surface viewport.</summary>
public readonly record struct CssRegionRect(double X, double Y, double Width, double Height, double Radius);

/// <summary>A hit region in device (client) pixels, ready for a window region.</summary>
public readonly record struct DeviceRegionRect(int X, int Y, int Width, int Height, int Radius);

/// <summary>Validates <c>surface.regions</c> page messages and converts them to device pixels.</summary>
public static class SurfaceRegions
{
    public const int MaxRects = 64;
    private const double MaxCssCoordinate = 100_000;

    /// <summary>
    /// Reads the <c>rects</c> array of a page message. Malformed entries (non-numbers, non-finite,
    /// non-positive size, absurd coordinates) are dropped; at most <see cref="MaxRects"/> are kept.
    /// </summary>
    public static IReadOnlyList<CssRegionRect> Parse(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("rects", out var rects)
            || rects.ValueKind != JsonValueKind.Array) return [];
        var result = new List<CssRegionRect>();
        foreach (var item in rects.EnumerateArray())
        {
            if (result.Count == MaxRects) break;
            if (item.ValueKind != JsonValueKind.Object
                || !TryRead(item, "x", out var x) || !TryRead(item, "y", out var y)
                || !TryRead(item, "w", out var w) || !TryRead(item, "h", out var h)) continue;
            var r = TryRead(item, "r", out var radius) ? Math.Max(0, radius) : 0;
            if (w <= 0 || h <= 0) continue;
            result.Add(new CssRegionRect(x, y, w, h, r));
        }
        return result;
    }

    /// <summary>
    /// Converts CSS rects to device pixels at <paramref name="scale"/> (device px per CSS px), growing
    /// outward to whole pixels so anti-aliased edges stay inside, clamping to the client area, and
    /// dropping rects that end up empty. The radius is capped at half the shorter side.
    /// </summary>
    public static IReadOnlyList<DeviceRegionRect> ToDevice(IReadOnlyList<CssRegionRect> rects, double scale, int clientWidth, int clientHeight)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var result = new List<DeviceRegionRect>(rects.Count);
        foreach (var rect in rects)
        {
            var left = Clamp(Math.Floor(rect.X * scale), clientWidth);
            var top = Clamp(Math.Floor(rect.Y * scale), clientHeight);
            var right = Clamp(Math.Ceiling((rect.X + rect.Width) * scale), clientWidth);
            var bottom = Clamp(Math.Ceiling((rect.Y + rect.Height) * scale), clientHeight);
            int width = right - left, height = bottom - top;
            if (width <= 0 || height <= 0) continue;
            var radius = (int)Math.Min(Math.Round(rect.Radius * scale), Math.Min(width, height) / 2);
            result.Add(new DeviceRegionRect(left, top, width, height, Math.Max(0, radius)));
        }
        return result;
    }

    /// <summary>Share of the client area the page may claim as hit region unless the host allows large regions.</summary>
    public const double MaxAreaFraction = 0.45;

    /// <summary>
    /// Keeps a page from turning the whole monitor into an input sink: unless <paramref name="allowLarge"/>,
    /// rects are dropped from the end until their union covers at most <see cref="MaxAreaFraction"/> of the client area.
    /// </summary>
    public static IReadOnlyList<DeviceRegionRect> ApplyAreaCap(IReadOnlyList<DeviceRegionRect> rects, int clientWidth, int clientHeight, bool allowLarge)
    {
        if (allowLarge) return rects;
        var limit = MaxAreaFraction * Math.Max(0, clientWidth) * (double)Math.Max(0, clientHeight);
        var count = rects.Count;
        while (count > 0 && UnionArea(rects.Take(count).ToList()) > limit) count--;
        return count == rects.Count ? rects : rects.Take(count).ToList();
    }

    /// <summary>Exact area of the union of rects (sweep over distinct x edges, merging y intervals per strip).</summary>
    public static double UnionArea(IReadOnlyList<DeviceRegionRect> rects)
    {
        var xs = rects.SelectMany(r => new[] { r.X, r.X + r.Width }).Distinct().Order().ToArray();
        double area = 0;
        for (var i = 0; i + 1 < xs.Length; i++)
        {
            int left = xs[i], right = xs[i + 1];
            var spans = rects.Where(r => r.X <= left && r.X + r.Width >= right)
                .Select(r => (Top: r.Y, Bottom: r.Y + r.Height)).OrderBy(s => s.Top);
            long covered = 0;
            int runTop = 0, runBottom = int.MinValue;
            foreach (var (top, bottom) in spans)
            {
                if (top > runBottom) { if (runBottom > runTop) covered += runBottom - runTop; runTop = top; runBottom = bottom; }
                else runBottom = Math.Max(runBottom, bottom);
            }
            if (runBottom > runTop) covered += runBottom - runTop;
            area += (double)covered * (right - left);
        }
        return area;
    }

    private static bool TryRead(JsonElement item, string name, out double value)
    {
        value = 0;
        return item.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value)
            && Math.Abs(value) <= MaxCssCoordinate;
    }

    private static int Clamp(double value, int max) => (int)Math.Clamp(value, 0, Math.Max(0, max));
}
