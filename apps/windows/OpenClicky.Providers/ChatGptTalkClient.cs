using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed class ChatGptTalkClient : ITalkClient
{
    private readonly ChatGptResponsesClient _responses;

    public ChatGptTalkClient(Func<CancellationToken, Task<string>> accessTokenFactory, HttpClient? httpClient = null)
    {
        _responses = new ChatGptResponsesClient(accessTokenFactory, httpClient);
    }

    public Task<string> CompleteVisionTalkAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string text)> history,
        string userTranscript,
        IReadOnlyList<ScreenCaptureResult> screens,
        string model,
        CancellationToken cancellationToken = default)
    {
        var input = new List<object>();
        foreach (var (role, text) in history)
        {
            input.Add(new
            {
                role,
                content = new object[] { new { type = "input_text", text } },
            });
        }

        var userContent = new List<object>
        {
            new { type = "input_text", text = userTranscript },
        };
        foreach (var screen in screens)
        {
            userContent.Add(new
            {
                type = "input_text",
                text = $"{screen.Label} (image dimensions: {screen.PixelWidth}x{screen.PixelHeight} pixels)",
            });
            userContent.Add(new
            {
                type = "input_image",
                image_url = $"data:image/jpeg;base64,{Convert.ToBase64String(screen.JpegBytes)}",
            });
        }

        input.Add(new { role = "user", content = userContent });
        return _responses.CompleteAsync(systemPrompt, input, model, cancellationToken);
    }
}
