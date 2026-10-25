using System.Security.Cryptography;
using System.Text;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Proactive;

/// <summary>Notch cards for the proactive layer. Nothing here acts: every action is the user's click.</summary>
public static class ProactiveCards
{
    public const string BriefId = "proactive-brief";
    public const string MeetingPrefix = "proactive-meeting-", SuggestionPrefix = "proactive-suggestion-", NudgePrefix = "proactive-nudge-";
    public const string PlanAction = "plan", JoinAction = "join", OpenAction = "open", DoItAction = "doit",
        NotNowAction = "notnow", NeverAction = "never", ConnectAction = "connect";
    public const int NudgeMs = 18000, TitleChars = 160;

    public static bool IsProactive(string cardId) => cardId.StartsWith("proactive-", StringComparison.Ordinal);

    public static NotchCard Brief(string title, IReadOnlyList<string> lines) => new(BriefId, NotchCardKind.Tip, title,
        string.Join("\n", lines), [new(PlanAction, "Plan my day", CardActionStyle.Primary)]);

    /// <summary>The countdown card: Join for a safe meeting link, else Open for a safe calendar link, else no action.</summary>
    public static NotchCard Meeting(string cardId, UpcomingEvent e, DateTimeOffset now)
    {
        var title = Clean(e.Title, TitleChars);
        CardAction[] actions = MeetingCountdown.SafeUrl(e.MeetingUrl) is not null ? [new(JoinAction, "Join", CardActionStyle.Primary)]
            : MeetingCountdown.SafeUrl(e.HtmlLink) is not null ? [new(OpenAction, "Open", CardActionStyle.Secondary)]
            : [];
        return new(cardId, NotchCardKind.Activity, title.Length == 0 ? "Meeting" : title, MeetingCountdown.Text(e.StartsAt, now), actions);
    }

    /// <summary>The connect nudge for an app the user has spent a while in. Connect opens Settings only on the click.</summary>
    public static NotchCard Nudge(NudgeApp app) => new(NudgePrefix + app.Id, NotchCardKind.Tip, $"Connect {app.Name}?",
        $"You've spent a while in {app.Name}. Connect it in Integrations so I can help with it.",
        [new(ConnectAction, "Connect", CardActionStyle.Primary), new(NotNowAction, "Not now", CardActionStyle.Secondary),
         new(NeverAction, "Never ask", CardActionStyle.Secondary)], NudgeMs);

    /// <summary>One card per event, stable across ticks; the event id is hashed so a huge id cannot blow the id cap.</summary>
    public static string MeetingCardId(string eventId) =>
        MeetingPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventId)))[..16].ToLowerInvariant();

    /// <summary>
    /// Makes untrusted text safe for a card: control characters and bidi overrides removed, whitespace runs
    /// (including newlines and tabs) become one space, trimmed, cut to <paramref name="max"/> without splitting a surrogate pair.
    /// </summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || max <= 0) return "";
        var sb = new StringBuilder(Math.Min(text.Length, max + 1));
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
            else if (!char.IsControl(c) && c is not (>= '‪' and <= '‮') and not (>= '⁦' and <= '⁩')) sb.Append(c);
            if (sb.Length > max) break; // ponytail: stops early; a cleaned string never needs more than max + 1 chars
        }
        var s = sb.ToString().TrimEnd();
        if (s.Length > max)
        {
            var cut = max;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            s = s[..cut].TrimEnd();
        }
        return s;
    }
}
