namespace OpenClicky.Core;

/// <summary>
/// Talk may call <c>PointAt</c> then <c>ShowResponding</c> while TTS plays.
/// Leaving Pointing for Responding re-anchors the buddy to the cursor every paint
/// frame and cancels the fly-to — so spoken text must keep Pointing when active.
/// </summary>
public enum OverlaySpokenVisualMode
{
    Responding,
    Pointing,
}

public static class OverlayRespondingMode
{
    public static OverlaySpokenVisualMode Resolve(bool pointingActive) =>
        pointingActive ? OverlaySpokenVisualMode.Pointing : OverlaySpokenVisualMode.Responding;
}
