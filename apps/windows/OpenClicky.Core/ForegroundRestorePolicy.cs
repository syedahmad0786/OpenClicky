namespace OpenClicky.Core;

/// <summary>
/// Chooses which HWND Agent confirm should restore before mutating SendInput.
/// Modal Yes/No UI steals foreground; restoring our own process leaves hotkeys/type
/// on the tray host. Only a living foreign target is valid; never switch to the desktop.
/// </summary>
public static class ForegroundRestorePolicy
{
    /// <param name="prior">HWND captured before the confirm UI appeared.</param>
    /// <param name="priorIsWindow">Whether <paramref name="prior"/> is still a living window.</param>
    /// <param name="priorIsCurrentProcess">True when prior belongs to the OpenClicky process.</param>
    /// <param name="shell">Shell/desktop HWND (<c>GetShellWindow</c>), or zero.</param>
    public static nint ResolveTarget(
        nint prior,
        bool priorIsWindow,
        bool priorIsCurrentProcess,
        nint shell)
    {
        if (prior != 0 && priorIsWindow && !priorIsCurrentProcess)
        {
            return prior;
        }

        return 0;
    }
}
