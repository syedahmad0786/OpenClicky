using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClicky.Core.Browser;

/// <summary>How an op is gated: Read and Local never ask; Act asks per access policy; AlwaysAsk asks unless Full; Lifecycle is app-only.</summary>
public enum BrowserOpKind { Read, Act, AlwaysAsk, Local, Lifecycle }

/// <summary>The browser tool's op table and the app-side argument checks (the extension re-validates).</summary>
public static class BrowserOps
{
    public static readonly IReadOnlyList<string> ModelOps =
    [
        "tabs", "open", "close", "snapshot", "extract", "click", "fill", "type", "press", "scroll", "wait_for", "back",
        "screenshot", "click_at", "hover_at", "drag_at", "javascript", "upload", "download", "dialog", "keep", "batch",
    ];
    public static readonly IReadOnlyList<string> All = [.. ModelOps, "end", "sweep"];
    public static readonly IReadOnlyList<string> BatchOps = ["open", "click", "fill", "press", "scroll", "wait_for", "back", "upload"];

    public static readonly IReadOnlyList<string> Keys =
        ["Enter", "Tab", "Escape", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Backspace", "Delete", "PageUp", "PageDown", "Home", "End"];

    private static readonly Regex Ref = new(@"^e[0-9]{1,7}\z", RegexOptions.CultureInvariant);

    public static BrowserOpKind Kind(string op, JsonElement args) => op switch
    {
        "tabs" or "snapshot" or "extract" or "scroll" or "wait_for" => BrowserOpKind.Read,
        "screenshot" => Bool(args, "save") ? BrowserOpKind.Local : BrowserOpKind.Read,
        "close" or "keep" => BrowserOpKind.Local,
        "javascript" or "upload" => BrowserOpKind.AlwaysAsk,
        "batch" => Steps(args).Any(s => Str(s, "op") == "upload") ? BrowserOpKind.AlwaysAsk : BrowserOpKind.Act,
        "end" or "sweep" => BrowserOpKind.Lifecycle,
        "open" or "click" or "fill" or "type" or "press" or "back" or "click_at" or "hover_at" or "drag_at" or "download" or "dialog"
            => BrowserOpKind.Act,
        _ => throw new ArgumentException($"Unknown browser operation '{op}'.", nameof(op)),
    };

    /// <summary>Returns the first problem with the call in one sentence, or null when it can be sent.</summary>
    public static string? Validate(string op, JsonElement args)
    {
        if (!All.Contains(op)) return $"Unknown operation \"{Clip(op)}\". Use one of: {string.Join(", ", ModelOps)}.";
        if (args.ValueKind != JsonValueKind.Object) return "Arguments must be an object.";
        if (Check(op, args) is { } problem) return problem;
        if (Has(args, "say") && Str(args, "say") is not { Length: <= 160 }) return "say is a short note of at most 160 characters.";
        if (Has(args, "rationale") && Str(args, "rationale") is not { Length: <= 500 }) return "rationale is at most 500 characters.";
        if (Kind(op, args) is BrowserOpKind.Act or BrowserOpKind.AlwaysAsk && string.IsNullOrWhiteSpace(Str(args, "rationale")))
            return $"{op} changes the page, so give a rationale saying why.";
        return null;
    }

    private static string? Check(string op, JsonElement a)
    {
        if (Has(a, "tabId") && !(a.GetProperty("tabId").TryGetInt32(out var tab) && tab > 0)) return "tabId must be a positive whole number.";
        if (Has(a, "ref") && !(Str(a, "ref") is { } r && Ref.IsMatch(r))) return "ref must be a ref like e12 from the latest snapshot.";
        switch (op)
        {
            case "open":
                return BadUrl(Str(a, "url"));
            case "download":
                if (Has(a, "url")) return BadUrl(Str(a, "url"));
                return Has(a, "ref") ? null : "Give the url or the ref of a link to download.";
            case "click" or "fill" or "upload" when !Has(a, "ref"):
                return $"{op} needs a ref from the latest snapshot.";
            case "fill" or "type" when Str(a, "text") is not { Length: <= 20_000 }:
                return "Give the text to enter, at most 20,000 characters.";
            case "press" when !Keys.Contains(Str(a, "key") ?? ""):
                return $"Unknown key. Use one of: {string.Join(", ", Keys)}.";
            case "scroll" when Str(a, "direction") is not ("up" or "down"):
                return "Scroll up or down.";
            case "wait_for" when Has(a, "timeout") && !(Num(a, "timeout") is >= 0 and <= 20):
                return "wait_for waits at most 20 seconds.";
            case "javascript" when Str(a, "code") is not { Length: > 0 and <= 20_000 } code || string.IsNullOrWhiteSpace(code):
                return "Give the script to run, at most 20,000 characters.";
            case "click_at" or "hover_at" when !Points(a, "x", "y"):
            case "drag_at" when !Points(a, "x", "y", "toX", "toY"):
                return "Give x and y in screenshot pixels.";
            case "upload" when !(a.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array
                && paths.GetArrayLength() is >= 1 and <= 10 && paths.EnumerateArray().All(p => p.ValueKind == JsonValueKind.String)):
                return "Give 1 to 10 file paths.";
            case "batch":
                if (!(a.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array && steps.GetArrayLength() is >= 1 and <= 20))
                    return "A batch has 1 to 20 steps.";
                var i = 0;
                foreach (var step in steps.EnumerateArray())
                {
                    i++;
                    var stepOp = step.ValueKind == JsonValueKind.Object ? Str(step, "op") : null;
                    if (stepOp is null || !BatchOps.Contains(stepOp)) return $"Step {i}: {Clip(stepOp ?? "that step")} can't be used in a batch.";
                    if (Check(stepOp, step) is { } bad) return $"Step {i}: {bad}";
                }
                return null;
        }
        return null;
    }

    /// <summary>Every path a call would upload, including batch upload steps.</summary>
    public static IReadOnlyList<string> UploadPaths(string op, JsonElement args)
    {
        var sources = op == "batch" ? Steps(args).Where(s => Str(s, "op") == "upload") : op == "upload" ? new[] { args } : [];
        return sources.SelectMany(s => s.TryGetProperty("paths", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Select(x => x.GetString() ?? "") : []).ToList();
    }

    private static string? BadUrl(string? url)
    {
        if (url is not { Length: > 0 and <= 2048 }) return "Use an absolute http or https address of at most 2,048 characters.";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "That is not an absolute web address.";
        if (uri.Scheme is not ("http" or "https")) return "Only http and https addresses can be opened.";
        return uri.UserInfo.Length > 0 ? "Addresses with a user name or password are not allowed." : null;
    }

    private static IEnumerable<JsonElement> Steps(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty("steps", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object) : [];
    private static bool Has(JsonElement a, string name) => a.TryGetProperty(name, out _);
    private static string? Str(JsonElement a, string name) =>
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Num(JsonElement a, string name) =>
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static bool Bool(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static bool Points(JsonElement a, params string[] names) => names.All(n => Num(a, n) is double d && d >= 0 && double.IsFinite(d));
    private static string Clip(string s) => s.Length > 40 ? s[..40] : s;
}
