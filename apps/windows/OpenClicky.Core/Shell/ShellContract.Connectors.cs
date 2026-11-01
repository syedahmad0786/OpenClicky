using System.Text.Json.Serialization;

namespace OpenClicky.Core.Shell;

/// <summary>What a connector row runs: a remote server, a local command, or an Obsidian vault.</summary>
public enum ConnectorViewKind { Remote, Local, Vault }

/// <summary>How a connector signs in. Names only; the page never sees a token, key, header or environment value.</summary>
public enum ConnectorViewAuth { Browser, ApiKey, None }

/// <summary>One built-in app on the Integrations page.</summary>
public sealed record CatalogView(string Id, string Name, string Description, CatalogSetup Setup, bool MultiAccount);

public sealed partial record ConnectorView
{
    /// <summary>The catalog app this account belongs to; null for a custom connector.</summary>
    [JsonPropertyOrder(1)] public string? App { get; init; }
    /// <summary>The account label (Work, Personal…); at most 30 chars.</summary>
    [JsonPropertyOrder(2)] public string? Label { get; init; }
    [JsonPropertyOrder(3)] public ConnectorViewKind Kind { get; init; } = ConnectorViewKind.Remote;
    [JsonPropertyOrder(4)] public ConnectorViewAuth Auth { get; init; } = ConnectorViewAuth.None;
    /// <summary>ISO-8601 time the connector was added, or null.</summary>
    [JsonPropertyOrder(5)] public string? Since { get; init; }
}

public sealed partial record SettingsIntegrations
{
    public IReadOnlyList<CatalogView> Catalog { get; init; } = [];
}
