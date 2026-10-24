using System.Numerics;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class FigureBlockParserTests
{
    private const string Gear = """{"kind":"gear","size":[1,0.2,1],"at":[0,0,0],"rotate":[0,0,0],"color":"#3380ff","label":"drive gear","teeth":12}""";

    private static string Block(string parts, string extra = "") =>
        "```figure\n{\"id\":\"f1\",\"box\":[100,80,300,240],\"screen\":1,\"spin\":true,\"title\":\"gear train\"" + extra + ",\"parts\":[" + parts + "]}\n```";

    [Fact]
    public void ParsesBlockAndStripsIt()
    {
        var (text, figures) = FigureBlockParser.Parse("here is the gear. " + Block(Gear) + "\nit turns the shaft.");
        Assert.Equal("here is the gear. it turns the shaft.", text);
        var figure = Assert.Single(figures);
        Assert.Equal(("f1", new Box(100, 80, 300, 240), (int?)1, true, "gear train"), (figure.Id, figure.Box, figure.Screen, figure.Spin, figure.Title));
        var part = Assert.Single(figure.Parts);
        Assert.Equal(FigureKind.Gear, part.Kind);
        Assert.Equal(new Vector3(1, 0.2f, 1), part.Size);
        Assert.Equal("#3380FF", part.Color);
        Assert.Equal("drive gear", part.Label);
        Assert.Equal(12, part.Params["teeth"]);

        var op = FigureBlockParser.ToOp(figure);
        Assert.Equal((DrawOpKind.Figure, "f1", "gear train", (int?)1), (op.Kind, op.Id, op.Text, op.Screen));
        Assert.Same(figure, op.Figure);
    }

    [Fact]
    public void TooManyPartsDropped()
    {
        var parts = string.Join(",", Enumerable.Repeat("""{"kind":"box"}""", 13));
        var figure = Assert.Single(FigureBlockParser.Parse(Block(parts)).Figures);
        Assert.Equal(FigureBlockParser.MaxParts, figure.Parts.Count);

        // A third figure is dropped (and stripped) too.
        var three = string.Join("\n", Enumerable.Repeat(Block(Gear), 3));
        var (text, figures) = FigureBlockParser.Parse("look. " + three);
        Assert.Equal(FigureBlockParser.MaxFigures, figures.Count);
        Assert.Equal("look.", text);
    }

    [Fact]
    public void UnknownKindDropped()
    {
        var figure = Assert.Single(FigureBlockParser.Parse(Block("""{"kind":"teapot"},{"kind":"Sphere"}""")).Figures);
        Assert.Equal(FigureKind.Sphere, Assert.Single(figure.Parts).Kind);
        // Out-of-range and malformed values drop their part; a figure left with no parts is dropped.
        Assert.Empty(FigureBlockParser.Parse(Block("""{"kind":"box","size":[1,1,1e9]},{"kind":"box","at":[0,"x",0]},{"kind":"box","color":"red"},{"kind":"cone","size":[0,1,1]},{"kind":"tube","profile":[[0,0],[1,1e9]]},{"kind":"box","label":"a‮b"}""")).Figures);
        // A box outside the allowed range drops the figure.
        Assert.Empty(FigureBlockParser.Parse(Block(Gear).Replace("[100,80,300,240]", "[100,80,300,999999]")).Figures);
        Assert.Empty(FigureBlockParser.Parse(Block(Gear).Replace("[100,80,300,240]", "[-5,80,300,240]")).Figures);
    }

    [Fact]
    public void MalformedJsonKeepsText()
    {
        var (text, figures) = FigureBlockParser.Parse("before. ```figure\n{\"id\":\"f1\",\"box\":[1,2\n``` after.");
        Assert.Empty(figures);
        Assert.Equal("before. after.", text);

        // An unterminated block (a reply cut off, or one still streaming) is stripped to the end, never spoken.
        Assert.Equal("before.", FigureBlockParser.Parse("before. ```figure\n{\"id\":\"f1\",\"box\":[1,2").Text);
        Assert.Equal("before.", FigureBlockParser.Strip("before. ```figure\n{\"title\":\"a. b. c\""));
        Assert.Equal("before.", FigureBlockParser.Strip("before. ```fig"));
        Assert.Equal("before. ```python", FigureBlockParser.Strip("before. ```python"));
        Assert.Equal("a ```ag-ui\n{}\n```", FigureBlockParser.Strip("a ```ag-ui\n{}\n```")); // another block's closing fence stays
        Assert.Equal("no block here.", FigureBlockParser.Strip("no block here."));
        Assert.Equal("one. two.", StreamingReplySpeech.Clean("one. " + Block(Gear) + " two."));
        Assert.Equal("one.", StreamingReplySpeech.Clean("one. ```figure\n{\"box\":[1,2,3"));
    }

    [Fact]
    public void TriangleBudgetEnforced()
    {
        // A 12-coil spring is 4,608 triangles: four fit in 20,000, five do not.
        var spring = """{"kind":"spring","coils":12}""";
        Assert.Single(FigureBlockParser.Parse(Block(string.Join(",", Enumerable.Repeat(spring, 4)))).Figures);
        var (text, figures) = FigureBlockParser.Parse("springs. " + Block(string.Join(",", Enumerable.Repeat(spring, 5))));
        Assert.Empty(figures);
        Assert.Equal("springs.", text);
    }
}
