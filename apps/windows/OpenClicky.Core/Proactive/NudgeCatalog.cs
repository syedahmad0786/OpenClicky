namespace OpenClicky.Core.Proactive;

/// <summary>An app OpenClicky can offer to connect: catalog id, display name, desktop process names (no ".exe"), connector host fragments.</summary>
public sealed record NudgeApp(string Id, string Name, IReadOnlyList<string> Processes, IReadOnlyList<string> Hosts);

/// <summary>The fixed list of apps the connect nudge knows about, and how a foreground window maps onto it.</summary>
public static class NudgeCatalog
{
    public static IReadOnlyList<NudgeApp> Apps { get; } =
    [
        new("notion", "Notion", ["Notion"], ["notion"]),
        new("linear", "Linear", ["Linear"], ["linear"]),
        new("github", "GitHub", ["GitHubDesktop"], ["github"]),
        new("gmail", "Gmail", [], ["gmail"]),
        new("googlecalendar", "Google Calendar", [], ["calendar"]),
        new("googledrive", "Google Drive", [], ["drive"]),
        new("googledocs", "Google Docs", [], ["docs.google"]),
        new("googlesheets", "Google Sheets", [], ["sheets"]),
        new("slack", "Slack", ["slack"], ["slack"]),
        new("jira", "Jira", [], ["atlassian", "jira"]),
        new("asana", "Asana", ["Asana"], ["asana"]),
        new("trello", "Trello", ["Trello"], ["trello"]),
        new("clickup", "ClickUp", ["ClickUp"], ["clickup"]),
        new("airtable", "Airtable", ["Airtable"], ["airtable"]),
        new("hubspot", "HubSpot", [], ["hubspot"]),
        new("salesforce", "Salesforce", [], ["salesforce"]),
        new("zendesk", "Zendesk", [], ["zendesk"]),
        new("discord", "Discord", ["Discord"], ["discord"]),
        new("dropbox", "Dropbox", ["Dropbox"], ["dropbox"]),
        new("zoom", "Zoom", ["Zoom"], ["zoom"]),
    ];

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" };

    private static readonly string[] TitleSeparators = [" - ", " | ", " – ", " — "];

    /// <summary>
    /// A desktop process name wins; otherwise a browser window whose title has a segment equal to an app name.
    /// Process names come without ".exe". Nothing is stored: the caller keeps only the returned app id.
    /// </summary>
    public static NudgeApp? Match(string? processName, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        var byProcess = Apps.FirstOrDefault(a => a.Processes.Any(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase)));
        if (byProcess is not null) return byProcess;
        if (!Browsers.Contains(processName) || string.IsNullOrWhiteSpace(windowTitle)) return null;
        // ponytail: segment equality is a heuristic (a tab titled "Gmail" in another site still matches); F's catalog can replace it.
        foreach (var segment in windowTitle.Split(TitleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var app = Apps.FirstOrDefault(a => a.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (app is not null) return app;
        }
        return null;
    }

    /// <summary>Connected when any connector's name contains the app name or its host contains one of the app's host fragments.</summary>
    public static bool IsConnected(NudgeApp app, IEnumerable<ConnectorRef> connectors) =>
        connectors.Any(c => (c.Name ?? "").Contains(app.Name, StringComparison.OrdinalIgnoreCase)
            || app.Hosts.Any(h => (c.Host ?? "").Contains(h, StringComparison.OrdinalIgnoreCase)));
}
