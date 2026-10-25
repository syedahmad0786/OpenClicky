using System.Text;
using System.Text.Json;
using OpenClicky.Core.Settings;

namespace OpenClicky.Core.Tests;

public sealed class ConnectorRegistryTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "openclicky-connreg-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly MemorySecrets _secrets = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_db)!, Path.GetFileName(_db) + "*")) File.Delete(file);
    }

    private ConnectorRegistry Registry() => new(_secrets, _db);

    private static NewConnector Spec(string name, string? app = null, string? label = null, ConnectorAuth auth = ConnectorAuth.None, ConnectorSecret? secret = null) =>
        new(name, ConnectorKind.Remote, "mcp.example.test", app, label, auth, secret ?? new ConnectorSecret("https://mcp.example.test/mcp", null, null));

    [Fact]
    public void MigratesOldTable()
    {
        using (var old = LocalSqliteStore.Open(_db))
        {
            using var command = old.CreateCommand();
            command.CommandText = "CREATE TABLE connectors(key TEXT PRIMARY KEY, display_name TEXT NOT NULL, host TEXT NOT NULL, created_at_utc TEXT NOT NULL);" +
                                  "INSERT INTO connectors VALUES('old_one','Old One','old.example.test','2026-10-01T00:00:00.0000000+00:00');";
            command.ExecuteNonQuery();
        }

        var row = Assert.Single(Registry().List());
        Assert.Equal("old_one", row.Key);
        Assert.Equal(ConnectorKind.Remote, row.Kind);
        Assert.Equal(ConnectorAuth.None, row.Auth);
        Assert.False(row.SignedIn);
        Assert.Null(row.App);
        Assert.Null(row.Label);
        Assert.Single(Registry().List()); // a second open must not re-add the columns
    }

    [Fact]
    public void AddKeepsMetadataAndSecret()
    {
        var tokens = new McpOAuthTokens("at", "rt", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), "https://auth.example.test/token", "client-1", null, "none", "https://mcp.example.test/mcp", "read");
        var secret = new ConnectorSecret("https://mcp.example.test/mcp", null, null)
        {
            Headers = new Dictionary<string, string> { ["X-Team"] = "design" },
            OAuthClientId = "client-1",
            OAuth = tokens,
        };
        var registry = Registry();
        var added = registry.Add(new NewConnector("Linear", ConnectorKind.Remote, "mcp.linear.app", "linear", "Work", ConnectorAuth.Browser, secret));

        var listed = Assert.Single(registry.List());
        Assert.Equal(added, listed);
        Assert.Equal("linear", listed.Key);
        Assert.Equal("Linear", listed.DisplayName);
        Assert.Equal("mcp.linear.app", listed.Host);
        Assert.Equal(ConnectorKind.Remote, listed.Kind);
        Assert.Equal("linear", listed.App);
        Assert.Equal("Work", listed.Label);
        Assert.Equal(ConnectorAuth.Browser, listed.Auth);
        Assert.False(listed.SignedIn);

        var back = registry.Secret("linear")!;
        Assert.Equal("design", back.Headers!["X-Team"]);
        Assert.Equal("client-1", back.OAuthClientId);
        Assert.Equal(tokens, back.OAuth);
    }

    [Fact]
    public void LocalConnectorKeepsCommandArgumentsAndEnvironment()
    {
        var secret = new ConnectorSecret("", null, null)
        {
            Command = "npx",
            Arguments = ["-y", "@scope/server"],
            Environment = new Dictionary<string, string> { ["TOKEN"] = "s3cret-e" },
        };
        var registry = Registry();
        var added = registry.Add(new NewConnector("Files", ConnectorKind.Local, "npx", null, null, ConnectorAuth.None, secret));
        Assert.Equal(ConnectorKind.Local, added.Kind);
        var back = registry.Secret(added.Key)!;
        Assert.Equal("npx", back.Command);
        Assert.Equal(["-y", "@scope/server"], back.Arguments);
        Assert.Equal("s3cret-e", back.Environment!["TOKEN"]);
    }

    [Fact]
    public void KeysAvoidCollisionsAndReserved()
    {
        var registry = Registry();
        Assert.Equal("linear", registry.Add(Spec("Linear", "linear", "Work")).Key);
        Assert.Equal("linear_2", registry.Add(Spec("Linear", "linear", "Personal")).Key);
        Assert.Equal("obsidian_2", registry.Add(Spec("Obsidian")).Key);
        Assert.Contains("obsidian", ConnectorRegistry.ReservedKeys);
    }

    [Fact]
    public void FiveAccountsPerApp()
    {
        var registry = Registry();
        for (var i = 0; i < ConnectorRegistry.MaxAccountsPerApp; i++) registry.Add(Spec("Linear", "linear", "Acct " + i));
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Add(Spec("Linear", "linear", "Sixth")));
        Assert.Equal("Up to 5 accounts can be connected for Linear.", ex.Message);
        registry.Add(Spec("Notion", "notion")); // other apps are unaffected
    }

    [Fact]
    public void TotalConnectorCapStillApplies()
    {
        var registry = Registry();
        for (var i = 0; i < ConnectorRegistry.MaxConnectors; i++) registry.Add(Spec("Custom " + i));
        Assert.Throws<InvalidOperationException>(() => registry.Add(Spec("One more")));
    }

    [Fact]
    public void SetLabelAndSignedIn()
    {
        var registry = Registry();
        var key = registry.Add(Spec("Linear", "linear", "Work", ConnectorAuth.Browser)).Key;

        registry.SetLabel(key, " Personal ");
        registry.SetSignedIn(key, true);
        var row = Assert.Single(Registry().List());
        Assert.Equal("Personal", row.Label);
        Assert.True(row.SignedIn);

        registry.SetLabel(key, null);
        registry.SetSignedIn(key, false);
        row = Assert.Single(Registry().List());
        Assert.Null(row.Label);
        Assert.False(row.SignedIn);

        Assert.Throws<ArgumentException>(() => registry.SetLabel(key, new string('x', 31)));
    }

    [Fact]
    public void SaveSecretReplacesTheSecretAndSkipsRemovedConnectors()
    {
        var registry = Registry();
        var key = registry.Add(Spec("Linear", "linear", "Work", ConnectorAuth.Browser)).Key;
        registry.SaveSecret(key, new ConnectorSecret("https://mcp.example.test/mcp", null, null) { OAuthClientId = "new-client" });
        Assert.Equal("new-client", registry.Secret(key)!.OAuthClientId);

        registry.Remove(key);
        registry.SaveSecret(key, new ConnectorSecret("https://mcp.example.test/mcp", null, null)); // a late token refresh
        Assert.Null(registry.Secret(key));
    }

    [Fact]
    public void ReusedKeyIsNotTheSameRow()
    {
        var registry = Registry();
        var old = registry.Add(Spec("Acme", auth: ConnectorAuth.Browser));
        Assert.True(ConnectorRegistry.IsCurrent(registry.List(), old));
        registry.Remove(old.Key);
        Thread.Sleep(2); // a re-add gets a later creation time
        var fresh = registry.Add(Spec("Acme", auth: ConnectorAuth.ApiKey, secret: new ConnectorSecret("https://mcp.example.test/mcp", "x-api-key", "new-key")));
        Assert.Equal(old.Key, fresh.Key);

        Assert.False(ConnectorRegistry.IsCurrent(registry.List(), old));
        Assert.True(ConnectorRegistry.IsCurrent(registry.List(), fresh));
        Assert.False(ConnectorRegistry.IsCurrent([], fresh));

        registry.SaveSecret(old, new ConnectorSecret("https://mcp.example.test/mcp", null, null) { OAuthClientId = "stale" }); // a late sign-in
        Assert.Equal("new-key", registry.Secret(fresh.Key)!.HeaderValue);
        Assert.Null(registry.Secret(fresh.Key)!.OAuthClientId);
        registry.SaveSecret(fresh, new ConnectorSecret("https://mcp.example.test/mcp", "x-api-key", "rotated"));
        Assert.Equal("rotated", registry.Secret(fresh.Key)!.HeaderValue);
    }

    [Fact]
    public void SecretsOnlyInSecretStore()
    {
        var secret = new ConnectorSecret("https://mcp.example.test/u/123?k=1", "X-Auth", "s3cret-h")
        {
            Headers = new Dictionary<string, string> { ["X-Key"] = "s3cret-h" },
            Environment = new Dictionary<string, string> { ["TOKEN"] = "s3cret-e" },
            OAuth = new McpOAuthTokens("s3cret-t", "s3cret-r", null, "https://auth.example.test/token", "client", "s3cret-c", "post", "https://mcp.example.test/u/123", null),
        };
        var registry = Registry();
        var key = registry.Add(new NewConnector("Custom", ConnectorKind.Remote, "mcp.example.test", null, "Work", ConnectorAuth.Browser, secret)).Key;
        registry.SetSignedIn(key, true);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var bytes = Directory.GetFiles(Path.GetDirectoryName(_db)!, Path.GetFileName(_db) + "*").SelectMany(File.ReadAllBytes).ToArray();
        var text = Encoding.UTF8.GetString(bytes);
        foreach (var needle in new[] { "s3cret-h", "s3cret-e", "s3cret-t", "s3cret-r", "s3cret-c", "/u/123" })
            Assert.DoesNotContain(needle, text);
        Assert.Contains("s3cret-t", _secrets.Get(ConnectorRegistry.SecretName(key)));
    }

    [Fact]
    public void OldSecretJsonStillReads()
    {
        var secret = JsonSerializer.Deserialize<ConnectorSecret>("""{"Url":"https://x.test/mcp","HeaderName":"Authorization","HeaderValue":"Bearer k"}""")!;
        Assert.Equal("https://x.test/mcp", secret.Url);
        Assert.Equal("Bearer k", secret.HeaderValue);
        Assert.Null(secret.Headers);
        Assert.Null(secret.Command);
        Assert.Null(secret.Arguments);
        Assert.Null(secret.Environment);
        Assert.Null(secret.OAuthClientId);
        Assert.Null(secret.OAuth);
    }

    [Fact]
    public void LegacyAddWrapperStillWorks()
    {
        var registry = Registry();
        var info = registry.Add("Work Gmail", "https://mcp.example.test/u/123?api_key=s3cret", "Authorization", "Bearer abc");
        Assert.Equal("work_gmail", info.Key);
        Assert.Equal(ConnectorAuth.ApiKey, info.Auth);
        Assert.Equal("Bearer abc", registry.Secret(info.Key)!.HeaderValue);
        Assert.Throws<ArgumentException>(() => registry.Add("Plain", "http://example.com/mcp", null, null));
        Assert.Throws<ArgumentException>(() => registry.Add("Half", "https://example.com/mcp", "Authorization", null));
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new();
        public string? Get(string name) => _values.GetValueOrDefault(name);
        public void Set(string name, string value) => _values[name] = value;
        public void Delete(string name) => _values.Remove(name);
    }
}
