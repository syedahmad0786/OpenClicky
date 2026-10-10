using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenClicky.Core;

/// <summary>
/// Parses trailing Clicky-compatible POINT tags from model responses.
/// Format: [POINT:none] or [POINT:x,y:label] or [POINT:x,y:label:screenN]
/// Coordinates are in screenshot pixel space, origin top-left.
/// </summary>
public static partial class PointParser
{
    [GeneratedRegex(
        @"\[POINT:(?:none|(\d+)\s*,\s*(\d+)(?::([^\]:\s][^\]:]*?))?(?::screen(\d+))?)\]\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PointTagRegex();

    public static PointingParseResult Parse(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        var match = PointTagRegex().Match(responseText);
        if (!match.Success)
        {
            return new PointingParseResult(responseText.TrimEnd(), null, null, null, null, IsNone: false);
        }

        var spoken = responseText[..match.Index].TrimEnd();
        if (!match.Groups[1].Success)
        {
            return new PointingParseResult(spoken, null, null, null, null, IsNone: true);
        }

        var x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var label = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;
        int? screen = match.Groups[4].Success
            ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)
            : null;

        return new PointingParseResult(spoken, x, y, label, screen, IsNone: false);
    }
}
