using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Providers;

/// <summary>Turns a real initialize + tools/list into the status and sentence the Integrations page shows.</summary>
public static class ConnectorVerifier
{
    public static async Task<ConnectorCheck> CheckAsync(McpConnector connector, ConnectorAuth auth, TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            // WaitAsync also bounds a connector that ignores its token.
            var tools = await connector.ListToolsAsync(limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
            return new(ConnectorStatus.Connected, tools.Count, tools.Count == 1 ? "1 tool available." : $"{tools.Count} tools available.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            return new(ConnectorStatus.Error, null, $"It did not answer within {Math.Ceiling(timeout.TotalSeconds)} seconds.");
        }
        catch (McpHttpException ex) when (ex.StatusCode is 401 or 403)
        {
            return auth switch
            {
                ConnectorAuth.ApiKey => new(ConnectorStatus.TokenRejected, null, "The server rejected the key."),
                ConnectorAuth.Browser => new(ConnectorStatus.Reconnect, null, "Sign in again to keep using it."),
                _ => new(ConnectorStatus.NeedsSignIn, null, "This server needs you to sign in or add a key."),
            };
        }
        catch (McpOAuthException ex)
        {
            return new(ex.Failure == McpOAuthFailure.RefreshRejected ? ConnectorStatus.Reconnect : ConnectorStatus.NeedsSignIn, null, ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
        {
            // Our transports' messages name the connector and the method, never its URL, headers or tokens.
            var message = SettingsActionArgs.UserMessage(ex);
            return new(ConnectorStatus.Error, null, message[..Math.Min(message.Length, 300)]);
        }
    }
}
