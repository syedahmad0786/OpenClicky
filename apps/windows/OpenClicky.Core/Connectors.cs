using System.Globalization;
using System.Text.Json;

namespace OpenClicky.Core;

/// <summary>A user-added remote MCP server. The endpoint and credential live only in the secret store.</summary>
public sealed record ConnectorInfo(string Key, string DisplayName, string Host, DateTimeOffset CreatedAt);

/// <summary>Endpoint secret: the full URL (it may carry account identifiers) plus one optional auth header.</summary>
public sealed record ConnectorSecret(string Url, string? HeaderName, string? HeaderValue);

public sealed class ConnectorRegistry
{
    public const int MaxConnectors = 20;
    private readonly string _databasePath;
    private readonly ISecretStore _secrets;
    private readonly object _sync = new();

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
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key,display_name,host,created_at_utc FROM connectors ORDER BY created_at_utc";
            using var reader = command.ExecuteReader();
            var list = new List<ConnectorInfo>();
            while (reader.Read())
                list.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            return list;
        }
    }

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
        lock (_sync)
        {
            using var connection = Open();
            var existing = List();
            if (existing.Count >= MaxConnectors) throw new InvalidOperationException($"Up to {MaxConnectors} connectors can be added.");
            var baseKey = WorkStore.Slugify(displayName).Replace('-', '_');
            var key = baseKey;
            for (var suffix = 2; existing.Any(c => c.Key == key); suffix++) key = $"{baseKey}_{suffix}";
            _secrets.Set(SecretName(key), JsonSerializer.Serialize(new ConnectorSecret(endpoint.AbsoluteUri, headerName, headerValue)));
            var info = new ConnectorInfo(key, displayName, endpoint.Host, DateTimeOffset.UtcNow);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO connectors(key,display_name,host,created_at_utc) VALUES($key,$name,$host,$at)";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$name", displayName);
            command.Parameters.AddWithValue("$host", info.Host);
            command.Parameters.AddWithValue("$at", info.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
            return info;
        }
    }

    public ConnectorSecret? Secret(string key)
    {
        var json = _secrets.Get(SecretName(key));
        return json is null ? null : JsonSerializer.Deserialize<ConnectorSecret>(json);
    }

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

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var connection = LocalSqliteStore.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS connectors(key TEXT PRIMARY KEY, display_name TEXT NOT NULL, host TEXT NOT NULL, created_at_utc TEXT NOT NULL);";
        command.ExecuteNonQuery();
        return connection;
    }
}
