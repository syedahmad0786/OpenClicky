namespace OpenClicky.Core;

public interface ILiveSttClient : ISttClient, IDisposable
{
    event Action<string>? TranscriptChanged;
    event Action? SpeechStarted { add { } remove { } }
    event Action? UtteranceEnded { add { } remove { } }
    event Action<Exception>? ListeningFailed { add { } remove { } }
    // True only when the active capture path has verified system acoustic echo cancellation.
    bool SupportsEchoCancelledInterruption => false;
    void StartListening();
    Task<string> FinishListeningAsync(CancellationToken cancellationToken);
    void CancelListening();
}
