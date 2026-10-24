using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed class ChromeBridgeHost(ChromeBridgePipeTransport transport) : IChromeBridgeHost
{
    public async Task<BrowserContext> GetCurrentContextAsync(CancellationToken token)
    {
        using var message = await transport.RequestAsync(new { requestId = Guid.NewGuid().ToString("N"), command = new { name = "contextActive" } }, token).ConfigureAwait(false);
        return ReadContext(message.RootElement);
    }

    public async Task<BrowserObservation> ObserveAsync(CancellationToken token)
    {
        using var message = await transport.RequestAsync(new { requestId = Guid.NewGuid().ToString("N"), command = new { name = "observeActive" } }, token).ConfigureAwait(false);
        ThrowIfError(message.RootElement);
        var response = message.RootElement.GetProperty("response");
        if (!response.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(response.GetProperty("error").GetString());
        var observation = response.GetProperty("observation");
        var context = ReadContext(message.RootElement, observation);
        var links = observation.GetProperty("links").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        return new(Guid.NewGuid().ToString("N"), context, observation.GetProperty("controls").GetRawText(), links);
    }

    public async Task<BrowserActionResult> ExecuteAsync(BrowserAction action, CancellationToken token)
    {
        Command command = action.Name switch
        {
            "browser_click" => new("click", action.Arguments["element_id"], null, null, ReadRevision(action)),
            "browser_type_text" => new("typeText", action.Arguments["element_id"], null, action.Arguments["text"], ReadRevision(action)),
            "browser_follow_link" => new("followLink", null, action.Arguments["link_id"], null, ReadRevision(action)),
            _ => throw new ArgumentOutOfRangeException(nameof(action), "Unsupported browser action."),
        };
        using var message = await transport.RequestAsync(new { requestId = Guid.NewGuid().ToString("N"), tabId = int.Parse(action.Arguments["tab_id"]), frameId = int.Parse(action.Arguments["frame_id"]), command }, token).ConfigureAwait(false);
        ThrowIfError(message.RootElement);
        var response = message.RootElement.GetProperty("response");
        return response.GetProperty("ok").GetBoolean()
            ? new(true, "Browser action completed; observe the destination to verify it.")
            : new(false, response.GetProperty("error").GetString() ?? "Browser action failed.");
    }

    private BrowserContext ReadContext(JsonElement root, JsonElement? observation = null)
    {
        ThrowIfError(root);
        var tab = root.GetProperty("tabId").GetInt32().ToString();
        var frame = root.GetProperty("frameId").GetInt32().ToString();
        var source = observation ?? root.GetProperty("response");
        if (source.TryGetProperty("ok", out var ok) && !ok.GetBoolean()) throw new InvalidOperationException(source.GetProperty("error").GetString());
        var url = source.GetProperty("url").GetString() ?? throw new InvalidDataException("Chrome response omitted URL.");
        var uri = new Uri(url, UriKind.Absolute);
        var revision = source.GetProperty("revision").GetInt64();
        return new(tab, frame, revision, uri.GetLeftPart(UriPartial.Authority) + "/", source.GetProperty("title").GetString() ?? "");
    }

    private static long ReadRevision(BrowserAction action) => long.Parse(action.Arguments["navigation_revision"]);
    private static void ThrowIfError(JsonElement root) { if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString()); }
    private sealed record Command(string name, string? elementId, string? linkId, string? text, long revision);
}
