using System.Drawing;
using OpenClicky.Core.CompanionCursor;

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
    Task<Rectangle?> SelectRegionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Rectangle?>(null);

    /// <summary>Hides every overlay window until the next Show* or <see cref="ShowIdle"/> (used before a capture). Keeps the drawing.</summary>
    void Hide();

    /// <summary>The resting companion: the cursor is visible and following, with no bubble; the drawing is kept.</summary>
    void ShowIdle() { }

    void ApplyStyle(CompanionStyle style) { }

    /// <summary>Microphone peak level (0..1) for the listening bars; fed about every 100 ms while listening.</summary>
    void ShowMicLevel(double level) { }

    /// <summary>Every overlay window that exists now (capture exclusion, own-window lists).</summary>
    IReadOnlyList<nint> WindowHandles => [];
}
