using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class JobColoursTests
{
    [Fact]
    public void EmptySetGivesRed() => Assert.Equal("red", JobColours.LeastUsed([]));

    [Fact]
    public void RedTakenGivesBlue() => Assert.Equal("blue", JobColours.LeastUsed(["red"]));

    [Fact]
    public void TiesBreakInPaletteOrder() => Assert.Equal("blue", JobColours.LeastUsed(["red", "red", "blue", "yellow", "green"]));

    [Fact]
    public void UnknownStringsAreIgnored() => Assert.Equal("blue", JobColours.LeastUsed(["purple", "", "RED!", "red"]));

    [Fact]
    public void FromIdIsStableAndInPalette()
    {
        Assert.Equal(JobColours.FromId("abc"), JobColours.FromId("abc"));
        Assert.Contains(JobColours.FromId("abc"), JobColours.All);
    }
}
