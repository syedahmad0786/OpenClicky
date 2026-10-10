namespace OpenClicky.Core;

public interface IOverlayController : IDisposable
{
    void ShowListening();
    void ShowProcessing();
    void Hide();
}
