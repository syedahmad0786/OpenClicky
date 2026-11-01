using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

[Collection("OfflineEnv")]
public class OfflineTalkClientTests
{
    [Fact]
    public void IsEnvEnabled_FalseByDefault()
    {
        var previous = Environment.GetEnvironmentVariable(OfflineTalkClient.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, null);
            Assert.False(OfflineTalkClient.IsEnvEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, previous);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("YES")]
    public void IsEnvEnabled_RecognizesTruthyValues(string value)
    {
        var previous = Environment.GetEnvironmentVariable(OfflineTalkClient.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, value);
            Assert.True(OfflineTalkClient.IsEnvEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, previous);
        }
    }

    [Fact]
    public void CreateTalkClient_ReturnsOfflineWhenEnvSet_WithoutSecrets()
    {
        var previous = Environment.GetEnvironmentVariable(OfflineTalkClient.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, "1");
            var client = ProviderFactory.CreateTalkClient(new AppSettings(), secrets: null);
            Assert.IsType<OfflineTalkClient>(client);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, previous);
        }
    }

    [Fact]
    public void CreateTalkClient_NullWhenEnvUnsetAndNoKey()
    {
        var previous = Environment.GetEnvironmentVariable(OfflineTalkClient.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, null);
            var client = ProviderFactory.CreateTalkClient(new AppSettings(), secrets: null);
            Assert.Null(client);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineTalkClient.EnvVarName, previous);
        }
    }

    [Fact]
    public async Task CompleteVisionTalkAsync_ReturnsParseablePointNearCaptureCenter()
    {
        var screens = new[]
        {
            new ScreenCaptureResult(
                JpegBytes: [0xFF, 0xD8],
                PixelWidth: 1920,
                PixelHeight: 1080,
                MonitorLeft: 0,
                MonitorTop: 0,
                MonitorWidth: 1920,
                MonitorHeight: 1080,
                IsCursorMonitor: true,
                Label: "screen1"),
        };

        var client = new OfflineTalkClient();
        var raw = await client.CompleteVisionTalkAsync(
            "system",
            Array.Empty<(string, string)>(),
            "point at the center",
            screens,
            "offline",
            CancellationToken.None);

        var parsed = PointParser.Parse(raw);
        Assert.Equal(OfflineTalkClient.SpokenText, parsed.SpokenText);
        Assert.Equal(960, parsed.X);
        Assert.Equal(540, parsed.Y);
        Assert.Equal(OfflineTalkClient.PointLabel, parsed.ElementLabel);
        Assert.False(parsed.IsNone);

        var mapped = CoordinateMapper.ToVirtualScreen(parsed, screens);
        Assert.NotNull(mapped);
        Assert.Equal(960, mapped.Value.virtualX);
        Assert.Equal(540, mapped.Value.virtualY);
    }

    [Fact]
    public void BuildCannedReply_FallsBackWithoutScreens()
    {
        var raw = OfflineTalkClient.BuildCannedReply(Array.Empty<ScreenCaptureResult>());
        var parsed = PointParser.Parse(raw);
        Assert.Equal(960, parsed.X);
        Assert.Equal(540, parsed.Y);
    }
}
