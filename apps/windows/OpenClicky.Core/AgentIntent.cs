namespace OpenClicky.Core;

public static class AgentIntent
{
    /// <summary>
    /// Detects HeyClicky-style agent invocation phrases in a transcript.
    /// </summary>
    public static bool TryExtract(string transcript, out string task)
    {
        task = transcript.Trim();
        if (string.IsNullOrEmpty(task))
        {
            return false;
        }

        var lower = task.ToLowerInvariant();
        string[] prefixes =
        [
            "openclicky agent",
            "hey openclicky agent",
            "heyclicky agent",
            "hey clicky agent",
            "clicky agent",
            "agent mode",
            "agent,",
            "agent:",
        ];

        foreach (var prefix in prefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                task = task[prefix.Length..].Trim().TrimStart(',', ':', '-', ' ');
                return true;
            }
        }

        if (lower.StartsWith("agent ", StringComparison.Ordinal))
        {
            task = task[6..].Trim();
            return true;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(task,
            @"^(?:(?:please|can you|could you)\s+)?(?:find|search for)\s+(?:this |that |the |my |a )?(?:chat|conversation|message|email)\b.{0,2000}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100))) return true;

        // A clear app launch followed by an explicit "then" clause is one desktop task.
        // Keep general conjunctions and quoted/advisory language in conversation mode.
        if (System.Text.RegularExpressions.Regex.IsMatch(task,
            @"^(?:(?:okay|ok|hey)[, ]+)?(?:(?:please|can you|could you|i want you to)\s+)?(?:open|launch|start)\s+[\p{L}\p{N}][\p{L}\p{N} ._+-]{0,79}?(?:\s+for me)?(?:[.!?]\s+|,\s*)then\s+\S.{0,2000}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100))) return true;

        // Short direct launch requests can be spoken without an agent prefix.
        // Questions and compound requests stay in conversation unless explicitly sent as Agent.
        var launch = System.Text.RegularExpressions.Regex.Match(task,
            @"^(?:(?:okay|ok|hey)[, ]+)?(?:(?:please|can you|could you|i want you to)\s+)?(?:open|launch|start)\s+([\p{L}\p{N}][\p{L}\p{N} ._+-]{0,79}?)(?:\s+for me)?[.!?]?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
        if (launch.Success && !System.Text.RegularExpressions.Regex.IsMatch(launch.Groups[1].Value,
            @"\b(and|then|how|why|what|when|if)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100))) return true;
        return false;
    }
}
