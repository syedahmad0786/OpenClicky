using OpenClicky.Core.Shell;

namespace OpenClicky.Core.CompanionCursor;

/// <summary>The companion's notch cards and their actions (C11 and C12 add theirs here).</summary>
public static class CompanionCards
{
    public const string WalkthroughId = "walkthrough", BackAction = "back", RecheckAction = "recheck", StopAction = "stop";
    public const int MaxBody = 1_000;

    /// <summary>The walkthrough step card: the instruction (or, before the first one, the status) and Back / Recheck / Stop.</summary>
    public static NotchCard Walkthrough(WalkthroughView v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var text = string.IsNullOrWhiteSpace(v.Instruction) ? v.Status : v.Instruction;
        List<CardAction> actions = [];
        if (v.CanBack) actions.Add(new(BackAction, "Back", CardActionStyle.Secondary));
        actions.Add(new(RecheckAction, "Recheck", CardActionStyle.Secondary));
        actions.Add(new(StopAction, "Stop", CardActionStyle.Danger));
        return new(WalkthroughId, NotchCardKind.Activity, v.Step > 0 ? $"Step {v.Step}" : "Guide",
            string.IsNullOrWhiteSpace(text) ? null : text.Length <= MaxBody ? text : text[..MaxBody], actions);
    }
}
