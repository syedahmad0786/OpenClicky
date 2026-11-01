using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClicky.Core.Browser;

/// <summary>One browser-card button. <paramref name="Profile"/> "" means the browser's first profile.</summary>
public sealed record BrowserSetupRequest(string Op, string? Browser = null, string? Profile = null, string? Id = null);

/// <summary>Parses the <c>chrome.setup</c> arg: "disconnect", or a JSON object <c>{"op":...}</c> from the browser card.</summary>
public static class BrowserSetupArgs
{
    public const int MaxLength = 512;
    private static readonly string[] Downloads = ["chrome", "edge", "brave"];
    private static readonly Regex BrowserId = new("^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant);

    public static BrowserSetupRequest? Parse(string? arg)
    {
        if (arg == "disconnect") return new("disconnect");
        if (string.IsNullOrEmpty(arg) || arg.Length > MaxLength) return null;
        try
        {
            using var doc = JsonDocument.Parse(arg);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return Str(root, "op") switch
            {
                "connect" when Browser(root) is { } b && Str(root, "profile") is { } p && SafeName(p, allowEmpty: true) => new("connect", Browser: b, Profile: p),
                "get" when Browser(root) is { } b && Downloads.Contains(b) => new("get", Browser: b),
                "select" when Str(root, "id") is { } id && SafeName(id, allowEmpty: false) => new("select", Id: id),
                "openExtensions" or "showFolder" or "copyPath" => new(Str(root, "op")!),
                _ => null,
            };
        }
        catch (JsonException) { return null; }
    }

    private static string? Browser(JsonElement root) => Str(root, "browser") is { } b && BrowserId.IsMatch(b) ? b : null;

    /// <summary>A profile directory or instance id: no path parts, quotes or control characters.</summary>
    private static bool SafeName(string value, bool allowEmpty) =>
        (allowEmpty || value.Length > 0) && value.Length <= 128 && !value.Contains("..", StringComparison.Ordinal)
        && !value.Any(c => c is '\\' or '/' or '"' || char.IsControl(c));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
