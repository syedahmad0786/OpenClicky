using System.Numerics;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class FigureMeshTests
{
    public static TheoryData<FigureKind> AllKinds => new(Enum.GetValues<FigureKind>());

    public static TheoryData<FigureKind> ClosedKinds => new(FigureKind.Box, FigureKind.Sphere, FigureKind.Cylinder, FigureKind.Cone, FigureKind.Torus, FigureKind.Capsule);

    private static FigurePart Part(FigureKind kind, Vector3? size = null, Dictionary<string, double>? numbers = null) =>
        new(kind, size ?? new Vector3(1.2f, 0.7f, 2), Vector3.Zero, Vector3.Zero, "", null, numbers ?? [], null);

    [Theory, MemberData(nameof(AllKinds))]
    public void IndicesInRangeAndTriangles(FigureKind kind)
    {
        var mesh = FigureMesh.Build(Part(kind));
        Assert.NotEmpty(mesh.Indices);
        Assert.Equal(0, mesh.Indices.Count % 3);
        Assert.Equal(mesh.Positions.Count, mesh.Normals.Count);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Positions.Count - 1));
        Assert.All(mesh.Positions, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)));
    }

    /// <summary>Faces keep their own vertices (flat normals), so edges are matched by position: each must border exactly two triangles.</summary>
    [Theory, MemberData(nameof(ClosedKinds))]
    public void ClosedSolidsWatertight(FigureKind kind)
    {
        var mesh = FigureMesh.Build(Part(kind));
        var weld = new Dictionary<(int, int, int), int>();
        int Key(int index)
        {
            var p = mesh.Positions[index];
            var key = ((int)MathF.Round(p.X * 1e4f), (int)MathF.Round(p.Y * 1e4f), (int)MathF.Round(p.Z * 1e4f));
            if (!weld.TryGetValue(key, out var id)) weld[key] = id = weld.Count;
            return id;
        }
        var edges = new Dictionary<(int, int), int>();
        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            int a = Key(mesh.Indices[t]), b = Key(mesh.Indices[t + 1]), c = Key(mesh.Indices[t + 2]);
            Assert.True(a != b && b != c && a != c, $"degenerate triangle {t / 3}");
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                var edge = (Math.Min(u, v), Math.Max(u, v));
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
            }
        }
        Assert.All(edges, e => Assert.Equal(2, e.Value));
    }

    [Theory, MemberData(nameof(AllKinds))]
    public void NormalsUnitLength(FigureKind kind)
    {
        var mesh = FigureMesh.Build(Part(kind));
        Assert.All(mesh.Normals, n => Assert.InRange(n.Length(), 0.999f, 1.001f));
    }

    [Theory, MemberData(nameof(AllKinds))]
    public void BoundsMatchSizeWithin1Percent(FigureKind kind)
    {
        var size = new Vector3(1.2f, 0.7f, 2);
        var bounds = Box3.Of(FigureMesh.Build(Part(kind, size)).Positions);
        var extent = bounds.Max - bounds.Min;
        Assert.InRange(extent.X, size.X * 0.99f, size.X * 1.01f);
        Assert.InRange(extent.Y, size.Y * 0.99f, size.Y * 1.01f);
        Assert.InRange(extent.Z, size.Z * 0.99f, size.Z * 1.01f);
        Assert.True(bounds.Centre.Length() < 0.01f, $"centre {bounds.Centre}");
    }

    [Theory]
    [InlineData(null, 12)]
    [InlineData(6.0, 6)]
    [InlineData(17.0, 17)]
    [InlineData(40.0, 40)]
    [InlineData(500.0, 40)] // clamped
    [InlineData(1.0, 6)]
    public void GearHasTeeth(double? teeth, int expected)
    {
        var mesh = FigureMesh.Build(Part(FigureKind.Gear, new Vector3(1, 0.2f, 1), teeth is { } t ? new() { ["teeth"] = t } : null));
        // The gear lies flat (its axis is Y): the tooth tips are the vertices farthest from the axis, in one cluster per tooth.
        var radii = mesh.Positions.Select(p => (Angle: MathF.Atan2(p.Z, p.X), Radius: MathF.Sqrt(p.X * p.X + p.Z * p.Z))).ToArray();
        var max = radii.Max(r => r.Radius);
        var tips = radii.Where(r => r.Radius > 0.95f * max).Select(r => r.Angle).Order().ToArray();
        var gap = MathF.PI / expected; // half a tooth: wider than a tip, narrower than the space between tips
        var clusters = Enumerable.Range(0, tips.Length).Count(i => (i + 1 < tips.Length ? tips[i + 1] : tips[0] + MathF.PI * 2) - tips[i] > gap);
        Assert.Equal(expected, clusters);
    }

    [Fact]
    public void SpringAndTubeStayBounded()
    {
        var spring = FigureMesh.Build(Part(FigureKind.Spring, numbers: new() { ["coils"] = 1e9 }));
        Assert.Equal(12 * 24 * 8 * 2, spring.TriangleCount); // coils clamped to 12; 24 segments a coil, 8 around
        var path = Enumerable.Range(0, 200).Select(i => new Vector2(i, i % 2)).ToArray();
        var tube = FigureMesh.Build(Part(FigureKind.Tube) with { Profile = path });
        Assert.Equal(23 * 12 * 2, tube.TriangleCount); // only the first 24 points are used
    }
}
