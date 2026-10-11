namespace OpenClicky.Core;

public interface ITalkClient
{
    Task<string> CompleteVisionTalkAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string text)> history,
        string userTranscript,
        IReadOnlyList<ScreenCaptureResult> screens,
        string model,
        CancellationToken cancellationToken = default);
}
