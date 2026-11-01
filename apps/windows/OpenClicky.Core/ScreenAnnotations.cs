using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenClicky.Core;

public sealed record AnnotationPoint(int X, int Y);
public sealed record ScreenAnnotation(string Shape, int X, int Y, int A, int B, string Label, int? ScreenNumber, IReadOnlyList<AnnotationPoint>? Points = null);
public sealed record VirtualAnnotation(string Shape, int X, int Y, int A, int B, string Label, IReadOnlyList<AnnotationPoint>? Points = null);

public static partial class ScreenAnnotations
{
    [GeneratedRegex(@"\[MARK:([^\]\r\n]*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\[MARK:[^\r\n]*(?:\]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DanglingTags();

    public static (string Text, IReadOnlyList<ScreenAnnotation> Marks) Parse(string text)
    {
        var marks = new List<ScreenAnnotation>();
        var clean = Tags().Replace(text, match =>
        {
            var parts = match.Groups[1].Value.Split(':');
            if (marks.Count >= 6 || parts.Length is < 3 or > 4
                || parts[0] is not ("rect" or "circle" or "arrow" or "polygon" or "freehand" or "curve" or "text")
                || string.IsNullOrWhiteSpace(parts[2]) || parts[2].Length > 60
                || parts[2].Any(char.IsControl)) return "";
            var coords = parts[1].Split(',');
            var requiredCoordinates = parts[0] == "text" ? 2 : parts[0] == "curve" ? 8 : parts[0] is "polygon" or "freehand" ? -1 : 4;
            if (requiredCoordinates >= 0 && coords.Length != requiredCoordinates
                || requiredCoordinates < 0 && (coords.Length % 2 != 0 || coords.Length < (parts[0] == "polygon" ? 6 : 4) || coords.Length > 32)) return "";
            var numbers = new int[coords.Length];
            for (var i = 0; i < coords.Length; i++)
                if (!int.TryParse(coords[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return "";
            int? screen = null;
            if (parts.Length == 4)
            {
                if (!parts[3].StartsWith("screen", StringComparison.Ordinal)
                    || !int.TryParse(parts[3].AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1) return "";
                screen = n;
            }
            var points = parts[0] is "polygon" or "freehand" or "curve"
                ? numbers.Chunk(2).Select(pair => new AnnotationPoint(pair[0], pair[1])).ToArray()
                : null;
            marks.Add(new(parts[0], numbers[0], numbers[1], numbers.ElementAtOrDefault(2), numbers.ElementAtOrDefault(3), parts[2], screen, points));
            return "";
        });
        return (DanglingTags().Replace(clean, "").Trim(), marks);
    }

    public static IReadOnlyList<VirtualAnnotation> Map(IReadOnlyList<ScreenAnnotation> marks, IReadOnlyList<ScreenCaptureResult> screens)
    {
        var mapped = new List<VirtualAnnotation>();
        foreach (var mark in marks.Take(6))
        {
            if (mark.Shape is not ("rect" or "circle" or "arrow" or "polygon" or "freehand" or "curve" or "text") || screens.Count == 0
                || mark.ScreenNumber is int n && (n < 1 || n > screens.Count)) continue;
            var screen = mark.ScreenNumber is int index ? screens[index - 1] : screens.FirstOrDefault(s => s.IsCursorMonitor) ?? screens[0];
            if (screen.PixelWidth <= 0 || screen.PixelHeight <= 0 || screen.MonitorWidth <= 0 || screen.MonitorHeight <= 0) continue;
            if (mark.Shape is "polygon" or "freehand" or "curve")
            {
                var minimum = mark.Shape == "polygon" ? 3 : 2;
                if (mark.Points is not { Count: >= 2 and <= 16 } points || points.Count < minimum
                    || mark.Shape == "curve" && points.Count != 4
                    || points.Distinct().Count() < minimum
                    || points.Any(point => point.X < 0 || point.Y < 0 || point.X >= screen.PixelWidth || point.Y >= screen.PixelHeight)) continue;
                int PointX(int x) => screen.MonitorLeft + (int)Math.Round(x * screen.MonitorWidth / (double)screen.PixelWidth);
                int PointY(int y) => screen.MonitorTop + (int)Math.Round(y * screen.MonitorHeight / (double)screen.PixelHeight);
                var mappedPoints = points.Select(point => new AnnotationPoint(PointX(point.X), PointY(point.Y))).ToArray();
                mapped.Add(new(mark.Shape, mappedPoints[0].X, mappedPoints[0].Y, 0, 0, mark.Label, mappedPoints));
                continue;
            }
            long endX = mark.Shape == "arrow" ? mark.A : (long)mark.X + mark.A;
            long endY = mark.Shape == "arrow" ? mark.B : (long)mark.Y + mark.B;
            if (mark.Shape == "text") { endX = mark.X; endY = mark.Y; }
            if (mark.X < 0 || mark.Y < 0 || mark.X >= screen.PixelWidth || mark.Y >= screen.PixelHeight
                || endX < 0 || endY < 0 || endX > screen.PixelWidth || endY > screen.PixelHeight
                || mark.Shape is "rect" or "circle" && (mark.A <= 0 || mark.B <= 0)
                || mark.Shape == "arrow" && endX == mark.X && endY == mark.Y) continue;
            int X(long x) => screen.MonitorLeft + (int)Math.Round(x * screen.MonitorWidth / (double)screen.PixelWidth);
            int Y(long y) => screen.MonitorTop + (int)Math.Round(y * screen.MonitorHeight / (double)screen.PixelHeight);
            var startX = X(mark.X); var startY = Y(mark.Y);
            var a = mark.Shape == "arrow" ? X(endX) : X(endX) - startX;
            var b = mark.Shape == "arrow" ? Y(endY) : Y(endY) - startY;
            if (mark.Shape is "rect" or "circle" && (a <= 0 || b <= 0)) continue;
            mapped.Add(new(mark.Shape, startX, startY, a, b, mark.Label));
        }
        return mapped;
    }
}
