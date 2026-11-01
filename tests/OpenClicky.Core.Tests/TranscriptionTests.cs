using System.Net;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class TranscriptionTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"text\":null}")]
    public async Task MissingTranscript_IsAnErrorRatherThanSpokenJson(string body)
    {
        using var http = new HttpClient(new Response(body));
        var client = new OpenRouterSttClient("fixture-key", "openai/whisper-1", httpClient: http);
        await Assert.ThrowsAsync<FormatException>(() => client.TranscribeAsync([1,2,3]));
    }

    [Fact]
    public async Task MultipartRequest_ReturnsOnlyTranscript()
    {
        using var http = new HttpClient(new Response("{\"text\":\"hello\"}"));
        var client = new OpenRouterSttClient("fixture-key", "openai/whisper-1", httpClient: http);
        Assert.Equal("hello", await client.TranscribeAsync([1,2,3]));
    }

    private sealed class Response(string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://openrouter.ai/api/v1/audio/transcriptions", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
            var multipart = await request.Content.ReadAsStringAsync(token);
            Assert.Contains("speech.wav", multipart);
            Assert.Contains("openai/whisper-1", multipart);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
