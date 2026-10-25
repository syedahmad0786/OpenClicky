using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public sealed class RoutineRequestsTests
{
    private const string Daily = "{\"title\":\"Morning news\",\"task\":\"Summarise the headlines.\",\"at\":\"08:30\"}";

    private static string Block(string json, string tag = "openclicky-routine") => "```" + tag + "\n" + json + "\n```";

    [Fact]
    public void ExtractsFencedBlockAndStripsIt()
    {
        var (text, request) = RoutineRequests.Extract("sure, i can set that up. " + Block(Daily) + " say yes when you're ready.");
        Assert.Equal("sure, i can set that up. say yes when you're ready.", text);
        Assert.Equal(new RoutineRequest("Morning news", "Summarise the headlines.", null, 8 * 60 + 30, 127), request);
        Assert.Equal("openclicky-routine", RoutineRequests.FenceTag);
    }

    [Fact]
    public void InvalidBlockIsStrippedButIgnored()
    {
        foreach (var json in new[] { "not json", "[1]", "{\"title\":\"t\"}", "{\"title\":\"t\",\"task\":\"x\",\"every_minutes\":2}", "{\"title\":\"t\",\"task\":\"x\",\"at\":\"25:00\"}" })
        {
            var (text, request) = RoutineRequests.Extract("ok. " + Block(json));
            Assert.Equal("ok.", text);
            Assert.Null(request);
        }
        var (open, none) = RoutineRequests.Extract("ok. ```openclicky-routine\n{\"title\"");
        Assert.Equal("ok.", open);
        Assert.Null(none);
        Assert.Equal(("no block.", (RoutineRequest?)null), RoutineRequests.Extract("no block."));
    }

    [Fact]
    public void OnlyOneRequestPerReply()
    {
        var second = "{\"title\":\"Other\",\"task\":\"y\",\"every_minutes\":60}";
        var (text, request) = RoutineRequests.Extract("a. " + Block("{}") + " b. " + Block(Daily) + " c. " + Block(second));
        Assert.Equal("a. b. c.", text);
        Assert.Equal("Morning news", request!.Title); // the first valid one; the rest are only stripped
    }

    [Fact]
    public void ConfirmCardCopy()
    {
        var card = RoutineRequests.ConfirmCard("r1", RoutineRequests.Extract(Block(Daily)).Request!);
        Assert.Equal("routine-confirm:r1", card.Id);
        Assert.Equal(NotchCardKind.NeedsYou, card.Kind);
        Assert.Equal("Schedule this?", card.Title);
        Assert.Equal("Morning news · Every day at 08:30\nSummarise the headlines.", card.Body);
        Assert.Equal([new CardAction("schedule", "Schedule", CardActionStyle.Primary), new CardAction("dismiss", "Not now", CardActionStyle.Secondary)], card.Actions);
        Assert.Null(card.AutoDismissMs);
        Assert.Equal("r1", RoutineRequests.RequestIdOf(card.Id));
        Assert.Null(RoutineRequests.RequestIdOf("your-turn:r1"));
    }

    [Fact]
    public void CadenceWordsFollowDescribe()
    {
        Assert.Equal("Every 1 h", RoutineRequests.Cadence(new("t", "x", 60, null, 0)));
        Assert.Equal("Every 90 min", RoutineRequests.Cadence(new("t", "x", 90, null, 0)));
        Assert.Equal("Mon, Tue, Wed, Thu, Fri at 08:30", RoutineRequests.Cadence(new("t", "x", null, 510, 0b0111110)));
        Assert.Equal("Every day at 08:30", RoutineRequests.Cadence(new("t", "x", null, 510, 127)));
    }

    [Fact]
    public void ToastCopy() =>
        Assert.Equal("Scheduled: Morning news — Every day at 08:30.", RoutineRequests.ScheduledToast(RoutineRequests.Extract(Block(Daily)).Request!));

    [Fact]
    public void LongTaskIsCutAndLinesCollapse()
    {
        var body = RoutineRequests.ConfirmCard("r", new("T\nitle", new string('a', 500) + "\nb", 60, null, 0)).Body!;
        Assert.Equal("T itle · Every 1 h\n" + new string('a', 200), body);
    }

    [Fact]
    public void StreamingNeverSpeaksOrShowsTheBlock()
    {
        var full = "i can set that up. " + Block(Daily) + " just say so.";
        for (var i = 0; i <= full.Length; i++)
        {
            var clean = StreamingReplySpeech.Clean(full[..i]);
            Assert.DoesNotContain("openclicky", clean);
            Assert.DoesNotContain("Morning", clean);
            Assert.DoesNotContain("every_minutes", clean);
            Assert.DoesNotContain("08:30", clean);
        }
        Assert.Equal("i can set that up. just say so.", StreamingReplySpeech.Clean(full));
        Assert.Equal("i can set that up.", StreamingReplySpeech.Clean("i can set that up. ```openclicky-rou"));
    }

    [Fact]
    public void TalkInstructionsAreExact() =>
        Assert.StartsWith("If the user asks you to do something on a schedule", RoutineRequests.TalkInstructions);
}
