using System.Text.RegularExpressions;

namespace OpenClicky.Core;

public static partial class AutomaticMemory
{
    private const int MaxSourceLength = 4_000;
    private static readonly HashSet<string> AllowedFacts = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "timezone", "time zone", "pronouns", "role", "job title", "company", "business",
        "project", "current project", "deadline", "preferred language",
    };

    [GeneratedRegex(@"(?:^|[.!?]\s+)I prefer\s+([^.!?\r\n]{1,240})(?:[.!?]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Preference();

    [GeneratedRegex(@"(?:^|[.!?]\s+)my\s+([a-z][a-z ]{0,29})\s+is\s+([^.!?\r\n]{1,240})(?:[.!?]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatedFact();

    [GeneratedRegex(@"\b(password|passcode|pin|secret|token|api[\s-]*key|credential|card\s*number|cvv|social\s*security|ssn|private\s*key|recovery\s*(?:code|phrase))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sensitive();

    public static IReadOnlyList<CompanionMemoryItem> LearnFromTurn(
        ConversationTurn turn,
        string? memoryPath = null)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if (turn.Id <= 0 || string.IsNullOrWhiteSpace(turn.User) || turn.User.Length > MaxSourceLength || Sensitive().IsMatch(turn.User)) return [];

        var suggestions = new List<string>(1);
        if (Preference().Match(turn.User) is { Success: true } preference)
            suggestions.Add("Prefers " + preference.Groups[1].Value.Trim() + ".");
        else if (StatedFact().Match(turn.User) is { Success: true } stated
            && AllowedFacts.Contains(stated.Groups[1].Value.Trim()))
        {
            var field = stated.Groups[1].Value.Trim().ToLowerInvariant();
            suggestions.Add($"{char.ToUpperInvariant(field[0])}{field[1..]}: {stated.Groups[2].Value.Trim()}.");
        }

        var existing = CompanionMemoryStore.Load(memoryPath).Memory?.Facts ?? [];
        var learned = new List<CompanionMemoryItem>();
        foreach (var text in suggestions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Count + learned.Count >= CompanionMemoryStore.MaxFacts) break;
            if (existing.Any(item => item.Text.Equals(text, StringComparison.OrdinalIgnoreCase))) continue;
            var item = CompanionMemoryStore.AddSourceLinkedFact(text, "conversation", turn.Id.ToString(), memoryPath);
            if (item is not null) learned.Add(item);
        }
        return learned;
    }
}
