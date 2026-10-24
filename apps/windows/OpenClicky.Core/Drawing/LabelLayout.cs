namespace OpenClicky.Core.Drawing;

/// <summary>Keeps drawing label pills from covering each other.</summary>
public static class LabelLayout
{
    private const int MaxTries = 4;
    private const float GapDip = 4;

    /// <summary>
    /// In order, a pill that overlaps one already placed moves down by its height plus 4 DIP, up to 4 times; it is then
    /// clamped inside <paramref name="workArea"/> (where it may still overlap).
    /// </summary>
    public static IReadOnlyList<Box> Resolve(IReadOnlyList<Box> pills, Box workArea, float dip = 1)
    {
        ArgumentNullException.ThrowIfNull(pills);
        var placed = new List<Box>(pills.Count);
        foreach (var pill in pills)
        {
            var box = pill;
            for (var tries = 0; tries < MaxTries && placed.Any(other => Overlaps(other, box)); tries++)
                box = box with { Y = box.Y + box.H + GapDip * dip };
            placed.Add(box with
            {
                X = Math.Clamp(box.X, workArea.X, Math.Max(workArea.X, workArea.Right - box.W)),
                Y = Math.Clamp(box.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - box.H)),
            });
        }
        return placed;
    }

    private static bool Overlaps(Box a, Box b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
