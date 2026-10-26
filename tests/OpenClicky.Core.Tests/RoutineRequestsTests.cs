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
    public void JobBlockBecomesAJobRequestAndIsNeverSpoken()
    {
        var (text, routine, job) = RoutineRequests.ExtractAll("i can start that as a background job. "
            + Block("{\"title\":\"Best note apps\",\"task\":\"Research the three best note apps for Windows and save a comparison.\"}", "openclicky-job"));
        Assert.Equal("i can start that as a background job.", text);
        Assert.Null(routine);
        Assert.Equal(new JobRequest("Best note apps", "Research the three best note apps for Windows and save a comparison."), job);
    }

    [Fact]
    public void RoutineAndJobInOneReplyAreBothRead()
    {
        var (text, routine, job) = RoutineRequests.ExtractAll("both. " + Block(Daily) + " and " + Block("{\"title\":\"T\",\"task\":\"Do it once.\"}", "openclicky-job"));
        Assert.Equal("both. and", text);
        Assert.NotNull(routine);
        Assert.Equal("Do it once.", job!.Task);
    }

    [Theory]
    [InlineData("{\"title\":\"\",\"task\":\"x\"}")]
    [InlineData("{\"title\":\"t\"}")]
    [InlineData("{\"title\":\"t\",\"task\":3}")]
    [InlineData("not json")]
    public void InvalidJobBlockIsStrippedButIgnored(string json)
    {
        var (text, _, job) = RoutineRequests.ExtractAll("ok. " + Block(json, "openclicky-job"));
        Assert.Equal("ok.", text);
        Assert.Null(job);
    }

    [Fact]
    public void AJobBlockStillBeingWrittenIsNeverSpoken()
    {
        Assert.Equal("starting it.", RoutineRequests.Strip("starting it. ```openclicky-job\n{\"title\":\"T\",\"ta"));
        Assert.Equal("starting it.", RoutineRequests.Strip("starting it. ```openclicky-j"));
    }

    [Fact]
    public void AJobClaimWithoutABlockStillOffersTheJob()
    {
        var job = RoutineRequests.FromClaim(" start that job ", "i've queued a background job to research three note apps and save a report.");
        Assert.Equal("start that job", job!.Title);
        Assert.Equal("The user asked: start that job\nWhat was agreed: i've queued a background job to research three note apps and save a report.", job.Task);
        Assert.Null(RoutineRequests.FromClaim("research", "here are three note apps."));
        Assert.Null(RoutineRequests.FromClaim("research", "i can't start a background job right now."));
        Assert.Null(RoutineRequests.FromClaim("research", "the background-job runtime is missing."));
    }

    [Fact]
    public void JobCardAsksBeforeAnythingStarts()
    {
        var card = RoutineRequests.JobConfirmCard("abc", new JobRequest("Best note apps", new string('x', 300)));
        Assert.Equal("job-confirm:abc", card.Id);
        Assert.Equal("abc", RoutineRequests.JobRequestIdOf(card.Id));
        Assert.Equal([RoutineRequests.StartAction, RoutineRequests.DismissAction], card.Actions.Select(a => a.Id));
        Assert.EndsWith("…", card.Body);
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
    public void LongTaskIsCutWithAnEllipsisAndLinesCollapse()
    {
        var body = RoutineRequests.ConfirmCard("r", new("T\nitle", new string('a', 500) + "\nb", 60, null, 0)).Body!;
        Assert.Equal("T itle · Every 1 h\n" + new string('a', 200) + "…", body);
        var exact = RoutineRequests.ConfirmCard("r", new("T", new string('a', 200), 60, null, 0)).Body!;
        Assert.Equal("T · Every 1 h\n" + new string('a', 200), exact); // not cut, no ellipsis
        Assert.Equal("T · Every 1 h\nshort task", RoutineRequests.ConfirmCard("r", new("T", "short\ntask", 60, null, 0)).Body);
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
        Assert.Equal("If the user asks you to do something on a schedule (for example \"every weekday at 8:30\" or \"every two hours\"), "
            + "do not do it now. Reply briefly that you can set it up, and add a fenced block tagged openclicky-routine holding one JSON object with "
            + "\"title\", \"task\" (what to do each time, written as an instruction) and either \"every_minutes\" or \"at\" (HH:MM, 24-hour) with optional "
            + "\"days\" (sun, mon, tue, wed, thu, fri, sat). The user confirms before anything is scheduled.", RoutineRequests.TalkInstructions);
}
