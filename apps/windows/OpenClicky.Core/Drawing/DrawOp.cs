namespace OpenClicky.Core.Drawing;

/// <summary>Every drawing op. The edits (<see cref="Recolor"/> and later) change existing parts instead of adding one.</summary>
public enum DrawOpKind
{
    Line, Arrow, Curve, Poly, Path, Circle, Rect, Dot, Text, Highlight, Graph, Timeline, Point, Target, Frame, Ring, Trace,
    Label, Underline, Strike, Cross, Check, Star, Scroll, Spotlight, Badge, Button, Card, Sticky, Tie, Figure,
    Recolor, Move, MoveTo, Scale, Erase, Undo, Clear,
}

/// <summary>
/// One parsed [DRAW:…] tag. Coordinates are screenshot pixels until <see cref="DrawingMapper"/> maps them to virtual pixels.
/// <see cref="Points"/> holds the op's anchor/polyline points (ring and the x/y/r circle keep their centre; box shapes have none);
/// <see cref="Box"/> is the op's bounds (zero size for single-point ops). <see cref="Text"/> is the <c>text</c> key, else the
/// <c>label</c>. <see cref="Args"/> keeps the op's validated raw keys (lengths such as <c>size</c> stay in screenshot pixels);
/// for <c>path</c>, <c>d</c> is reduced to its command letters, with M/L taking one point, Q two and C three.
/// Edits carry their values in Args (<c>dx dy</c>, <c>x y</c>, <c>factor</c>) and their target in <see cref="Id"/>.
/// <see cref="Offset"/> is the tag's start index in the raw reply.
/// </summary>
public sealed record DrawOp(
    DrawOpKind Kind,
    string? Id,
    IReadOnlyList<Pt> Points,
    Box Box,
    string? Text,
    string? Color,
    int? Screen,
    IReadOnlyDictionary<string, string> Args,
    int Offset,
    FigureSpec? Figure = null)
{
    public bool IsEdit => Kind >= DrawOpKind.Recolor;
}
