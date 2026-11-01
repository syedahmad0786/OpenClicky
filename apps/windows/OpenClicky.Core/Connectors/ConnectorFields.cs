using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core;

/// <summary>Parsers and limits for the free-text fields of a custom connector (headers, variables, arguments, command, label).</summary>
public static class ConnectorFields
{
    private const int MaxHeaders = 16, MaxVariables = 32, MaxArguments = 32, MaxValue = 4_000, MaxArgument = 1_000, MaxCommand = 260, MaxLabel = 30;

    private static readonly Regex HeaderName = new(@"^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex VariableName = new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    // The HTTP client owns these; letting a connector set them could break framing or the MCP session.
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Content-Type", "Accept", "Connection", "Transfer-Encoding", "Mcp-Session-Id", "MCP-Protocol-Version",
    };

    private const string HeaderShape = "Write each header as Name: value on its own line.";
    private const string VariableShape = "Write each variable as NAME=value on its own line.";

    /// <summary>"Name: value" lines. Authorization is refused when the connector signs in or uses a key, since those own it.</summary>
    public static bool TryParseHeaders(string? text, ConnectorAuth auth, out Dictionary<string, string> headers, out string error)
    {
        headers = new(StringComparer.OrdinalIgnoreCase);
        error = "";
        var lines = Lines(text);
        if (lines.Count > MaxHeaders) return Fail(out headers, out error, "Up to 16 headers.");
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon < 0) return Fail(out headers, out error, HeaderShape);
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (!HeaderName.IsMatch(name) || value.Length is 0 or > MaxValue || value.Any(char.IsControl)) return Fail(out headers, out error, HeaderShape);
            if (ReservedHeaders.Contains(name) || (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && auth != ConnectorAuth.None))
                return Fail(out headers, out error, $"Header {name} can't be set here.");
            if (!headers.TryAdd(name, value)) return Fail(out headers, out error, $"Header {name} is listed twice.");
        }
        return true;
    }

    /// <summary>"NAME=value" lines; the value is everything after the first "=".</summary>
    public static bool TryParseEnvironment(string? text, out Dictionary<string, string> env, out string error)
    {
        env = new(StringComparer.OrdinalIgnoreCase); // Windows environment names are case-insensitive
        error = "";
        var lines = Lines(text);
        if (lines.Count > MaxVariables) return Fail(out env, out error, "Up to 32 variables.");
        foreach (var line in lines)
        {
            var equals = line.IndexOf('=');
            if (equals < 0) return Fail(out env, out error, VariableShape);
            var name = line[..equals];
            var value = line[(equals + 1)..];
            if (!VariableName.IsMatch(name) || value.Length > MaxValue || value.Any(char.IsControl)) return Fail(out env, out error, VariableShape);
            if (!env.TryAdd(name, value)) return Fail(out env, out error, $"Variable {name} is listed twice.");
        }
        return true;
    }

    /// <summary>One argument per line; blank lines are dropped.</summary>
    public static bool TryParseArguments(string? text, out List<string> args, out string error)
    {
        args = [];
        error = "";
        var lines = Lines(text);
        if (lines.Count > MaxArguments) return Fail(out args, out error, "Up to 32 arguments.");
        if (lines.Any(l => l.Length > MaxArgument || l.Any(char.IsControl)))
            return Fail(out args, out error, "Each argument must be up to 1,000 characters, without control characters.");
        args = lines;
        return true;
    }

    public static bool TryValidateCommand(string? command, out string clean, out string error)
    {
        clean = command?.Trim() ?? "";
        error = "";
        if (clean.Length is 0 or > MaxCommand || clean.Any(char.IsControl))
        {
            clean = "";
            error = "Give the command to run, like npx.";
            return false;
        }
        return true;
    }

    /// <summary>A blank label is fine and means "no label" (clean is null).</summary>
    public static bool TryValidateLabel(string? label, out string? clean, out string error)
    {
        clean = null;
        error = "";
        var trimmed = label?.Trim() ?? "";
        if (trimmed.Length > MaxLabel || trimmed.Any(char.IsControl))
        {
            error = "Keep the label to 30 characters.";
            return false;
        }
        clean = trimmed.Length == 0 ? null : trimmed;
        return true;
    }

    /// <summary>
    /// The header a typed API key goes in (<see cref="SettingsActionArgs.ApiKeyHeader"/>: Authorization by default, with
    /// "Bearer " added), checked like any header and refused when <paramref name="others"/> already sets it.
    /// </summary>
    public static bool TryKeyHeader(string? headerName, string secret, IReadOnlyDictionary<string, string>? others,
        out string name, out string value, out string error)
    {
        (name, value) = SettingsActionArgs.ApiKeyHeader(headerName, secret);
        if (!TryParseHeaders(name + ": " + value, ConnectorAuth.None, out var parsed, out error) || parsed.Count != 1 || !parsed.ContainsKey(name))
        {
            if (error.Length == 0) error = HeaderShape;
        }
        else if (others?.ContainsKey(name) == true) error = $"Header {name} is listed twice.";
        else return true;
        name = value = "";
        return false;
    }

    /// <summary>
    /// The connector.label arg <c>{"key":…,"label":…}</c>; a missing or null label clears it. A malformed arg fails with an
    /// empty <paramref name="error"/> (nothing to tell the user); a label that is too long fails with its message.
    /// </summary>
    public static bool TryParseLabelArg(string? arg, out string key, out string? label, out string error)
    {
        key = "";
        label = null;
        error = "";
        if (string.IsNullOrWhiteSpace(arg)) return false;
        try
        {
            using var document = JsonDocument.Parse(arg, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("key", out var k) || k.ValueKind != JsonValueKind.String
                || ShellIntentParser.ReadString(k) is not { Length: > 0 } keyText) return false;
            string? text = null;
            if (root.TryGetProperty("label", out var l) && l.ValueKind != JsonValueKind.Null)
            {
                if (l.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(l) is not { } read) return false;
                text = read;
            }
            if (!TryValidateLabel(text, out label, out error)) return false;
            key = keyText;
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Non-blank, trimmed lines.</summary>
    private static List<string> Lines(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private static bool Fail<T>(out T empty, out string error, string message) where T : new()
    {
        empty = new T();
        error = message;
        return false;
    }
}
