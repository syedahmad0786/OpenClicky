namespace OpenClicky.Core;

/// <summary>How an app is set up: browser sign-in, a typed key, nothing, the user's own address, or a folder on this PC.</summary>
public enum CatalogSetup { SignIn, ApiKey, None, Url, Folder }

/// <param name="Url">The server's MCP address; null for <see cref="CatalogSetup.Url"/> (the user supplies it) and <see cref="CatalogSetup.Folder"/>.</param>
/// <param name="ApiKeyHeader">The header the key goes in, for <see cref="CatalogSetup.ApiKey"/> and <see cref="CatalogSetup.Url"/> apps.</param>
/// <param name="MultiAccount">True when several labelled accounts of this app make sense (sign-in and key apps).</param>
public sealed record CatalogApp(string Id, string Name, string Description, CatalogSetup Setup, string? Url, string? ApiKeyHeader, bool MultiAccount);

/// <summary>The built-in list of public MCP servers, in page order. No logos or third-party art; rows use the name.</summary>
public static class ConnectorCatalog
{
    public static IReadOnlyList<CatalogApp> Apps { get; } =
    [
        SignIn("linear", "Linear", "Issues, projects and cycles.", "https://mcp.linear.app/mcp"),
        SignIn("notion", "Notion", "Pages, databases and search across your workspace.", "https://mcp.notion.com/mcp"),
        SignIn("atlassian", "Atlassian", "Jira issues and Confluence pages.", "https://mcp.atlassian.com/v2/mcp"),
        SignIn("monday", "monday.com", "Boards, items and updates.", "https://mcp.monday.com/mcp"),
        new("github", "GitHub", "Repositories, issues and pull requests. Uses a personal access token.", CatalogSetup.ApiKey, "https://api.githubcopilot.com/mcp/", "Authorization", true),
        SignIn("sentry", "Sentry", "Errors, issues and releases.", "https://mcp.sentry.dev/mcp"),
        SignIn("vercel", "Vercel", "Projects, deployments and logs.", "https://mcp.vercel.com/"),
        SignIn("supabase", "Supabase", "Databases, tables and projects.", "https://mcp.supabase.com/mcp"),
        SignIn("stripe", "Stripe", "Payments, customers and documentation.", "https://mcp.stripe.com/"),
        SignIn("canva", "Canva", "Find, create and export designs.", "https://mcp.canva.com/mcp"),
        SignIn("intercom", "Intercom", "Conversations and contacts.", "https://mcp.intercom.com/mcp"),
        SignIn("huggingface", "Hugging Face", "Models, datasets and Spaces.", "https://huggingface.co/mcp"),
        Open("deepwiki", "DeepWiki", "Questions about public GitHub repositories. No account needed.", "https://mcp.deepwiki.com/mcp"),
        Open("microsoftlearn", "Microsoft Learn", "Microsoft documentation. No account needed.", "https://learn.microsoft.com/api/mcp"),
        Open("context7", "Context7", "Up-to-date library documentation. No account needed.", "https://mcp.context7.com/mcp"),
        new("composio", "Composio", "Your own Composio MCP address and key.", CatalogSetup.Url, null, "x-api-key", false),
        new("obsidian", "Obsidian", "Notes in a vault on this PC. No account needed.", CatalogSetup.Folder, null, null, false),
    ];

    public static CatalogApp? Find(string? id) => id is null ? null : Apps.FirstOrDefault(a => a.Id == id);

    private static CatalogApp SignIn(string id, string name, string description, string url) =>
        new(id, name, description, CatalogSetup.SignIn, url, null, true);

    private static CatalogApp Open(string id, string name, string description, string url) =>
        new(id, name, description, CatalogSetup.None, url, null, false);
}
