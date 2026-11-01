using System.Numerics;

namespace OpenClicky.Core.Drawing;

/// <summary>An axis-aligned 3D box.</summary>
public readonly record struct Box3(Vector3 Min, Vector3 Max)
{
    public Vector3 Centre => (Min + Max) / 2;

    /// <summary>The radius of the sphere through the box's corners.</summary>
    public float Radius => Vector3.Distance(Min, Max) / 2;

    public static Box3 Of(IEnumerable<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var p in points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return min.X > max.X ? new(Vector3.Zero, Vector3.Zero) : new(min, max);
    }
}

/// <summary>A perspective camera. <see cref="FieldOfViewDegrees"/> spans the shorter side of the viewport.</summary>
public readonly record struct CameraSpec(Vector3 Position, Vector3 Target, Vector3 Up, float FieldOfViewDegrees);

/// <summary>Frames a figure from above and to the right, and projects points into the picture (for the label pills).</summary>
public static class FigureCamera
{
    public const float FieldOfViewDegrees = 35;
    private static readonly Vector3 ViewDirection = Vector3.Normalize(new(1, 0.8f, 1.4f));

    /// <summary>
    /// Looks at the box's centre from direction (1, 0.8, 1.4), far enough that the sphere round the box fits the field of view
    /// with 10 % to spare (so it still fits while the figure spins about Y).
    /// </summary>
    public static CameraSpec Fit(Box3 bounds)
    {
        var radius = MathF.Max(bounds.Radius, 1e-3f);
        var distance = radius / MathF.Sin(FieldOfViewDegrees / 2 * MathF.PI / 180) * 1.1f;
        return new(bounds.Centre + ViewDirection * distance, bounds.Centre, Vector3.UnitY, FieldOfViewDegrees);
    }

    /// <summary>Where <paramref name="world"/> lands in a <paramref name="width"/> × <paramref name="height"/> picture (top-left origin); null when it is behind the camera.</summary>
    public static Pt? Project(Vector3 world, CameraSpec cam, float width, float height)
    {
        var forward = Vector3.Normalize(cam.Target - cam.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, cam.Up));
        var up = Vector3.Cross(right, forward);
        var v = world - cam.Position;
        var depth = Vector3.Dot(v, forward);
        if (!(depth > 1e-6f)) return null;
        var focal = MathF.Min(width, height) / 2 / MathF.Tan(cam.FieldOfViewDegrees / 2 * MathF.PI / 180);
        return new Pt(width / 2 + Vector3.Dot(v, right) / depth * focal, height / 2 - Vector3.Dot(v, up) / depth * focal);
    }

    /// <summary>The horizontal field of view (degrees) that gives the same picture, for cameras that take that (WPF's).</summary>
    public static double HorizontalFieldOfView(CameraSpec cam, double width, double height)
    {
        var shorter = Math.Min(width, height);
        if (!(shorter > 0)) return cam.FieldOfViewDegrees;
        return 2 * Math.Atan(Math.Tan(cam.FieldOfViewDegrees / 2 * Math.PI / 180) * width / shorter) * 180 / Math.PI;
    }
}
