namespace OpenClicky.Core;

public interface IOverlayController : IDisposable
{
    void ShowListening();
    void ShowProcessing();
    void ShowResponding(string spokenText);
    void PointAt(int virtualX, int virtualY, string? label);
    void Hide();
}
