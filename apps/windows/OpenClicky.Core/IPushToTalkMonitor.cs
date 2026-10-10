namespace OpenClicky.Core;

public interface IPushToTalkMonitor : IDisposable
{
    HotkeyChord Chord { get; set; }
    event EventHandler? Pressed;
    event EventHandler? Released;
    void Start();
    void Stop();
}
