using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class CoordinateMapperTests
{
    [Fact]
    public void ToVirtualScreen_MapsScreenshotPixelsToMonitor()
    {
        var screen = new ScreenCaptureResult(
            JpegBytes: [0xFF, 0xD8],
            PixelWidth: 1000,
            PixelHeight: 500,
            MonitorLeft: 100,
            MonitorTop: 200,
            MonitorWidth: 2000,
            MonitorHeight: 1000,
            IsCursorMonitor: true,
            Label: "primary");

        var point = new PointingParseResult("hi", 250, 125, "button", null, IsNone: false);
        var mapped = CoordinateMapper.ToVirtualScreen(point, [screen]);
        Assert.NotNull(mapped);
        Assert.Equal(100 + 500, mapped!.Value.virtualX);
        Assert.Equal(200 + 250, mapped.Value.virtualY);
    }

    [Fact]
    public void ToScreenshotRoundTrips()
    {
        ScreenCaptureResult[] screens =
        [
            new([0xff, 0xd8], 1000, 500, 0, 0, 2000, 1000, true, "screen 1 of 2"),
            new([0xff, 0xd8], 1280, 720, -1920, 100, 1920, 1080, false, "screen 2 of 2"),
        ];
        foreach (var (x, y) in new[] { (0, 0), (1999, 999), (731, 402), (-1920, 100), (-1, 1179), (-700, 640) })
        {
            var shot = CoordinateMapper.ToScreenshot(x, y, screens);
            Assert.NotNull(shot);
            var (screen, sx, sy) = shot.Value;
            Assert.Equal(x < 0 ? 2 : 1, screen);
            Assert.InRange(sx, 0, screens[screen - 1].PixelWidth - 1);
            Assert.InRange(sy, 0, screens[screen - 1].PixelHeight - 1);
            var back = CoordinateMapper.ToVirtualScreen(new("", sx, sy, null, screen, false), screens);
            Assert.NotNull(back);
            Assert.InRange(back.Value.virtualX, x - 2, x + 2); // one screenshot pixel is at most 2 monitor pixels here
            Assert.InRange(back.Value.virtualY, y - 2, y + 2);
        }
        Assert.Null(CoordinateMapper.ToScreenshot(2000, 0, screens)); // right edge is outside (half-open)
        Assert.Null(CoordinateMapper.ToScreenshot(-1920, 99, screens));
        Assert.Null(CoordinateMapper.ToScreenshot(5, 5, []));
    }

    [Fact]
    public void ToVirtualScreen_None_ReturnsNull()
    {
        var point = new PointingParseResult("hi", null, null, null, null, IsNone: true);
        Assert.Null(CoordinateMapper.ToVirtualScreen(point, []));
    }

    [Theory]
    [InlineData(-1, 5, null)]
    [InlineData(100, 5, null)]
    [InlineData(5, 50, null)]
    [InlineData(5, 5, 2)]
    public void ToVirtualScreen_RejectsTargetsOutsideCapturedImage(double x, double y, int? number)
    {
        var screen = new ScreenCaptureResult([0xff,0xd8],100,50,-200,0,200,100,true,"test");
        Assert.Null(CoordinateMapper.ToVirtualScreen(new("",x,y,null,number,false), [screen]));
    }
}
