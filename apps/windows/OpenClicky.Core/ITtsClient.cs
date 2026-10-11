namespace OpenClicky.Core;

public interface ITtsClient
{
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
    void Stop();
}
