using OpenClicky.Core.Shell;

namespace OpenClicky.Core.CompanionCursor;

/// <summary>When the walkthrough card is added (shown, to the front) rather than updated where it is.</summary>
public static class WalkthroughCardPolicy
{
    /// <param name="cardStep">The step the card was last added for; -1 when no walkthrough card is up.</param>
    /// <returns>True when the walkthrough just started, or a new step (not a reload of the current one) arrived.</returns>
    public static bool AddsCard(int cardStep, WalkthroughView view) => view.Active && (cardStep < 0 || !view.Busy && view.Step != cardStep);
}

/// <summary>The companion's notch cards and their actions (C11 and C12 add theirs here).</summary>
public static class CompanionCards
{
    public const string WalkthroughId = "walkthrough", BackAction = "back", RecheckAction = "recheck", StopAction = "stop";
    public const string DrawingSaveId = "drawing-save", SaveAction = "save", ClearAction = "clear";
    public const string DriveConsentId = "drive-consent", DriveAction = "drive", NotNowAction = "notNow", DrivingId = "driving";
    public const int MaxBody = 1_000, DrawingOfferMs = 20_000;

    /// <summary>Asked once per task before its first mouse or keyboard input. Only a real click on Drive says yes.</summary>
    public static NotchCard DriveConsent() => new(DriveConsentId, NotchCardKind.NeedsYou, "Let OpenClicky use your mouse and keyboard?",
        "For this task only. Move the mouse or press any key to take over; Esc stops.",
        [new(DriveAction, "Let OpenClicky drive", CardActionStyle.Primary), new(NotNowAction, "Not now", CardActionStyle.Secondary)]);

    /// <summary>The Esc pill: shown while a task drives.</summary>
    public static NotchCard Driving() => new(DrivingId, NotchCardKind.Activity, "OpenClicky is driving",
        "Esc stops · move the mouse to take over", [new(StopAction, "Stop", CardActionStyle.Danger)]);

    /// <summary>Offered when a turn that drew ends. Dismissing it (or letting it expire) keeps the drawing on screen, unsaved.</summary>
    public static NotchCard SaveDrawing() => new(DrawingSaveId, NotchCardKind.Tip, "Keep this drawing?",
        "Save it to replay later, or clear the screen.",
        [new(SaveAction, "Save", CardActionStyle.Primary), new(ClearAction, "Clear", CardActionStyle.Secondary)], DrawingOfferMs);

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
