namespace OpenClicky.Core.Drawing;

/// <summary>
/// Maps draw ops from screenshot pixels to virtual-screen pixels. Screen selection follows <see cref="ScreenAnnotations.Map"/>:
/// <c>screen=N</c> picks the Nth screenshot, otherwise the cursor monitor. An op with any point or box corner outside its
/// screenshot is dropped, never clamped. Edits pass through unmapped with scale 1.
/// </summary>
public static class DrawingMapper
{
    public static IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> ToVirtual(IReadOnlyList<DrawOp> ops, IReadOnlyList<ScreenCaptureResult> screens)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(screens);
        var mapped = new List<(DrawOp, DrawOp, float)>();
        foreach (var op in ops)
        {
            if (op.IsEdit) { mapped.Add((op, op, 1f)); continue; }
            if (screens.Count == 0 || op.Screen is int n && (n < 1 || n > screens.Count)) continue;
            var index = op.Screen is int chosen ? chosen - 1 : Math.Max(0, IndexOfCursorMonitor(screens));
            var s = screens[index];
            if (s.PixelWidth <= 0 || s.PixelHeight <= 0 || s.MonitorWidth <= 0 || s.MonitorHeight <= 0) continue;
            bool Inside(float x, float y) => x >= 0 && y >= 0 && x < s.PixelWidth && y < s.PixelHeight;
            if (!op.Points.All(p => Inside(p.X, p.Y)) || !Inside(op.Box.X, op.Box.Y)
                || op.Box.Right > s.PixelWidth || op.Box.Bottom > s.PixelHeight) continue;
            float sx = s.MonitorWidth / (float)s.PixelWidth, sy = s.MonitorHeight / (float)s.PixelHeight;
            var source = op with { Screen = index + 1 };
            var virt = source with
            {
                Points = op.Points.Select(p => new Pt(s.MonitorLeft + p.X * sx, s.MonitorTop + p.Y * sy)).ToArray(),
                Box = new(s.MonitorLeft + op.Box.X * sx, s.MonitorTop + op.Box.Y * sy, op.Box.W * sx, op.Box.H * sy),
            };
            mapped.Add((virt, source, sx));
        }
        return mapped;
    }

    private static int IndexOfCursorMonitor(IReadOnlyList<ScreenCaptureResult> screens)
    {
        for (var i = 0; i < screens.Count; i++) if (screens[i].IsCursorMonitor) return i;
        return -1;
    }
}
