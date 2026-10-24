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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void ToDevice_RejectsInvalidScale(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceRegions.ToDevice([], scale, 10, 10));
}
