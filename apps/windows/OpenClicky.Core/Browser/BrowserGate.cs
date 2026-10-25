using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Browser;

/// <summary>When agents get the browser, and what the browser card shows.</summary>
public static class BrowserGate
{
    /// <summary>A task gets the browser tool only while a profile is connected and the user's Connect still stands.</summary>
    public static bool OffersSession(bool connected, bool registered) => connected && registered;

    public static ChromeState CardState(bool anyDetected, bool registered, int instances, bool working) =>
        !anyDetected ? ChromeState.NoBrowser
        : !registered ? ChromeState.NotInstalled
        : instances == 0 ? ChromeState.Waiting
        : working ? ChromeState.Working
        : ChromeState.Connected;
}
