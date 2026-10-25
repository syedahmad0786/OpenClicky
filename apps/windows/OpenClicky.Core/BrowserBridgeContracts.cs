using System.Text.Json;
using OpenClicky.Core.Agent;

namespace OpenClicky.Core;

/// <summary>Routes browser writes through the application's task-scoped approval broker.</summary>
public interface IChromeActionBroker
{
    Task<bool> ApproveAsync(ActionRequest request, CancellationToken token);
}

/// <summary>One command for the browser extension. Lifecycle ops (<c>end</c>, <c>sweep</c>) are sent only by the app.</summary>
public sealed record BrowserCommand(string Op, string Task, string Owner, string GroupTitle, string Color, IReadOnlyList<string> Deny, string? Say, JsonElement Args);

/// <summary>The extension's answer. <c>Ok=false</c> means <see cref="Text"/> is the error.</summary>
public sealed record BrowserReply(bool Ok, string Text, string? ImageBase64 = null, string? ImageMimeType = null);

public interface IBrowserBridge
{
    bool IsConnected { get; }
    Task<BrowserReply> SendAsync(BrowserCommand command, TimeSpan timeout, CancellationToken token);
    /// <summary>Marks the task as running until the result is disposed.</summary>
    IDisposable TrackTask(string task);
}
