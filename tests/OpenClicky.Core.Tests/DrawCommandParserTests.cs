using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class DrawCommandParserTests
{
    [Theory]
    [InlineData("line pts=1,2,30,40 style=marker", DrawOpKind.Line)]
    [InlineData("arrow pts=1,2,30,40", DrawOpKind.Arrow)]
    [InlineData("curve pts=1,2,10,20,30,20,40,2", DrawOpKind.Curve)]
    [InlineData("poly pts=1,1,20,1,10,20 closed=1", DrawOpKind.Poly)]
    [InlineData("path d=\"M10 10 L20 20 Q30 30 40 10 C1 2 3 4 5 6 Z\"", DrawOpKind.Path)]
    [InlineData("circle x=50 y=50 r=10", DrawOpKind.Circle)]
    [InlineData("circle x=50 y=50 w=10 h=20", DrawOpKind.Circle)]
    [InlineData("rect x=1 y=2 w=30 h=40 r=6", DrawOpKind.Rect)]
    [InlineData("dot x=5 y=6", DrawOpKind.Dot)]
    [InlineData("text x=5 y=6 text=\"Hello\" size=20", DrawOpKind.Text)]
    [InlineData("highlight x=1 y=2 w=30 h=4", DrawOpKind.Highlight)]
    [InlineData("graph x=1 y=2 w=100 h=50 series=1,4,9;2,3,5 xlabel=Year ylabel=\"Sales ($)\"", DrawOpKind.Graph)]
    [InlineData("timeline x=1 y=50 w=200 items=\"1990:Web;2007:Phones\"", DrawOpKind.Timeline)]
    [InlineData("point x=5 y=6 label=Save", DrawOpKind.Point)]
    [InlineData("target x=1 y=2 w=30 h=40 label=\"Save button\" hover=1", DrawOpKind.Target)]
    [InlineData("frame x=1 y=2 w=30 h=40", DrawOpKind.Frame)]
    [InlineData("spotlight x=1 y=2 w=30 h=40", DrawOpKind.Spotlight)]
    [InlineData("ring x=50 y=50 r=10", DrawOpKind.Ring)]
    [InlineData("trace pts=1,1,5,5,9,1", DrawOpKind.Trace)]
    [InlineData("label x=5 y=6 text=Total", DrawOpKind.Label)]
    [InlineData("underline x=5 y=6 w=40", DrawOpKind.Underline)]
    [InlineData("strike x=5 y=6 w=40", DrawOpKind.Strike)]
    [InlineData("cross x=5 y=6", DrawOpKind.Cross)]
    [InlineData("check x=5 y=6 size=30", DrawOpKind.Check)]
    [InlineData("star x=5 y=6 size=400", DrawOpKind.Star)]
    [InlineData("star x=5 y=6", DrawOpKind.Star)]
    [InlineData("scroll x=5 y=6 dir=down", DrawOpKind.Scroll)]
    [InlineData("badge x=5 y=6 n=3", DrawOpKind.Badge)]
    [InlineData("button x=5 y=6 text=Explain prompt=\"Explain this chart\"", DrawOpKind.Button)]
    [InlineData("card x=5 y=6 title=Tip text=\"Use Ctrl+S\" w=240 stock=keyboard", DrawOpKind.Card)]
    [InlineData("sticky x=5 y=6 text=\"Remember this\"", DrawOpKind.Sticky)]
    [InlineData("tie pts=1,2,30,40", DrawOpKind.Tie)]
    [InlineData("recolor id=a color=#FF4C4C", DrawOpKind.Recolor)]
    [InlineData("move id=a dx=-5 dy=7", DrawOpKind.Move)]
    [InlineData("moveto id=a x=5 y=7", DrawOpKind.MoveTo)]
    [InlineData("scale id=a factor=1.5", DrawOpKind.Scale)]
    [InlineData("erase id=a", DrawOpKind.Erase)]
    [InlineData("undo", DrawOpKind.Undo)]
    [InlineData("clear", DrawOpKind.Clear)]
    public void ParsesEveryOp(string tag, DrawOpKind kind)
    {
        var (text, ops) = DrawCommandParser.Parse($"Look here. [DRAW:{tag}] Done.");
        Assert.Equal("Look here. Done.", text);
        var op = Assert.Single(ops);
        Assert.Equal(kind, op.Kind);
        Assert.Equal(11, op.Offset);
    }

    [Fact]
    public void ParsesCommonKeysAndShapes()
    {
        var (_, ops) = DrawCommandParser.Parse(
            "[DRAW:ring id=save-1 x=50 y=40 r=10 color=#26C281 screen=2 label=Save bogus=1]" +
            "[DRAW:path d=\"M10,10L20,20 Z\"][DRAW:cross x=5 y=6][DRAW:underline x=5 y=6 w=40]");
        var ring = ops[0];
        Assert.Equal("save-1", ring.Id);
        Assert.Equal("#26C281", ring.Color);
        Assert.Equal(2, ring.Screen);
        Assert.Equal("Save", ring.Text);
        Assert.Equal([new Pt(50, 40)], ring.Points);
        Assert.Equal(new Box(40, 30, 20, 20), ring.Box);
        Assert.False(ring.Args.ContainsKey("bogus"));
        Assert.Equal("MLZ", ops[1].Args["d"]);
        Assert.Equal([new Pt(10, 10), new Pt(20, 20)], ops[1].Points);
        Assert.Null(ops[1].Id);
        Assert.Equal("24", ops[2].Args["size"]);
        Assert.Equal([new Pt(5, 6), new Pt(45, 6)], ops[3].Points);
        Assert.Equal(new Box(5, 6, 40, 0), ops[3].Box);
    }

    [Fact]
    public void QuotedTextEscapes()
    {
        var (text, ops) = DrawCommandParser.Parse("""A [DRAW:text x=1 y=2 text="say \"hi\" \] ok \\ end"] B""");
        Assert.Equal("A B", text);
        Assert.Equal("""say "hi" ] ok \ end""", Assert.Single(ops).Text);
    }

    [Fact]
    public void BracketInsideQuotesDoesNotCloseTag()
    {
        const string reply = """Look. [DRAW:text x=1 y=1 text="Click [OK] now"] Then.""";
        var (text, ops) = DrawCommandParser.Parse(reply);
        Assert.Equal("Look. Then.", text);
        Assert.Equal("Click [OK] now", Assert.Single(ops).Text);
        Assert.Equal("Look. Then.", StreamingReplySpeech.Clean(reply));
        // An unterminated quote swallows the rest of its line (never spoken, no op) but ends at the line break.
        var (cut, none) = DrawCommandParser.Parse("Look. [DRAW:text x=1 y=1 text=\"Click [OK] now] after.\nNext.");
        Assert.Equal("Look.\nNext.", cut);
        Assert.Empty(none);
    }

    [Theory]
    [InlineData("‮")]
    [InlineData("‪")]
    [InlineData("⁦")]
    [InlineData("⁩")]
    [InlineData("‎")]
    [InlineData("‏")]
    [InlineData("؜")]
    public void RejectsBidiControls(string control)
    {
        Assert.Empty(DrawCommandParser.Parse($"[DRAW:point x=1 y=1 label=\"a{control}b\"]").Ops);
        Assert.Empty(DrawCommandParser.Parse($"[DRAW:text x=1 y=1 text=\"a{control}b\"]").Ops);
    }

    [Fact]
    public void KeepsEmojiJoiners()
    {
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        Assert.Equal(family, Assert.Single(DrawCommandParser.Parse($"[DRAW:text x=1 y=1 text=\"{family}\"]").Ops).Text);
    }

    [Fact]
    public void EverySplitPointNeverLeaksTagText()
    {
        const string reply = "First [DRAW:text x=1 y=1 text=\"Click [OK] now\"] line. "
            + "[DRAW:rect id=a x=1 y=1 w=5 h=5 label=\"a \\\"b\\\" \\] c \\\\\"] Second line! [DRAW:dot x=2 y=2]\n"
            + "[DRAW:path d=\"M1 1 L2 2\"] Third? [DRAW:undo]";
        for (var length = 0; length <= reply.Length; length++)
        {
            var prefix = reply[..length];
            foreach (var spoken in new[] { StreamingReplySpeech.Clean(prefix), DrawCommandParser.Parse(prefix, partial: true).Text })
            {
                Assert.DoesNotContain("DRAW", spoken);
                Assert.DoesNotContain("[DRA", spoken);
                Assert.True(spoken.IndexOfAny(['[', ']', '"', '=', '\\']) < 0, $"residue at {length}: {spoken}");
            }
        }
        Assert.Equal("First line. Second line!\nThird?", StreamingReplySpeech.Clean(reply));
        var ops = DrawCommandParser.Parse(reply).Ops;
        Assert.Equal(5, ops.Count);
        Assert.Equal("a \"b\" ] c \\", ops[1].Text);
    }

    [Theory]
    [InlineData("blob x=1 y=1")]
    [InlineData("ring x=5 y=5")]
    [InlineData("dot x=-5 y=5")]
    [InlineData("dot x=999999 y=5")]
    [InlineData("dot x=100000 y=5")]
    [InlineData("dot x=1.5 y=5")]
    [InlineData("dot x=99999999999 y=5")]
    [InlineData("dot x=1 y=5 id=Bad")]
    [InlineData("dot x=1 y=5 color=red")]
    [InlineData("dot x=1 y=5 screen=0")]
    [InlineData("DOT x=1 y=5")]
    [InlineData("dot x=1 y=5 X=3")]
    [InlineData("dot x=1y=5")]
    [InlineData("text x=1 y=5 text=\"bell\a\"")]
    [InlineData("text x=1 y=5 text=\"ok\" size=60")]
    [InlineData("star x=100 y=100 size=99999")] // ~100k wobble points a frame
    [InlineData("check x=100 y=100 size=401")]
    [InlineData("cross x=100 y=100 size=0")]
    [InlineData("curve pts=1,2,3,4,5,6")]
    [InlineData("poly pts=1,2,3,4")]
    [InlineData("line pts=1,2,3")]
    [InlineData("line pts=1,2,3,4 style=crayon")]
    [InlineData("path d=\"L10 10\"")]
    [InlineData("path d=\"M10 10 l5 5\"")]
    [InlineData("path d=\"M10 10 L5\"")]
    [InlineData("scale id=a factor=5")]
    [InlineData("badge x=1 y=1 n=16")]
    [InlineData("scroll x=1 y=1 dir=left")]
    [InlineData("card x=1 y=1 title=T text=X w=100")]
    [InlineData("graph x=1 y=1 w=9 h=9 series=1;2;3;4;5")]
    [InlineData("timeline x=1 y=1 w=9 items=\"a;b;c;d;e;f;g;h;i\"")]
    [InlineData("point x=1 y=1")]
    [InlineData("point x=1 y=1 label=\"0123456789012345678901234567890123456789012345678901234567890\"")]
    [InlineData("move id=a dx=5")]
    public void RejectsMalformedAndOutOfRange(string tag)
    {
        var (text, ops) = DrawCommandParser.Parse($"Before [DRAW:{tag}] after.");
        Assert.Equal("Before after.", text);
        Assert.Empty(ops);
    }

    [Fact]
    public void RejectsTooManyPointsAndTags()
    {
        var points = string.Join(",", Enumerable.Range(0, 66));
        Assert.Empty(DrawCommandParser.Parse($"[DRAW:line pts={points}]").Ops);
        var path = "M1 1" + string.Concat(Enumerable.Repeat(" L2 2", 64));
        Assert.Empty(DrawCommandParser.Parse($"[DRAW:path d=\"{path}\"]").Ops);

        var reply = string.Concat(Enumerable.Range(0, 25).Select(i => $"[DRAW:dot x={i} y=1]")) + "Done.";
        var (text, ops) = DrawCommandParser.Parse(reply);
        Assert.Equal("Done.", text);
        Assert.Equal(24, ops.Count);
        Assert.Equal(23, ops[^1].Points[0].X);

        var buttons = string.Concat(Enumerable.Range(0, 4).Select(i => $"[DRAW:button x={i} y=1 text=Go prompt=Go]"));
        Assert.Equal(3, DrawCommandParser.Parse(buttons).Ops.Count);
    }

    [Fact]
    public void StripsDanglingTagWhenPartial()
    {
        var (text, ops) = DrawCommandParser.Parse("Look here. [DRAW:rect x=1 y=", partial: true);
        Assert.Equal("Look here.", text);
        Assert.Empty(ops);
        Assert.Equal("Look", DrawCommandParser.Parse("Look [DRA", partial: true).Text);
        Assert.Equal("Look [DRA", DrawCommandParser.Parse("Look [DRA").Text);
        // An unclosed tag is never spoken, and it ends at the line break.
        Assert.Equal("Look here.", DrawCommandParser.Parse("Look here. [DRAW:rect x=1 y=").Text);
        var (multi, multiOps) = DrawCommandParser.Parse("A [DRAW:rect x=1\nB [DRAW:dot x=1 y=1]");
        Assert.Equal("A\nB", multi);
        Assert.Single(multiOps);
    }

    [Fact]
    public void FromMarksMapsAllSevenShapes()
    {
        ScreenAnnotation[] marks =
        [
            new("rect", 1, 2, 30, 40, "Box", 2),
            new("circle", 1, 2, 30, 40, "Ring", null),
            new("arrow", 1, 2, 30, 40, "Go", null),
            new("polygon", 0, 0, 0, 0, "Poly", null, [new(1, 1), new(9, 1), new(5, 9)]),
            new("freehand", 0, 0, 0, 0, "Pen", null, [new(1, 1), new(9, 9)]),
            new("curve", 0, 0, 0, 0, "Curve", null, [new(1, 1), new(3, 5), new(6, 5), new(9, 1)]),
            new("text", 7, 8, 0, 0, "Hello", null),
        ];
        var ops = DrawCommandParser.FromMarks(marks);
        Assert.Equal([DrawOpKind.Rect, DrawOpKind.Circle, DrawOpKind.Arrow, DrawOpKind.Poly, DrawOpKind.Trace, DrawOpKind.Curve, DrawOpKind.Text],
            ops.Select(o => o.Kind));
        Assert.Equal(new Box(1, 2, 30, 40), ops[0].Box);
        Assert.Equal(2, ops[0].Screen);
        Assert.Equal("Box", ops[0].Text);
        Assert.Equal([new Pt(1, 2), new Pt(30, 40)], ops[2].Points);
        Assert.Equal("1", ops[3].Args["closed"]);
        Assert.Equal(3, ops[3].Points.Count);
        Assert.Equal(new Box(1, 1, 8, 8), ops[4].Box);
        Assert.Equal(4, ops[5].Points.Count);
        Assert.Equal([new Pt(7, 8)], ops[6].Points);
        Assert.Equal("Hello", ops[6].Text);
    }
}
