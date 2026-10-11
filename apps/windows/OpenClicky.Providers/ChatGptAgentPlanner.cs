using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed class ChatGptAgentPlanner : IAgentPlanner
{
    private readonly ChatGptResponsesClient _responses;

    public ChatGptAgentPlanner(Func<CancellationToken, Task<string>> accessTokenFactory, HttpClient? httpClient = null)
    {
        _responses = new ChatGptResponsesClient(accessTokenFactory, httpClient);
    }

    public async Task<AgentPlanStep> NextStepAsync(
        string task,
        IReadOnlyList<ScreenCaptureResult> screens,
        IReadOnlyList<string> priorResults,
        string model,
        CancellationToken cancellationToken = default)
    {
        var userContent = new List<object>
        {
            new { type = "input_text", text = $"task: {task}" },
        };
        if (priorResults.Count > 0)
        {
            userContent.Add(new
            {
                type = "input_text",
                text = "prior step results:\n" + string.Join("\n", priorResults),
            });
        }

        foreach (var screen in screens.Take(2))
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

        var input = new object[]
        {
            new { role = "user", content = userContent },
        };

        var content = await _responses.CompleteAsync(
            CompanionPrompts.AgentSystemPrompt,
            input,
            model,
            cancellationToken).ConfigureAwait(false);

        return OpenRouterAgentPlanner.ParseStep(content);
    }
}
