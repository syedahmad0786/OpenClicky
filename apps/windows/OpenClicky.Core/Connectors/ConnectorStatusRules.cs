using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core;

/// <summary>The latest real check of a connector (initialize + tools/list) this session.</summary>
public sealed record ConnectorCheck(ConnectorStatus Status, int? ToolCount, string Message);

public static class ConnectorStatusRules
{
    /// <summary>
    /// What the page shows: an active sign-in first, then the latest check, then what is stored. A browser connector that
    /// never finished signing in needs sign-in; anything else unchecked is only "configured", never "connected".
    /// </summary>
    public static ConnectorStatus Display(ConnectorInfo info, ConnectorCheck? check, bool signingIn) =>
        signingIn ? ConnectorStatus.SigningIn
        : check is not null ? check.Status
        : info.Auth == ConnectorAuth.Browser && !info.SignedIn ? ConnectorStatus.NeedsSignIn
        : ConnectorStatus.Configured;
}
