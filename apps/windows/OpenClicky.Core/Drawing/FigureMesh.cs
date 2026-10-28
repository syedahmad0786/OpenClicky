using System.Numerics;

namespace OpenClicky.Core.Drawing;

/// <summary>A triangle mesh: <see cref="Indices"/> in threes, counter-clockwise seen from outside; one unit normal per position.</summary>
public sealed record Mesh(IReadOnlyList<Vector3> Positions, IReadOnlyList<Vector3> Normals, IReadOnlyList<int> Indices)
{
    public int TriangleCount => Indices.Count / 3;
}

/// <summary>
/// Builds each figure kind as a mesh whose bounding box is exactly the part's <see cref="FigurePart.Size"/>, centred on the
/// origin (Y is up: cylinders, cones, gears and arrows stand on the Y axis). Segment counts are fixed per kind and every model
/// number is clamped here, so a part can never make a huge mesh.
/// </summary>
public static class FigureMesh
{
    public const int DefaultTeeth = 12, MinTeeth = 6, MaxTeeth = 40;
    public const int DefaultCoils = 5, MinCoils = 1, MaxCoils = 12;
    public const int MaxProfilePoints = 24;

    private const int Around = 32, SphereStacks = 16, TorusAround = 16, CapRows = 8, TubeAround = 12, CoilSegments = 24, SpringAround = 8;
    private const float Tau = MathF.PI * 2;

    public static Mesh Build(FigurePart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var b = new Builder();
        switch (part.Kind)
        {
            case FigureKind.Box: b.Cube(); break;
            case FigureKind.Sphere:
                b.Revolve(Enumerable.Range(0, SphereStacks + 1).Select(j => Arc(-MathF.PI / 2 + MathF.PI * j / SphereStacks, 0)).ToArray(), Around);
                break;
            case FigureKind.Cylinder: b.Cylinder(0.5f, -0.5f, 0.5f, bottomCap: true, topCap: true); break;
            case FigureKind.Cone: b.Cone(0.5f, -0.5f, 0.5f); break;
            case FigureKind.Torus:
                b.Revolve(Enumerable.Range(0, TorusAround).Select(k =>
                {
                    var t = Tau * k / TorusAround;
                    return (1 + 0.3f * MathF.Cos(t), 0.3f * MathF.Sin(t), new Vector2(MathF.Cos(t), MathF.Sin(t)));
                }).ToArray(), Around, closed: true);
                break;
            case FigureKind.Capsule:
                // Two hemispheres of eight rows each, joined by the band between their equators.
                b.Revolve(Enumerable.Range(0, CapRows + 1).Select(k => Arc(-MathF.PI / 2 + MathF.PI / 2 * k / CapRows, -0.5f))
                    .Concat(Enumerable.Range(0, CapRows + 1).Select(k => Arc(MathF.PI / 2 * k / CapRows, 0.5f))).ToArray(), Around);
                break;
            case FigureKind.Tube:
            {
                IReadOnlyList<Vector2> path = Clean(part.Profile) is { Count: >= 2 } given ? given
                    : Enumerable.Range(0, 13).Select(k => new Vector2(MathF.Cos(MathF.PI * k / 12), MathF.Sin(MathF.PI * k / 12))).ToList();
                var radius = Clamp(Param(part, "radius", 0.1), 0.01, 1);
                b.Sweep(path.Select(p => new Vector3(p, 0)).ToArray(), Vector3.UnitZ, radius, TubeAround);
                break;
            }
            case FigureKind.Lathe: b.Lathe(Clean(part.Profile)); break;
            case FigureKind.Extrude: b.Prism(Outline(part.Profile), turnUp: false); break;
            case FigureKind.Gear: b.Prism(GearOutline((int)Math.Round(Clamp(Param(part, "teeth", DefaultTeeth), MinTeeth, MaxTeeth))), turnUp: true); break;
            case FigureKind.Spring:
            {
                var coils = (int)Math.Round(Clamp(Param(part, "coils", DefaultCoils), MinCoils, MaxCoils));
                var steps = coils * CoilSegments;
                var helix = Enumerable.Range(0, steps + 1).Select(k =>
                {
                    var u = Tau * k / CoilSegments;
                    return new Vector3(MathF.Cos(u), 0.5f * coils * ((float)k / steps - 0.5f), MathF.Sin(u));
                }).ToArray();
                b.Sweep(helix, Vector3.UnitY, 0.12f, SpringAround);
                break;
            }
            case FigureKind.Arrow:
                b.Cylinder(0.08f, -0.5f, 0.2f, bottomCap: true, topCap: false); // the head's base covers the shaft's top
                b.Cone(0.2f, 0.2f, 0.5f);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(part), part.Kind, "Unknown figure kind.");
        }
        return b.Fit(part.Size);
    }

    /// <summary>Where the part sits in the figure: turned about X, then Y, then Z (degrees), then moved to <see cref="FigurePart.At"/>.</summary>
    public static Matrix4x4 Placement(FigurePart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        static float Rad(float degrees) => float.IsFinite(degrees) ? degrees * MathF.PI / 180 : 0;
        var r = part.RotateDegrees;
        return Matrix4x4.CreateRotationX(Rad(r.X)) * Matrix4x4.CreateRotationY(Rad(r.Y)) * Matrix4x4.CreateRotationZ(Rad(r.Z))
            * Matrix4x4.CreateTranslation(Finite(part.At));
    }

    private static Vector3 Finite(Vector3 v) => new(float.IsFinite(v.X) ? v.X : 0, float.IsFinite(v.Y) ? v.Y : 0, float.IsFinite(v.Z) ? v.Z : 0);

    /// <summary>A row of a sphere of radius 0.5 at <paramref name="latitude"/>, raised by <paramref name="y"/>; the poles have radius 0.</summary>
    private static (float R, float Y, Vector2 N) Arc(float latitude, float y)
    {
        var pole = MathF.Abs(MathF.Abs(latitude) - MathF.PI / 2) < 1e-5f;
        return (pole ? 0 : 0.5f * MathF.Cos(latitude), y + 0.5f * MathF.Sin(latitude), new Vector2(MathF.Cos(latitude), MathF.Sin(latitude)));
    }

    private static double Param(FigurePart part, string key, double fallback) =>
        part.Params is not null && part.Params.TryGetValue(key, out var v) && double.IsFinite(v) ? v : fallback;

    private static float Clamp(double v, double min, double max) => (float)Math.Clamp(v, min, max);

    /// <summary>The first <see cref="MaxProfilePoints"/> finite points with repeats dropped; null when none were given.</summary>
    private static List<Vector2>? Clean(IReadOnlyList<Vector2>? points)
    {
        if (points is null) return null;
        var kept = new List<Vector2>();
        foreach (var p in points.Take(MaxProfilePoints))
            if (float.IsFinite(p.X) && float.IsFinite(p.Y) && (kept.Count == 0 || Vector2.DistanceSquared(kept[^1], p) > 1e-10f)) kept.Add(p);
        return kept;
    }

    /// <summary>A counter-clockwise outline: the given one (reversed if clockwise), else a five-pointed star.</summary>
    private static IReadOnlyList<Vector2> Outline(IReadOnlyList<Vector2>? given)
    {
        var points = Clean(given);
        if (points is { Count: > 1 } && Vector2.DistanceSquared(points[0], points[^1]) <= 1e-10f) points.RemoveAt(points.Count - 1);
        if (points is { Count: >= 3 })
        {
            var area = SignedArea(points);
            if (MathF.Abs(area) > 1e-6f)
            {
                if (area < 0) points.Reverse();
                return points;
            }
        }
        return Enumerable.Range(0, 10).Select(k =>
        {
            var a = MathF.PI / 2 + Tau * k / 10;
            var r = k % 2 == 0 ? 1 : 0.45f;
            return new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
        }).ToArray();
    }

    /// <summary>Four points a tooth (root, tip, tip, root) around a root circle of 0.8 and a tip circle of 1.</summary>
    private static IReadOnlyList<Vector2> GearOutline(int teeth)
    {
        var points = new List<Vector2>(teeth * 4);
        var step = Tau / teeth;
        foreach (var k in Enumerable.Range(0, teeth))
            foreach (var (radius, at) in new[] { (0.8f, 0f), (1f, 0.15f), (1f, 0.45f), (0.8f, 0.6f) })
            {
                var a = step * (k + at);
                points.Add(new Vector2(radius * MathF.Cos(a), radius * MathF.Sin(a)));
            }
        return points;
    }

    private static float SignedArea(IReadOnlyList<Vector2> p)
    {
        var sum = 0f;
        for (var i = 0; i < p.Count; i++) sum += Cross(p[i], p[(i + 1) % p.Count]);
        return sum / 2;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>
    /// Ear clipping of a counter-clockwise polygon. A self-crossing outline that runs out of ears keeps the triangles found so far.
    /// </summary>
    internal static List<(int A, int B, int C)> Triangulate(IReadOnlyList<Vector2> poly)
    {
        var left = Enumerable.Range(0, poly.Count).ToList();
        var triangles = new List<(int, int, int)>(poly.Count);
        while (left.Count > 3)
        {
            var clipped = false;
            for (var k = 0; k < left.Count && !clipped; k++)
            {
                int a = left[(k + left.Count - 1) % left.Count], b = left[k], c = left[(k + 1) % left.Count];
                if (Cross(poly[b] - poly[a], poly[c] - poly[b]) <= 1e-9f) continue; // reflex or flat corner
                if (left.Any(m => m != a && m != b && m != c && Inside(poly[m], poly[a], poly[b], poly[c]))) continue;
                triangles.Add((a, b, c));
                left.RemoveAt(k);
                clipped = true;
            }
            if (!clipped) return triangles;
        }
        if (left.Count == 3) triangles.Add((left[0], left[1], left[2]));
        return triangles;

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0;
    }

    private sealed class Builder
    {
        private readonly List<Vector3> _p = [], _n = [];
        private readonly List<int> _i = [];

        private int V(Vector3 p, Vector3 n)
        {
            _p.Add(p);
            _n.Add(n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY);
            return _p.Count - 1;
        }

        private void Tri(int a, int b, int c) { _i.Add(a); _i.Add(b); _i.Add(c); }

        /// <summary>A flat quad a-b-c-d, counter-clockwise seen from the side it faces.</summary>
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var n = Vector3.Cross(b - a, c - a);
            int ia = V(a, n), ib = V(b, n), ic = V(c, n), id = V(d, n);
            Tri(ia, ib, ic);
            Tri(ia, ic, id);
        }

        public void Cube()
        {
            const float h = 0.5f;
            foreach (var (n, u, v) in new[]
            {
                (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
                (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
                (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
            })
                Quad((n - u - v) * h, (n + u - v) * h, (n + u + v) * h, (n - u + v) * h);
        }

        /// <summary>
        /// Turns profile rows (radius, height, outward 2D normal), bottom to top, about the Y axis. A row of radius 0 is a single
        /// pole vertex. <paramref name="closed"/> joins the last row back to the first (a torus).
        /// </summary>
        public void Revolve(IReadOnlyList<(float R, float Y, Vector2 N)> rows, int around, bool closed = false)
        {
            var rings = new int[rows.Count][];
            for (var k = 0; k < rows.Count; k++)
            {
                var (r, y, n) = rows[k];
                if (!closed && r <= 1e-6f) { rings[k] = [V(new(0, y, 0), new(0, n.Y < 0 ? -1 : 1, 0))]; continue; }
                rings[k] = new int[around];
                for (var i = 0; i < around; i++)
                {
                    float c = MathF.Cos(Tau * i / around), s = MathF.Sin(Tau * i / around);
                    rings[k][i] = V(new(r * c, y, r * s), new(n.X * c, n.Y, n.X * s));
                }
            }
            for (var k = 0; k < (closed ? rows.Count : rows.Count - 1); k++)
            {
                int[] lo = rings[k], up = rings[(k + 1) % rows.Count];
                if (lo.Length == 1 && up.Length == 1) continue;
                for (var i = 0; i < around; i++)
                {
                    var j = (i + 1) % around;
                    if (lo.Length == 1) Tri(lo[0], up[i], up[j]);
                    else if (up.Length == 1) Tri(lo[i], up[0], lo[j]);
                    else { Tri(lo[i], up[i], up[j]); Tri(lo[i], up[j], lo[j]); }
                }
            }
        }

        /// <summary>A flat disc of radius <paramref name="r"/> at height <paramref name="y"/>, facing up or down.</summary>
        private void Cap(float r, float y, bool up)
        {
            var normal = up ? Vector3.UnitY : -Vector3.UnitY;
            var centre = V(new(0, y, 0), normal);
            var ring = Enumerable.Range(0, Around).Select(i => V(new(r * MathF.Cos(Tau * i / Around), y, r * MathF.Sin(Tau * i / Around)), normal)).ToArray();
            for (var i = 0; i < Around; i++)
            {
                var j = (i + 1) % Around;
                if (up) Tri(centre, ring[j], ring[i]); else Tri(centre, ring[i], ring[j]);
            }
        }

        public void Cylinder(float r, float bottom, float top, bool bottomCap, bool topCap)
        {
            Revolve([(r, bottom, Vector2.UnitX), (r, top, Vector2.UnitX)], Around);
            if (bottomCap) Cap(r, bottom, up: false);
            if (topCap) Cap(r, top, up: true);
        }

        public void Cone(float r, float bottom, float top)
        {
            Revolve([(r, bottom, Vector2.Normalize(new(top - bottom, r))), (0, top, Vector2.UnitY)], Around);
            Cap(r, bottom, up: false);
        }

        /// <summary>A (radius, height) outline turned 32 times about Y; a vase when none is given.</summary>
        public void Lathe(List<Vector2>? profile)
        {
            if (profile is not { Count: >= 2 }) profile = [new(0, 0), new(0.5f, 0), new(0.6f, 0.3f), new(0.35f, 0.7f), new(0.45f, 1), new(0, 1)];
            profile = profile.Select(p => new Vector2(MathF.Max(0, p.X), p.Y)).ToList();
            if (profile[^1].Y < profile[0].Y) profile.Reverse(); // bottom to top, so the faces look outward
            var rows = new (float, float, Vector2)[profile.Count];
            for (var k = 0; k < profile.Count; k++)
            {
                var t = profile[Math.Min(k + 1, profile.Count - 1)] - profile[Math.Max(k - 1, 0)];
                var n = new Vector2(t.Y, -t.X);
                rows[k] = (profile[k].X, profile[k].Y, n.LengthSquared() > 1e-12f ? Vector2.Normalize(n) : Vector2.UnitX);
            }
            Revolve(rows, Around);
        }

        /// <summary>
        /// A circle of <paramref name="around"/> points swept along <paramref name="path"/> (open ends). <paramref name="hint"/>
        /// must never lie along the path: Z for a path in the XY plane, Y for a helix about Y.
        /// </summary>
        public void Sweep(IReadOnlyList<Vector3> path, Vector3 hint, float radius, int around)
        {
            var rings = new int[path.Count][];
            for (var k = 0; k < path.Count; k++)
            {
                var t = path[Math.Min(k + 1, path.Count - 1)] - path[Math.Max(k - 1, 0)];
                if (t.LengthSquared() < 1e-12f) t = k + 1 < path.Count ? path[k + 1] - path[k] : path[k] - path[k - 1]; // a path that doubles back
                t = t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : Vector3.UnitX;
                var side = Vector3.Cross(t, hint);
                side = side.LengthSquared() > 1e-12f ? Vector3.Normalize(side) : Vector3.UnitX;
                var other = Vector3.Cross(side, t);
                rings[k] = new int[around];
                for (var i = 0; i < around; i++)
                {
                    var o = MathF.Cos(Tau * i / around) * side + MathF.Sin(Tau * i / around) * other;
                    rings[k][i] = V(path[k] + radius * o, o);
                }
            }
            for (var k = 0; k + 1 < path.Count; k++)
                for (var i = 0; i < around; i++)
                {
                    var j = (i + 1) % around;
                    Tri(rings[k][i], rings[k + 1][i], rings[k + 1][j]);
                    Tri(rings[k][i], rings[k + 1][j], rings[k][j]);
                }
        }

        /// <summary>
        /// A counter-clockwise outline in XY made one unit deep along Z. <paramref name="turnUp"/> stands its depth on the Y axis
        /// instead (a gear lying flat): turned 90° about X.
        /// </summary>
        public void Prism(IReadOnlyList<Vector2> outline, bool turnUp)
        {
            var first = _p.Count;
            const float h = 0.5f;
            var front = outline.Select(p => V(new(p, h), Vector3.UnitZ)).ToArray();
            var back = outline.Select(p => V(new(p, -h), -Vector3.UnitZ)).ToArray();
            foreach (var (a, b, c) in Triangulate(outline))
            {
                Tri(front[a], front[b], front[c]);
                Tri(back[a], back[c], back[b]);
            }
            for (var i = 0; i < outline.Count; i++)
            {
                Vector2 p = outline[i], q = outline[(i + 1) % outline.Count];
                Quad(new(p, -h), new(q, -h), new(q, h), new(p, h));
            }
            if (!turnUp) return;
            for (var i = first; i < _p.Count; i++)
            {
                _p[i] = new(_p[i].X, -_p[i].Z, _p[i].Y);
                _n[i] = new(_n[i].X, -_n[i].Z, _n[i].Y);
            }
        }

        /// <summary>Scales and centres the mesh so its bounding box is <paramref name="size"/> (normals follow the inverse scale).</summary>
        public Mesh Fit(Vector3 size)
        {
            static float Target(float v) => float.IsFinite(v) ? Math.Clamp(v, 1e-3f, 1e4f) : 1;
            var target = new Vector3(Target(size.X), Target(size.Y), Target(size.Z));
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var p in _p) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var extent = max - min;
            var scale = new Vector3(extent.X > 1e-6f ? target.X / extent.X : 1, extent.Y > 1e-6f ? target.Y / extent.Y : 1, extent.Z > 1e-6f ? target.Z / extent.Z : 1);
            var centre = (min + max) / 2;
            var positions = _p.Select(p => (p - centre) * scale).ToArray();
            var normals = _n.Select(n => Vector3.Normalize(n / scale)).ToArray();
            return new Mesh(positions, normals, _i.ToArray());
        }
    }
}
