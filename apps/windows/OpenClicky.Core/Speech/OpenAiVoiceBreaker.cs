namespace OpenClicky.Core.Speech;

/// <summary>
/// When to stop trying the OpenAI voice: a key OpenAI rejected (401/403) is skipped for the rest of the session (a newly
/// saved key is tried); any other failure (network, timeout, server error) pauses OpenAI voices for five minutes.
/// Thread-safe. Keys are identified by an in-process id (e.g. a hash), never stored.
/// </summary>
public sealed class OpenAiVoiceBreaker
{
    public static readonly TimeSpan NetworkPause = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private int? _rejectedKey;
    private long _pausedUntilMs = long.MinValue;

    public bool ShouldTry(int key, long nowMs)
    {
        lock (_gate) return _rejectedKey != key && nowMs >= _pausedUntilMs;
    }

    /// <param name="status">The HTTP status, or null when no response arrived.</param>
    public void Failed(int key, int? status, long nowMs)
    {
        lock (_gate)
        {
            if (status is 401 or 403) _rejectedKey = key;
            else _pausedUntilMs = nowMs + (long)NetworkPause.TotalMilliseconds;
        }
    }
}
