using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenClicky.Core.Speech;

namespace OpenClicky.App.Speech;

/// <summary>OpenAI text to speech (gpt-4o-mini-tts, WAV). The key is sent only to api.openai.com and never logged.</summary>
internal static class OpenAiVoice
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/audio/speech");
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan }; // the 30 s limit is per request below

    public static async Task<SpeechClip> RenderAsync(string apiKey, string voice, string text, double speed, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { model = "gpt-4o-mini-tts", voice, input = text, speed, response_format = "wav" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("OpenAI speech failed.", null, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        var info = SpeechPlan.ReadWav(bytes) ?? throw new InvalidDataException("OpenAI speech was not PCM WAV.");
        return new SpeechClip(bytes, info, SpeechPlan.SpreadWords(text, info.Duration));
    }
}
