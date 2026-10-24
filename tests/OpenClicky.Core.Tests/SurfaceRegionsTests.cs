using System.Text.Json;
using OpenClicky.Core.Surfaces;

namespace OpenClicky.Core.Tests;

public class SurfaceRegionsTests
{
    private static IReadOnlyList<CssRegionRect> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return SurfaceRegions.Parse(document.RootElement);
    }

    [Fact]
    public void Parse_KeepsValidRectsAndDropsMalformedOnes()
    {
        var rects = Parse("""
            {"kind":"surface.regions","rects":[
              {"x":10,"y":20,"w":30,"h":40,"r":8},
              {"x":1,"y":2,"w":3,"h":4},
              {"x":"1","y":2,"w":3,"h":4},
              {"x":1,"y":2,"w":0,"h":4},
              {"x":1,"y":2,"w":-5,"h":4},
              {"x":1e9,"y":2,"w":3,"h":4},
              {"x":1,"y":2,"w":3,"h":4,"r":-2},
              7
            ]}
            """);
        Assert.Equal(
            [new CssRegionRect(10, 20, 30, 40, 8), new CssRegionRect(1, 2, 3, 4, 0), new CssRegionRect(1, 2, 3, 4, 0)],
            rects);
    }

    [Theory]
    [InlineData("""{"kind":"surface.regions"}""")]
    [InlineData("""{"kind":"surface.regions","rects":{}}""")]
    [InlineData("""[]""")]
    public void Parse_MissingOrWrongShape_IsEmpty(string json) => Assert.Empty(Parse(json));

    [Fact]
    public void Parse_CapsRectCount()
    {
        var items = string.Join(",", Enumerable.Repeat("""{"x":0,"y":0,"w":1,"h":1}""", SurfaceRegions.MaxRects + 10));
        Assert.Equal(SurfaceRegions.MaxRects, Parse($$"""{"rects":[{{items}}]}""").Count);
    }

    [Fact]
    public void ToDevice_ScalesGrowsOutwardAndCapsRadius()
    {
        var device = SurfaceRegions.ToDevice([new CssRegionRect(10.2, 20.6, 100.1, 30, 50)], 1.5, 2000, 2000);
        // left floor(15.3)=15, top floor(30.9)=30, right ceil(165.45)=166, bottom ceil(75.9)=76, radius capped to 46/2
        Assert.Equal([new DeviceRegionRect(15, 30, 151, 46, 23)], device);
    }

    [Fact]
    public void ToDevice_ClampsToClientAndDropsOffscreen()
    {
        var device = SurfaceRegions.ToDevice(
            [new CssRegionRect(-10, -10, 30, 30, 0), new CssRegionRect(90, 90, 50, 50, 4), new CssRegionRect(500, 500, 10, 10, 0)],
            1, 100, 100);
        Assert.Equal([new DeviceRegionRect(0, 0, 20, 20, 0), new DeviceRegionRect(90, 90, 10, 10, 4)], device);
    }

    [Fact]
    public void ToDeviceScalesClampsAndCaps()
    {
        // Grow-outward rule: left/top floor, right/bottom ceil. (10,10,100,20) at 1.5 lands exactly on (15,15,150,30).
        var device = SurfaceRegions.ToDevice(
            [new CssRegionRect(10, 10, 100, 20, 0), new CssRegionRect(950, 0, 100, 10, 0)], 1.5, 1500, 1000);
        Assert.Equal([new DeviceRegionRect(15, 15, 150, 30, 0), new DeviceRegionRect(1425, 0, 75, 15, 0)], device);

        var items = string.Join(",", Enumerable.Repeat("""{"x":0,"y":0,"w":1,"h":1}""", 70));
        Assert.Equal(64, Parse($$"""{"rects":[{{items}}]}""").Count);
    }

    [Fact]
    public void ClampsToMonitorAfterDpiChange()
    {
        CssRegionRect[] css = [new(1200, 10, 300, 60, 18), new(-40, 700, 200, 200, 0), new(10, 10, 100, 20, 4)];
        const int width = 1920, height = 1080;
        foreach (var scale in new[] { 1.0, 2.0 })
        {
            var device = SurfaceRegions.ToDevice(css, scale, width, height);
            Assert.NotEmpty(device);
            Assert.All(device, r =>
            {
                Assert.InRange(r.X, 0, width - 1);
                Assert.InRange(r.Y, 0, height - 1);
                Assert.InRange(r.X + r.Width, 1, width);
                Assert.InRange(r.Y + r.Height, 1, height);
                Assert.InRange(r.Radius, 0, Math.Min(r.Width, r.Height) / 2);
            });
        }
    }

    [Fact]
    public void UnionArea_CountsOverlapOnce()
    {
        Assert.Equal(100 + 100 - 25, SurfaceRegions.UnionArea([new(0, 0, 10, 10, 0), new(5, 5, 10, 10, 0)]));
        Assert.Equal(100, SurfaceRegions.UnionArea([new(0, 0, 10, 10, 0), new(0, 0, 10, 10, 0)]));
        Assert.Equal(0, SurfaceRegions.UnionArea([]));
    }

    [Fact]
    public void ApplyAreaCap_DropsFromEndUntilUnder45Percent()
    {
        // Client 100x100 = 10,000 px; cap 4,500.
        DeviceRegionRect[] rects = [new(0, 0, 50, 50, 0), new(50, 0, 50, 30, 0), new(0, 50, 100, 50, 0)];
        Assert.Equal(rects[..2], SurfaceRegions.ApplyAreaCap(rects, 100, 100, allowLarge: false)); // 2,500 + 1,500 = 4,000
        Assert.Same(rects, SurfaceRegions.ApplyAreaCap(rects, 100, 100, allowLarge: true));
        Assert.Empty(SurfaceRegions.ApplyAreaCap([new(0, 0, 100, 100, 0)], 100, 100, allowLarge: false));
        // Overlap counts once: two identical 40x100 rects are 4,000, under the cap.
        Assert.Equal(2, SurfaceRegions.ApplyAreaCap([new(0, 0, 40, 100, 0), new(0, 0, 40, 100, 0)], 100, 100, false).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void ToDevice_RejectsInvalidScale(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceRegions.ToDevice([], scale, 10, 10));
}
