namespace OpenClicky.Core;

public static class WalkthroughTargetWindow
{
    public static nint Track(nint currentTarget, nint foregroundWindow, bool foregroundIsOpenClicky)
        => foregroundWindow != nint.Zero && !foregroundIsOpenClicky ? foregroundWindow : currentTarget;

    public static bool MustRestore(nint foregroundWindow, bool foregroundIsOpenClicky)
        => foregroundWindow == nint.Zero || foregroundIsOpenClicky;
}
