using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// Hands a browser-signed-in connector its access token, refreshing it once (one caller at a time) when it expires
/// within 60 seconds and saving the new tokens. A token without an expiry is never refreshed. A missing or refused
/// refresh token throws <see cref="McpOAuthFailure.RefreshRejected"/>, which the page shows as "Reconnect required"; a refresh
/// that times out throws <see cref="HttpRequestException"/>.
/// </summary>
public sealed class McpTokenSource(McpOAuthTokens initial, McpOAuth oauth, Action<McpOAuthTokens> save, Func<DateTimeOffset>? now = null)
{
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);
    // Never disposed: without a wait handle it holds nothing (same reasoning as JsonRpcMcpConnector's gate).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private volatile McpOAuthTokens _tokens = initial;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (!Expiring(_tokens)) return _tokens.AccessToken;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _tokens;
            if (!Expiring(current)) return current.AccessToken;
            McpOAuthTokens fresh;
            try { fresh = await oauth.RefreshAsync(current, ct).ConfigureAwait(false); }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // The token endpoint's own HTTP timeout, not the caller's cancellation: a connector error, never "task cancelled".
                throw new HttpRequestException("Sign-in refresh timed out.", ex);
            }
            // Swap first: the server may have rotated the refresh token, so the old one is spent even if saving fails.
            _tokens = fresh;
            save(fresh);
            return fresh.AccessToken;
        }
        finally { _gate.Release(); }
    }

    private bool Expiring(McpOAuthTokens tokens) => tokens.ExpiresAt is { } expires && expires - _now() <= Margin;
}
