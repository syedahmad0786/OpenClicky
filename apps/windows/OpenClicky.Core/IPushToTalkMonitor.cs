namespace OpenClicky.Core;

public interface IPushToTalkMonitor : IDisposable
{
    HotkeyChord Chord { get; set; }
    event EventHandler? Pressed;
    event EventHandler? Released;
    void Start();
    void Stop();
}

/// <summary>Released/DictateEnded args from the shortcut monitor.</summary>
public sealed class ShortcutEndedEventArgs(bool cancelled) : EventArgs
{
    /// <summary>A tap, another key, Esc or Stop ended it: discard rather than send.</summary>
    public bool Cancelled { get; } = cancelled;
}
