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

        return false;
    }
}
