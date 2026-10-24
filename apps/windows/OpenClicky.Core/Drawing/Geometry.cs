namespace OpenClicky.Core.Drawing;

/// <summary>A point in virtual-screen physical pixels. Core has no System.Drawing.Common, so geometry is its own.</summary>
public readonly record struct Pt(float X, float Y);

/// <summary>An axis-aligned box. Contains is half-open (left/top inclusive, right/bottom exclusive).</summary>
public readonly record struct Box(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public Pt Centre => new(X + W / 2, Y + H / 2);

    public bool Contains(Pt p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    /// <summary>Grows by <paramref name="d"/> on every side (shrinks when negative, never below zero size).</summary>
    public Box Inflate(float d) => new(X - d, Y - d, Math.Max(0, W + 2 * d), Math.Max(0, H + 2 * d));

    public Box Union(Box o)
    {
        var x = Math.Min(X, o.X);
        var y = Math.Min(Y, o.Y);
        return new(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }
}
