using System.Numerics;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class FigureCameraTests
{
    private static readonly Box3 Bounds = new(new Vector3(-1, 0, 2), new Vector3(3, 1, 4));

    [Fact]
    public void CentreProjectsToViewportCentre()
    {
        var cam = FigureCamera.Fit(Bounds);
        Assert.Equal(35, cam.FieldOfViewDegrees);
        Assert.Equal(Bounds.Centre, cam.Target);
        // From (1, 0.8, 1.4), at r / sin(17.5°) × 1.1.
        var offset = cam.Position - Bounds.Centre;
        Assert.True(Vector3.Distance(Vector3.Normalize(offset), Vector3.Normalize(new Vector3(1, 0.8f, 1.4f))) < 1e-5f);
        Assert.Equal(Bounds.Radius / MathF.Sin(17.5f * MathF.PI / 180) * 1.1f, offset.Length(), 3);

        var centre = FigureCamera.Project(Bounds.Centre, cam, 400, 300);
        Assert.NotNull(centre);
        Assert.Equal(200, centre.Value.X, 2);
        Assert.Equal(150, centre.Value.Y, 2);

        // Every corner of the box lands inside the picture (it would still while spinning: the sphere round it fits).
        foreach (var x in new[] { Bounds.Min.X, Bounds.Max.X })
        foreach (var y in new[] { Bounds.Min.Y, Bounds.Max.Y })
        foreach (var z in new[] { Bounds.Min.Z, Bounds.Max.Z })
        {
            var p = FigureCamera.Project(new Vector3(x, y, z), cam, 400, 300)!.Value;
            Assert.InRange(p.X, 0, 400);
            Assert.InRange(p.Y, 0, 300);
        }
        // Up in the world is up on screen.
        Assert.True(FigureCamera.Project(Bounds.Centre + Vector3.UnitY * 0.5f, cam, 400, 300)!.Value.Y < 150);
        // A wide viewport keeps 35° across its height; WPF takes the horizontal angle.
        Assert.Equal(35, FigureCamera.HorizontalFieldOfView(cam, 300, 400), 3);
        Assert.True(FigureCamera.HorizontalFieldOfView(cam, 400, 300) > 35);
    }

    [Fact]
    public void BehindCameraIsNull()
    {
        var cam = FigureCamera.Fit(Bounds);
        var back = Vector3.Normalize(cam.Position - cam.Target);
        Assert.Null(FigureCamera.Project(cam.Position + back, cam, 400, 300));
        Assert.Null(FigureCamera.Project(cam.Position, cam, 400, 300));
    }
}
