using System.Collections.Concurrent;
using System.Globalization;
using OpenClicky.App.Settings;
using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>
/// Connectors on the Integrations page (connector.add|signIn|label|test|remove) and for jobs. Secrets (URLs, keys, headers,
/// environment values, tokens) stay in the registry's secret store: never in a toast, a log line or the shell state.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private const string ObsidianKey = "obsidian";
    private static readonly TimeSpan ConnectorCheckTimeout = TimeSpan.FromSeconds(30);
    /// <summary>Latest real check per key this session (UI thread only); dropped on remove.</summary>
    private readonly Dictionary<string, ConnectorCheck> _connectorChecks = new(StringComparer.Ordinal);
    /// <summary>One token source per signed-in key, shared by checks and jobs so a rotated refresh token is used once.</summary>
    private readonly ConcurrentDictionary<string, McpTokenSource> _connectorTokens = new(StringComparer.Ordinal);
    /// <summary>Refreshes tokens for every signed-in connector; each sign-in gets its own <see cref="McpOAuth"/>.</summary>
    private readonly Lazy<McpOAuth> _connectorRefresh = new(() => new McpOAuth());
    /// <summary>The key whose browser sign-in is running (one at a time: the callback port is fixed). UI thread only.</summary>
    private string? _connectorSigningIn;

    private void ConnectorAction(string action, string? arg)
    {
        switch (action)
        {
            case "connector.add": AddConnector(arg); break;
            case "connector.signIn": _ = SignInConnectorAsync(arg, firstSignIn: false); break;
            case "connector.label": LabelConnector(arg); break;
            case "connector.test": _ = VerifyConnectorAsync(arg, announce: true); break;
            case "connector.remove": RemoveConnector(arg); break;
        }
    }

    /// <summary>The page rows: registry connectors with their display status, then the Obsidian vault when one is set.</summary>
    private IReadOnlyList<ConnectorView> ConnectorViews(IReadOnlyList<ConnectorInfo> connectors)
    {
        var views = connectors.Select(c =>
        {
            var check = _connectorChecks.GetValueOrDefault(c.Key);
            return new ConnectorView(c.Key, c.DisplayName, c.Host, ConnectorStatusRules.Display(c, check, c.Key == _connectorSigningIn), check?.ToolCount)
            {
                App = string.IsNullOrEmpty(c.App) ? null : c.App,
                Label = c.Label,
                Kind = c.Kind == ConnectorKind.Local ? ConnectorViewKind.Local : ConnectorViewKind.Remote,
                Auth = c.Auth switch { ConnectorAuth.Browser => ConnectorViewAuth.Browser, ConnectorAuth.ApiKey => ConnectorViewAuth.ApiKey, _ => ConnectorViewAuth.None },
                Since = c.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            };
        }).ToList();
        if (_settings.ObsidianVaultPath is { Length: > 0 } vault)
        {
            var valid = ObsidianVaultConnector.IsVault(vault);
            views.Add(new ConnectorView(ObsidianKey, "Obsidian", vault, valid ? ConnectorStatus.Connected : ConnectorStatus.Error,
                valid ? ObsidianVaultConnector.ToolCount : null)
            { App = ObsidianKey, Kind = ConnectorViewKind.Vault, Auth = ConnectorViewAuth.None, Since = null });
        }
        return views;
    }

    /// <summary>Opening the Integrations page checks, in parallel and quietly, every connector not checked this session.</summary>
    private void VerifyUncheckedConnectors()
    {
        IReadOnlyList<ConnectorInfo> all;
        try { all = _connectors.List(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("connector_load_error=" + ex.GetType().Name); return; }
        foreach (var info in all)
            if (!_connectorChecks.ContainsKey(info.Key) && info.Key != _connectorSigningIn && !(info.Auth == ConnectorAuth.Browser && !info.SignedIn))
                _ = VerifyConnectorAsync(info.Key, announce: false);
    }

    private void AddConnector(string? arg)
    {
        if (!ConnectorRequest.TryParse(arg, out var request, out var error)) { _shell?.AddToast(error); return; }
        try
        {
            switch (request)
            {
                case VaultSetup: ChooseObsidianVault(); return;
                case CatalogAccount { App: var app } account:
                    var url = app.Url!;
                    var host = ConnectorRegistry.ValidateUrl(url).Host;
                    switch (app.Setup)
                    {
                        case CatalogSetup.SignIn:
                            if (SignInBusy()) return;
                            var signIn = _connectors.Add(new NewConnector(app.Name, ConnectorKind.Remote, host, app.Id, account.Label,
                                ConnectorAuth.Browser, new ConnectorSecret(url, null, null)));
                            _ = SignInConnectorAsync(signIn.Key, firstSignIn: true);
                            break;
                        case CatalogSetup.ApiKey:
                            if (PromptKey($"{app.Name} key", app.ApiKeyHeader ?? "Authorization", null) is not { } key) return;
                            Added(_connectors.Add(new NewConnector(app.Name, ConnectorKind.Remote, host, app.Id, account.Label,
                                ConnectorAuth.ApiKey, new ConnectorSecret(url, key.Name, key.Value))));
                            break;
                        default:
                            Added(_connectors.Add(new NewConnector(app.Name, ConnectorKind.Remote, host, app.Id, account.Label,
                                ConnectorAuth.None, new ConnectorSecret(url, null, null))));
                            break;
                    }
                    break;
                case CustomRemote remote:
                    // Only the host is stored with the row; the full URL (it may carry account ids) stays in the secret.
                    var remoteHost = ConnectorRegistry.ValidateUrl(remote.Url).Host;
                    var secret = new ConnectorSecret(remote.Url, null, null) { Headers = remote.Headers, OAuthClientId = remote.ClientId };
                    switch (remote.Auth)
                    {
                        case ConnectorAuth.Browser:
                            if (SignInBusy()) return;
                            var custom = _connectors.Add(new NewConnector(remote.Name, ConnectorKind.Remote, remoteHost, remote.App, null, ConnectorAuth.Browser, secret));
                            _ = SignInConnectorAsync(custom.Key, firstSignIn: false); // a custom row keeps the typed config if sign-in fails
                            break;
                        case ConnectorAuth.ApiKey:
                            if (PromptKey($"{remote.Name} key", remote.App == "composio" ? "x-api-key" : "Authorization", remote.Headers) is not { } key) return;
                            Added(_connectors.Add(new NewConnector(remote.Name, ConnectorKind.Remote, remoteHost, remote.App, null, ConnectorAuth.ApiKey,
                                secret with { HeaderName = key.Name, HeaderValue = key.Value })));
                            break;
                        default:
                            Added(_connectors.Add(new NewConnector(remote.Name, ConnectorKind.Remote, remoteHost, remote.App, null, ConnectorAuth.None, secret)));
                            break;
                    }
                    break;
                case CustomLocal local:
                    if (StdioMcpConnector.Launch(local.Command, local.Arguments, Environment.GetEnvironmentVariable("PATH"),
                            Environment.GetEnvironmentVariable("PATHEXT"), out var launchError) is null)
                    { _shell?.AddToast(launchError); return; }
                    Added(_connectors.Add(new NewConnector(local.Name, ConnectorKind.Local, Path.GetFileName(local.Command), null, null, ConnectorAuth.None,
                        new ConnectorSecret("", null, null) { Command = local.Command, Arguments = local.Arguments, Environment = local.Environment })));
                    break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { _shell?.AddToast(SettingsActionArgs.UserMessage(ex)); }
        finally { RequestShellPush(); }

        void Added(ConnectorInfo info) => _ = VerifyConnectorAsync(info.Key, announce: true);
    }

    /// <summary>The masked key prompt; null when cancelled, left empty or refused (the refusal toasts).</summary>
    private (string Name, string Value)? PromptKey(string title, string headerDefault, IReadOnlyDictionary<string, string>? otherHeaders)
    {
        if (Modal(() => NativeSettings.PromptSecret(title, "Paste the connector's API key. It is saved with Windows Credential Manager, never in settings or logs.", headerDefault), null)
            is not { } entered || string.IsNullOrWhiteSpace(entered.Secret)) return null;
        if (ConnectorFields.TryKeyHeader(entered.Header, entered.Secret, otherHeaders, out var name, out var value, out var error)) return (name, value);
        _shell?.AddToast(error);
        return null;
    }

    private bool SignInBusy()
    {
        if (_connectorSigningIn is null) return false;
        _shell?.AddToast("Another sign-in is still waiting in your browser.");
        return true;
    }

    private void ChooseObsidianVault()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose your Obsidian vault folder", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (!string.IsNullOrWhiteSpace(_settings.ObsidianVaultPath)) dialog.InitialDirectory = _settings.ObsidianVaultPath;
        if (!Modal(() => { using var owner = NativeSettings.TopMostOwner(); return dialog.ShowDialog(owner) == DialogResult.OK; }, false)
            || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
        var folder = Path.GetFullPath(dialog.SelectedPath);
        if (!ObsidianVaultConnector.IsVault(folder))
        {
            _shell?.AddToast("That folder isn't an Obsidian vault. Choose the folder that holds your vault's .obsidian folder.");
            return;
        }
        if (NotchSettings.Change(s => s.ObsidianVaultPath = folder)) _shell?.AddToast("Obsidian vault connected. Agents can now read your notes.");
    }

    /// <summary>
    /// Browser sign-in for one key: the user signs in on the server's own page in their default browser. A failed or
    /// cancelled first sign-in of a catalog account removes that row; any other row stays, needing sign-in.
    /// </summary>
    private async Task SignInConnectorAsync(string? key, bool firstSignIn)
    {
        if (SignInBusy()) return;
        if (_connectors.List().FirstOrDefault(c => c.Key == key) is not { Auth: ConnectorAuth.Browser } info
            || _connectors.Secret(info.Key) is not { } secret)
        {
            _shell?.AddToast("That connector no longer exists.");
            RequestShellPush();
            return;
        }
        _connectorSigningIn = info.Key;
        RequestShellPush();
        _shell?.AddToast("Finish connecting in your browser.");
        var signedIn = false;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            limit.CancelAfter(TimeSpan.FromMinutes(5));
            using var oauth = new McpOAuth();
            var tokens = await Task.Run(() => oauth.SignInAsync(new Uri(secret.Url), secret.OAuthClientId, limit.Token));
            _connectors.SaveSecret(info.Key, secret with { OAuth = tokens });
            _connectors.SetSignedIn(info.Key, true);
            _connectorTokens.TryRemove(info.Key, out _);
            signedIn = true;
        }
        catch (McpOAuthException ex)
        {
            _shell?.AddToast(ex.Message);
            Forget();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("connector_signin_error=" + ex.GetType().Name);
            _shell?.AddToast("Sign-in failed: " + SettingsActionArgs.UserMessage(ex));
            Forget();
        }
        finally
        {
            _connectorSigningIn = null;
            RequestShellPush();
        }
        if (signedIn) await VerifyConnectorAsync(info.Key, announce: true);

        void Forget()
        {
            try
            {
                if (firstSignIn) { _connectors.Remove(info.Key); _connectorChecks.Remove(info.Key); }
                else _connectors.SetSignedIn(info.Key, false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("connector_signin_cleanup_error=" + ex.GetType().Name); }
        }
    }

    /// <summary>
    /// A real initialize + tools/list (30 s), opened exactly as for a job. Toasts "{Name}: {message}" only when the user
    /// started it (Test, add, sign-in); a page-open check stays quiet.
    /// </summary>
    private async Task VerifyConnectorAsync(string? key, bool announce)
    {
        if (key is null) return;
        if (key == ObsidianKey)
        {
            if (announce) _shell?.AddToast(ObsidianVaultConnector.IsVault(_settings.ObsidianVaultPath)
                ? $"Obsidian: {ObsidianVaultConnector.ToolCount} tools available."
                : "That folder isn't an Obsidian vault. Choose the folder that holds your vault's .obsidian folder.");
            return;
        }
        if (_connectors.List().FirstOrDefault(c => c.Key == key) is not { } info)
        {
            if (announce) _shell?.AddToast("That connector no longer exists.");
            RequestShellPush();
            return;
        }
        if (_connectorChecks.TryGetValue(info.Key, out var running) && running.Status == ConnectorStatus.Checking) return;
        _connectorChecks[info.Key] = new ConnectorCheck(ConnectorStatus.Checking, null, "");
        RequestShellPush();
        ConnectorCheck check;
        try
        {
            // Off the UI thread: a local command starts on its first request.
            check = await Task.Run(async () =>
            {
                using var connector = OpenConnector(info);
                if (connector is not null) return await ConnectorVerifier.CheckAsync(connector, info.Auth, ConnectorCheckTimeout, _lifetime.Token).ConfigureAwait(false);
                return new ConnectorCheck(ConnectorStatus.NeedsSignIn, null,
                    info.Auth == ConnectorAuth.Browser ? "Sign in to use it." : "It needs its address and key again.");
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("connector_check_error=" + ex.GetType().Name);
            var message = SettingsActionArgs.UserMessage(ex);
            check = new ConnectorCheck(ConnectorStatus.Error, null, message[..Math.Min(message.Length, 300)]);
        }
        // A check that finishes after its connector was removed must not leave a stale entry behind.
        if (_connectors.List().Any(c => c.Key == info.Key))
        {
            _connectorChecks[info.Key] = check;
            if (check.Status == ConnectorStatus.Reconnect) { _connectors.SetSignedIn(info.Key, false); _connectorTokens.TryRemove(info.Key, out _); }
        }
        else _connectorChecks.Remove(info.Key);
        if (announce) _shell?.AddToast($"{info.DisplayName}: {check.Message}");
        RequestShellPush();
    }

    private void LabelConnector(string? arg)
    {
        if (!ConnectorFields.TryParseLabelArg(arg, out var key, out var label, out var error))
        {
            if (error.Length > 0) _shell?.AddToast(error);
            else Log("settings: invalid connector label arg");
            return;
        }
        if (!_connectors.List().Any(c => c.Key == key)) _shell?.AddToast("That connector no longer exists.");
        else _connectors.SetLabel(key, label);
        RequestShellPush();
    }

    private void RemoveConnector(string? key)
    {
        if (key == ObsidianKey)
        {
            if (NotchSettings.Change(s => s.ObsidianVaultPath = null)) _shell?.AddToast("Obsidian vault disconnected.");
            return;
        }
        if (_connectors.List().FirstOrDefault(c => c.Key == key) is not { } info) { RequestShellPush(); return; }
        _connectors.Remove(info.Key);
        _connectorChecks.Remove(info.Key);
        _connectorTokens.TryRemove(info.Key, out _);
        _shell?.AddToast($"Removed {info.DisplayName}.");
        RequestShellPush();
    }

    /// <summary>
    /// Every usable connector for one job: remote (key or none), signed-in browser accounts (silent refresh), local
    /// commands and the vault. A browser account that is not signed in is skipped. The caller disposes the list.
    /// </summary>
    private Task<IReadOnlyList<McpConnector>> OpenConnectorsAsync(CancellationToken token) => Task.Run<IReadOnlyList<McpConnector>>(() =>
    {
        var list = new List<McpConnector>();
        try
        {
            foreach (var info in _connectors.List())
            {
                token.ThrowIfCancellationRequested();
                if (OpenConnector(info) is { } connector) list.Add(connector);
            }
            if (_settings.ObsidianVaultPath is { Length: > 0 } vault && ObsidianVaultConnector.IsVault(vault)) list.Add(new ObsidianVaultConnector(vault));
        }
        catch (OperationCanceledException) { foreach (var connector in list) connector.Dispose(); throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("connector_load_error=" + ex.GetType().Name); }
        return list;
    }, token);

    /// <summary>Builds one registry connector (thread-safe; touches no UI state). Null when it has no secret or needs sign-in.</summary>
    private McpConnector? OpenConnector(ConnectorInfo info)
    {
        if (_connectors.Secret(info.Key) is not { } secret) return null;
        if (info.Kind == ConnectorKind.Local) return new StdioMcpConnector(info.Key, info.DisplayName, secret) { Label = info.Label };
        if (info.Auth != ConnectorAuth.Browser) return new RemoteMcpConnector(info.Key, info.DisplayName, secret) { Label = info.Label };
        if (!info.SignedIn || secret.OAuth is not { } tokens) return null;
        var source = _connectorTokens.GetOrAdd(info.Key, key => new McpTokenSource(tokens, _connectorRefresh.Value,
            fresh => _connectors.SaveSecret(key, secret with { OAuth = fresh })));
        return new RemoteMcpConnector(info.Key, info.DisplayName, secret, bearer: async ct =>
        {
            try { return await source.GetAsync(ct).ConfigureAwait(false); }
            catch (McpOAuthException ex) when (ex.Failure == McpOAuthFailure.RefreshRejected)
            {
                // The page shows Reconnect required and later jobs skip this account; this job sees a connector error.
                OnUi(() => MarkReconnect(info.Key, ex.Message));
                throw;
            }
        }) { Label = info.Label };
    }

    private void MarkReconnect(string key, string message)
    {
        _connectorTokens.TryRemove(key, out _);
        if (!_connectors.List().Any(c => c.Key == key)) return;
        _connectors.SetSignedIn(key, false);
        _connectorChecks[key] = new ConnectorCheck(ConnectorStatus.Reconnect, null, message);
        RequestShellPush();
    }

    private void DisposeConnectors()
    {
        if (_connectorRefresh.IsValueCreated) _connectorRefresh.Value.Dispose();
    }
}
