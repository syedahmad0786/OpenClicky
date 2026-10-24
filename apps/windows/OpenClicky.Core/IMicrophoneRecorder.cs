namespace OpenClicky.Core;

public interface IMicrophoneRecorder : IDisposable
{
    void Start();
    byte[] StopToWav();
    byte[] SnapshotToWav() => [];
    void DiscardBufferedAudio() { }
    // True only when the active Windows capture path reports acoustic echo cancellation.
    bool SupportsEchoCancellation => false;
    bool IsRecording { get; }
}
