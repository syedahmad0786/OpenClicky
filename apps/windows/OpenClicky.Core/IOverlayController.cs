namespace OpenClicky.Core;

public interface IOverlayController : IDisposable
{
    void ShowListening();
    void ShowProcessing();
    void ShowResponding(string spokenText);

    /// <param name="spokenText">
    /// Optional reply bubble set in the same UI marshal as the fly-to so Talk
    /// does not need a follow-up <see cref="ShowResponding"/> that could race.
    /// </param>
    void PointAt(int virtualX, int virtualY, string? label, string? spokenText = null);
    void Hide();
}
