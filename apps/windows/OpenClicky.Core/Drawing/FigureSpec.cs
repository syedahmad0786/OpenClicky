using System.Numerics;

namespace OpenClicky.Core.Drawing;

public enum FigureKind { Box, Sphere, Cylinder, Cone, Torus, Capsule, Tube, Lathe, Extrude, Gear, Spring, Arrow }

/// <summary>
/// One solid of a 3D figure, in the figure's own units. <see cref="Size"/> is the part's bounding box before it is turned
/// (<see cref="RotateDegrees"/>, about X then Y then Z) and moved to <see cref="At"/>. <see cref="Color"/> is <c>#RRGGBB</c>, or
/// empty for the user's accent. <see cref="Params"/> holds numeric extras (gear <c>teeth</c>, spring <c>coils</c>, tube
/// <c>radius</c>); <see cref="Profile"/> is the tube's path, the lathe's (radius, height) outline or the extrude's outline.
/// </summary>
public sealed record FigurePart(
    FigureKind Kind,
    Vector3 Size,
    Vector3 At,
    Vector3 RotateDegrees,
    string Color,
    string? Label,
    IReadOnlyDictionary<string, double> Params,
    IReadOnlyList<Vector2>? Profile);

/// <summary>A 3D figure floating over <see cref="Box"/> (screenshot pixels until mapped, like any draw op).</summary>
public sealed record FigureSpec(string Id, Box Box, int? Screen, bool Spin, string? Title, IReadOnlyList<FigurePart> Parts);
