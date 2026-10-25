using System.Drawing;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core;

public interface IOverlayController : IDisposable
{
    void ShowListening();
    void ShowProcessing();
    void ShowResponding(string spokenText);
    void ShowTranscript(string text) { }
    void ShowAnnotations(IReadOnlyList<VirtualAnnotation> marks) { }

    /// <param name="spokenText">
    /// Optional reply bubble set in the same UI marshal as the fly-to so Talk
    /// does not need a follow-up <see cref="ShowResponding"/> that could race.
    /// </param>
    void PointAt(int virtualX, int virtualY, string? label, string? spokenText = null);

    /// <summary>
    /// POINT at a control: <paramref name="controlFrame"/> (virtual-screen px) is the control's bounds, so the click ring
    /// outlines it instead of a plain circle. Without a frame (or on an overlay that ignores it) this is the plain POINT.
    /// </summary>
    void PointAt(int virtualX, int virtualY, string? label, string? spokenText, Box? controlFrame) =>
        PointAt(virtualX, virtualY, label, spokenText);

    Task<Rectangle?> SelectRegionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Rectangle?>(null);

    /// <summary>
    /// Hides every overlay window until the next Show* or <see cref="ShowIdle"/> (used before a capture). Keeps the drawing.
    /// Synchronous: the windows are hidden when it returns; wait <see cref="HideSettleMilliseconds"/> before capturing.
    /// </summary>
    void Hide();

    /// <summary>
    /// Until the returned scope is disposed, nothing shows the overlay again (a ShowIdle from a pause or the end of a talk turn is
    /// deferred to the release). Take it before <see cref="Hide"/> around an agent's capture; overlays that never defer return a no-op.
    /// </summary>
    IDisposable HoldHidden() => OverlayHoldGate.None;

    /// <summary>Holds the overlay hidden, then hides it: the scope covers the settle delay and the capture.</summary>
    IDisposable HideForCapture()
    {
        var hold = HoldHidden();
        Hide();
        return hold;
    }

    /// <summary>Time for the desktop compositor to drop hidden overlay windows from what a capture sees.</summary>
    const int HideSettleMilliseconds = 120;

    /// <summary>The resting companion: the cursor is visible and following with no new bubble (the last spoken line fades
    /// out); the drawing is kept.</summary>
    void ShowIdle() { }

    void ApplyStyle(CompanionStyle style) { }

    /// <summary>Where a docked companion goes: the top centre of the monitor that hosts the shell surface (virtual-screen px).</summary>
    void SetNotchPoint(int virtualX, int virtualY) { }

    /// <summary>Microphone peak level (0..1) for the listening bars; fed about every 100 ms while listening.</summary>
    void ShowMicLevel(double level) { }

    /// <summary>
    /// Shows the whole drawing (<paramref name="parts"/> in virtual-screen px, a snapshot the caller will not change). New parts
    /// reveal as <paramref name="animations"/> say, with starts relative to this call; erased parts fade. Empty parts clear it.
    /// The drawing stays up across turns until it is cleared; <see cref="Hide"/> hides it only until the next Show*.
    /// </summary>
    void ShowDrawing(IReadOnlyList<DrawPart> parts, IReadOnlyList<PartAnimation> animations) { }

    /// <summary>Ends every running reveal or erase at once (a new talk press): the drawing shows its final state.</summary>
    void FinishDrawing() { }

    /// <summary>
    /// One pointer sample of the user's highlight while they hold talk (virtual-screen px): the trail shows in the accent and
    /// each sample fades over a second. <see cref="Hide"/> hides it with everything else.
    /// </summary>
    void AddHighlightPoint(int x, int y) { }

    /// <summary>Takes the highlight trail off the screen at once (Esc, a cancelled hold, a new hold).</summary>
    void ClearHighlight() { }

    /// <summary>True while the last <see cref="ShowDrawing"/> had parts. Any thread.</summary>
    bool HasDrawing => false;

    /// <summary>Every overlay window that exists now (capture exclusion, own-window lists).</summary>
    IReadOnlyList<nint> WindowHandles => [];
}
