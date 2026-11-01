namespace OpenClicky.Core;

/// <summary>Why a browser sign-in or token refresh failed. Shared by the sign-in code and the connector verifier.</summary>
public enum McpOAuthFailure
{
    NoMetadata, RegistrationUnsupported, ApprovedClientsOnly, PortInUse, Denied,
    StateMismatch, TokenExchangeFailed, RefreshRejected, TimedOut, InsecureEndpoint,
}

/// <summary>Carries a fixed, user-readable sentence per failure. It never includes server text, URLs or tokens.</summary>
public sealed class McpOAuthException(McpOAuthFailure failure) : Exception(Copy(failure))
{
    public McpOAuthFailure Failure { get; } = failure;

    private static string Copy(McpOAuthFailure failure) => failure switch
    {
        McpOAuthFailure.NoMetadata => "This server doesn't say how to sign in. Try an API key instead.",
        McpOAuthFailure.RegistrationUnsupported => "This server needs an app registered with it first. Add its OAuth client ID under Advanced.",
        McpOAuthFailure.ApprovedClientsOnly => "This server only accepts apps it has approved. Ask its provider for an OAuth client ID and add it under Advanced.",
        McpOAuthFailure.PortInUse => "Another app is using port 47613, which sign-in needs. Close it and try again.",
        McpOAuthFailure.Denied => "Sign-in was cancelled or refused in the browser.",
        McpOAuthFailure.StateMismatch => "The browser returned an unexpected sign-in response. Try again.",
        McpOAuthFailure.TokenExchangeFailed => "The server didn't accept the sign-in. Try again.",
        McpOAuthFailure.RefreshRejected => "Your sign-in has expired. Reconnect to keep using it.",
        McpOAuthFailure.TimedOut => "Sign-in timed out. Finish signing in within 5 minutes.",
        McpOAuthFailure.InsecureEndpoint => "The server's sign-in address isn't secure (https), so OpenClicky won't use it.",
        _ => "Sign-in failed. Try again.",
    };
}
