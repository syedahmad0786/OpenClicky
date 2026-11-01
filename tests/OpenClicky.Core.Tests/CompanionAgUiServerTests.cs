using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public sealed class CompanionAgUiServerTests
{
    [Fact]
    public async Task ServerShutdownDoesNotDependOnItsStartingUiContext()
    {
        var server = new CompanionAgUiServer((_, update, _) => { update("Done", true); return Task.CompletedTask; });
        var context = new StoppedUiContext();
        var previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(context); server.Start(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = Message(server, Request("hello"));
        using var response = await client.SendAsync(request);
        Assert.Contains("RUN_FINISHED", await response.Content.ReadAsStringAsync());
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, context.PostCount);
    }

    private sealed class StoppedUiContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref PostCount);
    }

    [Fact]
    public async Task ActualHttpRejectsMissingAuthOriginAndForeignHost()
    {
        await using var server = new CompanionAgUiServer((_, _, _) => Task.CompletedTask);
        server.Start();
        using var client = new HttpClient();
        using var missing = await client.PostAsync(server.Endpoint, JsonBody(Request("hello")));
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var origin = Message(server, Request("hello"));
        origin.Headers.Add("Origin", "http://127.0.0.1:9999");
        using var originResponse = await client.SendAsync(origin);
        Assert.Equal(HttpStatusCode.Unauthorized, originResponse.StatusCode);

        using var foreign = Message(server, Request("hello"));
        foreign.Headers.Host = "evil.example";
        using var foreignResponse = await client.SendAsync(foreign);
        Assert.NotEqual(HttpStatusCode.OK, foreignResponse.StatusCode);
    }

    [Fact]
    public async Task OnlyLastNewUserMessageIsAcceptedAndInvalidRootIsSanitized()
    {
        string? received = null;
        await using var server = new CompanionAgUiServer((prompt, _, _) => { received = prompt; return Task.CompletedTask; });
        server.Start();
        using var client = new HttpClient();
        using var valid = Message(server, new
        {
            threadId = "thread", runId = "run",
            messages = new object[] { new { role = "user", content = "old" }, new { role = "assistant", content = "reply" }, new { role = "user", content = "new" } },
        });
        using var response = await client.SendAsync(valid);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("new", received);

        using var stale = Message(server, new
        {
            threadId = "thread", runId = "run-stale",
            messages = new object[] { new { role = "user", content = "old" }, new { role = "assistant", content = "already answered" } },
        });
        using var staleResponse = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.BadRequest, staleResponse.StatusCode);

        using var invalid = Message(server, Array.Empty<object>());
        using var invalidResponse = await client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.DoesNotContain("InvalidOperationException", await invalidResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConcurrentRunGetsConflictAndClientAbortCancelsActiveRun()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new CompanionAgUiServer(async (_, _, token) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled.TrySetResult(); throw; }
        });
        server.Start();
        using var client = new HttpClient();
        using var firstRequest = Message(server, Request("first"));
        using var first = await client.SendAsync(firstRequest, HttpCompletionOption.ResponseHeadersRead);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var secondRequest = Message(server, Request("second"));
        using var second = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        first.Dispose();
        await server.DisposeAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SuccessfulRunStreamsRealDeltasAndFinishesWithoutError()
    {
        await using var server = new CompanionAgUiServer((_, update, _) =>
        {
            update("Hel", false);
            update("Hello", true);
            return Task.CompletedTask;
        });
        server.Start();
        using var client = new HttpClient();
        using var request = Message(server, Request("hello"));
        using var response = await client.SendAsync(request);
        var sse = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"type\":\"TEXT_MESSAGE_CONTENT\",\"messageId\":", sse);
        Assert.Contains("\"delta\":\"Hel\"", sse);
        Assert.Contains("\"delta\":\"lo\"", sse);
        Assert.Contains("\"type\":\"RUN_FINISHED\"", sse);
        Assert.DoesNotContain("RUN_ERROR", sse);
    }

    private static HttpRequestMessage Message(CompanionAgUiServer server, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint) { Content = JsonBody(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
        return request;
    }

    private static StringContent JsonBody(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    private static object Request(string prompt) => new
    {
        threadId = "thread", runId = Guid.NewGuid().ToString("N"), messages = new[] { new { role = "user", content = prompt } },
    };
}
