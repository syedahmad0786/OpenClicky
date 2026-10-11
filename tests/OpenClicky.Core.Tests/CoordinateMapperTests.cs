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
    public void ToVirtualScreen_None_ReturnsNull()
    {
        var point = new PointingParseResult("hi", null, null, null, null, IsNone: true);
        Assert.Null(CoordinateMapper.ToVirtualScreen(point, []));
    }
}
