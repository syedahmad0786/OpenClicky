namespace OpenClicky.Core;

public interface IMicrophoneRecorder : IDisposable
{
    void Start();
    byte[] StopToWav();
    bool IsRecording { get; }
}
