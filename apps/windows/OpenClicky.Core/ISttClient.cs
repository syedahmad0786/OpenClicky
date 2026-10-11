namespace OpenClicky.Core;

public interface ISttClient
{
    Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default);
}
