using System.Text.Json;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core;

/// <summary>A connector the page asked to add (settings.action connector.add), already validated.</summary>
public abstract record ConnectorRequest
{
    /// <summary>Longest connector.add arg. Every other settings action stays at the shell's smaller cap.</summary>
    public const int MaxArg = 8_000;

    private const string Default = "That connector could not be added.";

    private protected ConnectorRequest() { }

    /// <summary>
    /// Parses the connector.add arg. All fields are strings: app, label (catalog account); name, kind, url, auth,
    /// clientId, headers (remote); name, kind, command, args, env (local). Header and variable values travel here once
    /// and are never echoed back; on failure only a fixed message comes out.
    /// </summary>
    public static bool TryParse(string? arg, out ConnectorRequest? request, out string error)
    {
        request = null;
        error = Default;
        if (string.IsNullOrWhiteSpace(arg) || arg.Length > MaxArg) return false;
        try
        {
            using var document = JsonDocument.Parse(arg, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !Text(root, "app", out var app) || !Text(root, "label", out var label) || !Text(root, "name", out var name)
                || !Text(root, "kind", out var kind) || !Text(root, "url", out var url) || !Text(root, "auth", out var authText)
                || !Text(root, "clientId", out var clientId) || !Text(root, "headers", out var headers)
                || !Text(root, "command", out var command) || !Text(root, "args", out var args) || !Text(root, "env", out var env))
                return false;

            if (!string.IsNullOrWhiteSpace(app))
            {
                var catalog = ConnectorCatalog.Find(app.Trim());
                if (catalog is null) { error = "That app isn't in the list."; return false; }
                if (catalog.Setup == CatalogSetup.Folder) { request = new VaultSetup(); return true; }
                if (catalog.Setup != CatalogSetup.Url)
                {
                    if (!ConnectorFields.TryValidateLabel(label, out var clean, out error)) return false;
                    request = new CatalogAccount(catalog, clean);
                    return true;
                }
                kind = "remote"; // composio: your own address and key
                app = catalog.Id;
            }
            else app = null;

            name = name?.Trim();
            if (name is not { Length: > 0 and <= 60 } || name.Any(char.IsControl)) { error = "Give the connector a name of up to 60 characters."; return false; }

            switch (kind)
            {
                case null or "" or "remote":
                    return TryRemote(name, url, authText, clientId, headers, app, out request, out error);
                case "local":
                    if (!ConnectorFields.TryValidateCommand(command, out var clean, out error)
                        || !ConnectorFields.TryParseArguments(args, out var argList, out error)
                        || !ConnectorFields.TryParseEnvironment(env, out var variables, out error)) return false;
                    request = new CustomLocal(name, clean, argList, variables);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return false; }
    }

    private static bool TryRemote(string name, string? url, string? authText, string? clientId, string? headerText, string? app,
        out ConnectorRequest? request, out string error)
    {
        request = null;
        error = Default;
        ConnectorAuth? auth = authText switch
        {
            "none" => ConnectorAuth.None,
            "apiKey" => ConnectorAuth.ApiKey,
            "browser" => ConnectorAuth.Browser,
            _ => null,
        };
        if (auth is null || string.IsNullOrWhiteSpace(url)) return false;
        try { url = ConnectorRegistry.ValidateUrl(url).AbsoluteUri; }
        catch (ArgumentException e) { error = SettingsActionArgs.UserMessage(e); return false; }
        clientId = clientId?.Trim();
        if (clientId is { Length: > 500 } || clientId?.Any(char.IsControl) == true) return false;
        if (auth != ConnectorAuth.Browser || string.IsNullOrEmpty(clientId)) clientId = null;
        if (!ConnectorFields.TryParseHeaders(headerText, auth.Value, out var headers, out error)) return false;
        request = new CustomRemote(name, url, auth.Value, clientId, headers, app);
        return true;
    }

    /// <summary>A missing field reads as null; a field that is present but not a string makes the whole request invalid.</summary>
    private static bool Text(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = ShellIntentParser.ReadString(element);
        return value is not null;
    }
}

/// <summary>One more account of a catalog app (sign-in, key or no auth), with an optional label.</summary>
public sealed record CatalogAccount(CatalogApp App, string? Label) : ConnectorRequest;

/// <summary>A remote MCP server the user typed in (or Composio, when <paramref name="App"/> is "composio").</summary>
public sealed record CustomRemote(string Name, string Url, ConnectorAuth Auth, string? ClientId, IReadOnlyDictionary<string, string> Headers, string? App) : ConnectorRequest;

/// <summary>A local command that speaks MCP over stdin/stdout.</summary>
public sealed record CustomLocal(string Name, string Command, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment) : ConnectorRequest;

/// <summary>"Choose a vault folder": the Obsidian setup, handled by the host with a folder dialog.</summary>
public sealed record VaultSetup() : ConnectorRequest;
