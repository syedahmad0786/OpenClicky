namespace OpenClicky.Core;

/// <summary>
/// Soft safety gate: refuse mutating agent actions when the foreground window
/// title looks like banking / password managers / authenticators.
/// </summary>
public static class AgentDenyList
{
    private static readonly string[] DefaultTitleFragments =
    [
        "1password",
        "bitwarden",
        "lastpass",
        "dashlane",
        "keeper®",
        "keeper ",
        "password manager",
        "authenticator",
        "chase ",
        "bank of america",
        "wells fargo",
        "capital one",
        "paypal",
        "venmo",
        "coinbase",
        "kraken",
        "robinhood",
    ];

    /// <summary>Host forms of the bank defaults, so the browser's pre-navigation URL check (host only) catches them too.</summary>
    private static readonly string[] DefaultHostFragments = ["chase.com", "bankofamerica.com", "wellsfargo.com", "capitalone.com"];

    public static bool IsDenied(string? foregroundWindowTitle, IEnumerable<string>? extraFragments = null)
    {
        if (string.IsNullOrWhiteSpace(foregroundWindowTitle))
        {
            return false;
        }

        var haystack = foregroundWindowTitle.Trim().ToLowerInvariant();
        foreach (var fragment in DefaultTitleFragments)
        {
            if (haystack.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (extraFragments is null)
        {
            return false;
        }

        foreach (var fragment in extraFragments)
        {
            if (!string.IsNullOrWhiteSpace(fragment)
                && haystack.Contains(fragment.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string? MatchReason(string? foregroundWindowTitle, IEnumerable<string>? extraFragments = null)
    {
        if (string.IsNullOrWhiteSpace(foregroundWindowTitle))
        {
            return null;
        }

        var haystack = foregroundWindowTitle.Trim().ToLowerInvariant();
        foreach (var fragment in DefaultTitleFragments)
        {
            if (haystack.Contains(fragment, StringComparison.Ordinal))
            {
                return fragment.Trim();
            }
        }

        if (extraFragments is not null)
        {
            foreach (var fragment in extraFragments)
            {
                if (string.IsNullOrWhiteSpace(fragment))
                {
                    continue;
                }

                var needle = fragment.Trim().ToLowerInvariant();
                if (haystack.Contains(needle, StringComparison.Ordinal))
                {
                    return needle;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The defaults verbatim (their trailing spaces matter: "chase " must not match "purchases") and the bank hosts, then the user's
    /// non-empty extra fragments trimmed and lower-case. For the browser extension's check.
    /// </summary>
    public static IReadOnlyList<string> Fragments(IEnumerable<string>? extra) =>
        DefaultTitleFragments.Concat(DefaultHostFragments).Concat((extra ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim().ToLowerInvariant()))
            .Distinct().ToArray();

    public static IReadOnlyList<string> ParseExtraFragments(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split([',', '\n', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
