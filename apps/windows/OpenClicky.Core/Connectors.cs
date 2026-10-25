using System.Globalization;
using System.Text.Json;
using OpenClicky.Core.Settings;

namespace OpenClicky.Core;

public enum ConnectorKind { Remote, Local }

/// <summary>
/// A connector row. Only non-secret metadata lives here (and in the database); the endpoint, headers, command line,
/// environment and tokens live only in the secret store (<see cref="ConnectorSecret"/>).
/// </summary>
public sealed record ConnectorInfo(string Key, string DisplayName, string Host, DateTimeOffset CreatedAt)
{
    public ConnectorKind Kind { get; init; } = ConnectorKind.Remote;
    /// <summary>The catalog app id this account belongs to, or null for a custom connector.</summary>
    public string? App { get; init; }
    public string? Label { get; init; }
    public ConnectorAuth Auth { get; init; } = ConnectorAuth.None;
    public bool SignedIn { get; init; }
}

/// <summary>
/// Everything private about a connector: the full URL (it may carry account identifiers; "" for a local command), the
/// legacy single auth header, extra headers, the local command line and environment, and the browser sign-in state.
/// Old JSON blobs (Url/HeaderName/HeaderValue only) still deserialize.
/// </summary>
public sealed record ConnectorSecret(string Url, string? HeaderName, string? HeaderValue)
{
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public string? Command { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public string? OAuthClientId { get; init; }
    public McpOAuthTokens? OAuth { get; init; }
}

/// <summary>Tokens and the client details needed to refresh them. Stored inside <see cref="ConnectorSecret"/>, never elsewhere.</summary>
public sealed record McpOAuthTokens(
    string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string TokenEndpoint, string ClientId,
    string? ClientSecret, string? ClientAuthMethod, string Resource, string? Scope);

/// <summary>What <see cref="ConnectorRegistry.Add(NewConnector)"/> stores for one new connector or account.</summary>
public sealed record NewConnector(
    string DisplayName, ConnectorKind Kind, string Host, string? App, string? Label, ConnectorAuth Auth, ConnectorSecret Secret);

public sealed class ConnectorRegistry
{
    public const int MaxConnectors = 20;
    public const int MaxAccountsPerApp = 5;
    private const string Columns = "key,display_name,host,created_at_utc,kind,app,label,auth,signed_in";
    private readonly string _databasePath;
    private readonly ISecretStore _secrets;
    private readonly object _sync = new();

    /// <summary>Keys no connector row may take (the Obsidian vault row is built from settings, not from this table).</summary>
    public static IReadOnlySet<string> ReservedKeys { get; } = new HashSet<string>(StringComparer.Ordinal) { "obsidian" };

    public ConnectorRegistry(ISecretStore secrets, string? databasePath = null)
    {
        _secrets = secrets;
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky", "openclicky.db");
    }

    public static string SecretName(string key) => "connector-" + key;

    public IReadOnlyList<ConnectorInfo> List()
    {
        lock (_sync)
        {
            using var connection = Open();
            return Read(connection);
        }
    }

    /// <summary>Adds a remote server with at most one custom header. Kept for the classic window; the Integrations page uses <see cref="Add(NewConnector)"/>.</summary>
    public ConnectorInfo Add(string displayName, string url, string? headerName, string? headerValue)
    {
        displayName = displayName.Trim();
        if (displayName.Length is 0 or > 60) throw new ArgumentException("Give the connector a name of up to 60 characters.", nameof(displayName));
        var endpoint = ValidateUrl(url);
        headerName = string.IsNullOrWhiteSpace(headerName) ? null : headerName.Trim();
        headerValue = string.IsNullOrWhiteSpace(headerValue) ? null : headerValue.Trim();
        if ((headerName is null) != (headerValue is null)) throw new ArgumentException("Enter both the header name and its value, or neither.");
        if (headerName is not null && (headerName.Length > 64 || !headerName.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("The header name may use only letters, digits and hyphens.", nameof(headerName));
        if (headerValue is { Length: > 4_000 } || headerValue?.Any(char.IsControl) == true)
            throw new ArgumentException("The header value is too long or contains control characters.", nameof(headerValue));
        return Add(new NewConnector(displayName, ConnectorKind.Remote, endpoint.Host, null, null,
            headerName is null ? ConnectorAuth.None : ConnectorAuth.ApiKey, new ConnectorSecret(endpoint.AbsoluteUri, headerName, headerValue)));
    }

    public ConnectorInfo Add(NewConnector spec)
    {
        var displayName = spec.DisplayName.Trim();
        if (displayName.Length is 0 or > 60) throw new ArgumentException("Give the connector a name of up to 60 characters.", nameof(spec));
        if (!ConnectorFields.TryValidateLabel(spec.Label, out var label, out var labelError)) throw new ArgumentException(labelError, nameof(spec));
        var secret = spec.Secret;
        if (spec.Kind == ConnectorKind.Remote) secret = secret with { Url = ValidateUrl(secret.Url).AbsoluteUri };
        else if (!ConnectorFields.TryValidateCommand(secret.Command, out var commandLine, out var commandError)) throw new ArgumentException(commandError, nameof(spec));
        else secret = secret with { Url = "", Command = commandLine };
        lock (_sync)
        {
            using var connection = Open();
            var existing = Read(connection);
            if (existing.Count >= MaxConnectors) throw new InvalidOperationException($"Up to {MaxConnectors} connectors can be added.");
            if (spec.App is not null && existing.Count(c => c.App == spec.App) >= MaxAccountsPerApp)
                throw new InvalidOperationException($"Up to {MaxAccountsPerApp} accounts can be connected for {ConnectorCatalog.Find(spec.App)?.Name ?? spec.App}.");
            var baseKey = WorkStore.Slugify(spec.App ?? displayName).Replace('-', '_');
            var key = baseKey;
            for (var suffix = 2; ReservedKeys.Contains(key) || existing.Any(c => c.Key == key); suffix++) key = $"{baseKey}_{suffix}";
            _secrets.Set(SecretName(key), JsonSerializer.Serialize(secret));
            var info = new ConnectorInfo(key, displayName, spec.Host, DateTimeOffset.UtcNow)
            {
                Kind = spec.Kind, App = spec.App, Label = label, Auth = spec.Auth,
            };
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO connectors(" + Columns + ") VALUES($key,$name,$host,$at,$kind,$app,$label,$auth,0)";
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$name", displayName);
            insert.Parameters.AddWithValue("$host", info.Host);
            insert.Parameters.AddWithValue("$at", info.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$kind", info.Kind.ToString().ToLowerInvariant());
            insert.Parameters.AddWithValue("$app", (object?)info.App ?? DBNull.Value);
            insert.Parameters.AddWithValue("$label", (object?)info.Label ?? DBNull.Value);
            insert.Parameters.AddWithValue("$auth", info.Auth.ToString().ToLowerInvariant());
            insert.ExecuteNonQuery();
            return info;
        }
    }

    public ConnectorSecret? Secret(string key)
    {
        var json = _secrets.Get(SecretName(key));
        return json is null ? null : JsonSerializer.Deserialize<ConnectorSecret>(json);
    }

    /// <summary>Replaces the stored secret (a token refresh, a new sign-in). Does nothing when the connector was removed meanwhile, so a late refresh cannot resurrect a secret.</summary>
    public void SaveSecret(string key, ConnectorSecret secret)
    {
        lock (_sync)
        {
            using var connection = Open();
            if (!Read(connection).Any(c => c.Key == key)) return;
            _secrets.Set(SecretName(key), JsonSerializer.Serialize(secret));
        }
    }

    /// <summary>
    /// As <see cref="SaveSecret(string, ConnectorSecret)"/>, but only while <paramref name="row"/> itself is still there: a
    /// flow that outlived its connector must not write into a newer one that reused the key.
    /// </summary>
    public void SaveSecret(ConnectorInfo row, ConnectorSecret secret)
    {
        lock (_sync)
        {
            using var connection = Open();
            if (!IsCurrent(Read(connection), row)) return;
            _secrets.Set(SecretName(row.Key), JsonSerializer.Serialize(secret));
        }
    }

    /// <summary>True when <paramref name="rows"/> still hold this very row: same key and same creation time (keys are reused after a remove).</summary>
    public static bool IsCurrent(IEnumerable<ConnectorInfo> rows, ConnectorInfo row) =>
        rows.Any(r => r.Key == row.Key && r.CreatedAt == row.CreatedAt);

    /// <summary>Renames the account label; null or blank clears it.</summary>
    public void SetLabel(string key, string? label)
    {
        if (!ConnectorFields.TryValidateLabel(label, out var clean, out var error)) throw new ArgumentException(error, nameof(label));
        Update(key, "label", (object?)clean ?? DBNull.Value);
    }

    public void SetSignedIn(string key, bool signedIn) => Update(key, "signed_in", signedIn ? 1 : 0);

    public void Remove(string key)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM connectors WHERE key=$key";
            command.Parameters.AddWithValue("$key", key);
            command.ExecuteNonQuery();
            _secrets.Delete(SecretName(key));
        }
    }

    /// <summary>HTTPS anywhere, or plain HTTP only to this PC for a locally run MCP server.</summary>
    public static Uri ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new ArgumentException("Use the connector's https:// MCP address (http:// only for a server on this PC).", nameof(url));
        return uri;
    }

    // column is always one of our own literals, never user text.
    private void Update(string key, string column, object value)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE connectors SET {column}=$value WHERE key=$key";
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$key", key);
            command.ExecuteNonQuery();
        }
    }

    private static List<ConnectorInfo> Read(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + Columns + " FROM connectors ORDER BY created_at_utc";
        using var reader = command.ExecuteReader();
        var list = new List<ConnectorInfo>();
        while (reader.Read())
            list.Add(new ConnectorInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            {
                Kind = Enum.TryParse<ConnectorKind>(reader.GetString(4), true, out var kind) ? kind : ConnectorKind.Remote,
                App = reader.IsDBNull(5) ? null : reader.GetString(5),
                Label = reader.IsDBNull(6) ? null : reader.GetString(6),
                Auth = Enum.TryParse<ConnectorAuth>(reader.GetString(7), true, out var auth) ? auth : ConnectorAuth.None,
                SignedIn = reader.GetInt64(8) != 0,
            });
        return list;
    }

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var connection = LocalSqliteStore.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS connectors(key TEXT PRIMARY KEY, display_name TEXT NOT NULL, host TEXT NOT NULL, created_at_utc TEXT NOT NULL);";
        command.ExecuteNonQuery();
        // Databases made before accounts and labels have only the four columns above: add whatever is missing.
        var have = new HashSet<string>(StringComparer.Ordinal);
        command.CommandText = "PRAGMA table_info(connectors)";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) have.Add(reader.GetString(1));
        foreach (var (name, definition) in new (string, string)[]
        {
            ("kind", "TEXT NOT NULL DEFAULT 'remote'"), ("app", "TEXT"), ("label", "TEXT"),
            ("auth", "TEXT NOT NULL DEFAULT 'none'"), ("signed_in", "INTEGER NOT NULL DEFAULT 0"),
        })
        {
            if (have.Contains(name)) continue;
            command.CommandText = $"ALTER TABLE connectors ADD COLUMN {name} {definition}";
            command.ExecuteNonQuery();
        }
        return connection;
    }
}
