using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public sealed class JobCardsTests
{
    private static WorkJob Job(string title = "Pay the invoice") =>
        new("job1", "openclicky", title, "prompt", DateTimeOffset.UnixEpoch, WorkJobStates.NeedsYou, "red", "");

    private static UserStep Step(string? page) => new("Sign in to the bank", page, "/home", DateTimeOffset.UnixEpoch);

    [Fact]
    public void YourTurnCardHasTheExactCopyAndActions()
    {
        var card = JobCards.YourTurn(Job(), Step("https://bank.example/login"));
        Assert.Equal("your-turn:job1", card.Id);
        Assert.Equal(NotchCardKind.NeedsYou, card.Kind);
        Assert.Equal("Your turn: Pay the invoice", card.Title);
        Assert.Equal("Sign in to the bank", card.Body);
        Assert.Equal([new CardAction("open-page", "Open page", CardActionStyle.Primary), new CardAction("done", "I'm done", CardActionStyle.Secondary),
            new CardAction("stop", "Stop", CardActionStyle.Secondary)], card.Actions);
        Assert.Null(card.AutoDismissMs);
    }

    [Fact]
    public void WithNoPageThereIsNoOpenPageAction()
    {
        var card = JobCards.YourTurn(Job(), Step(null));
        Assert.Equal([new CardAction("done", "I'm done", CardActionStyle.Secondary), new CardAction("stop", "Stop", CardActionStyle.Secondary)], card.Actions);
    }

    [Fact]
    public void JobIdRoundTripsThroughTheCardId() =>
        Assert.Equal("job1", JobCards.JobIdOf(JobCards.YourTurn(Job(), Step(null)).Id));

    [Fact]
    public void LongTitlesAreCut()
    {
        var card = JobCards.YourTurn(Job(new string('x', 500)), Step(null));
        Assert.True(card.Title.Length <= "Your turn: ".Length + JobAnnouncer.MaxTitle);
    }
}
