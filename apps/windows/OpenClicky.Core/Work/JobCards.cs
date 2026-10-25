using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Work;

/// <summary>Notch cards for a job that needs the user's hands (E8). Card ids carry the job id; the shell only forwards actions a card offers.</summary>
public static class JobCards
{
    public const string YourTurnPrefix = "your-turn:", OpenPageAction = "open-page", DoneAction = "done";

    public static string YourTurnId(string jobId) => YourTurnPrefix + jobId;

    /// <summary>The job id inside a your-turn card id, or null for any other card.</summary>
    public static string? JobIdOf(string cardId) => cardId.StartsWith(YourTurnPrefix, StringComparison.Ordinal) ? cardId[YourTurnPrefix.Length..] : null;

    /// <summary>The step is untrusted model text and the title may be: the title is cut. The card stays until the step ends.</summary>
    public static NotchCard YourTurn(WorkJob job, UserStep step)
    {
        var title = job.Title.ReplaceLineEndings(" ").Trim();
        if (title.Length > JobAnnouncer.MaxTitle) title = title[..JobAnnouncer.MaxTitle].TrimEnd();
        List<CardAction> actions = [];
        if (UserStepWatch.PageToOpen(step) is not null) actions.Add(new(OpenPageAction, "Open page", CardActionStyle.Primary));
        actions.Add(new(DoneAction, "I'm done", CardActionStyle.Secondary));
        return new(YourTurnId(job.Id), NotchCardKind.NeedsYou, "Your turn: " + title, step.Step, actions);
    }
}
