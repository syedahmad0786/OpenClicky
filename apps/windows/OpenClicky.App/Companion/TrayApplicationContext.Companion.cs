using OpenClicky.App.Shell;
using OpenClicky.Core.CompanionCursor;

namespace OpenClicky.App;

/// <summary>The on-screen companion: its look and presence follow the cursor settings, Pause and reduced motion.</summary>
internal sealed partial class TrayApplicationContext
{
    /// <summary>UI thread: at startup, after a cursor.* patch, Pause/Resume, the classic settings form, and when a turn starts
    /// (Windows' reduced-motion setting is read fresh each time).</summary>
    private void ApplyCompanionStyle() => _overlay.ApplyStyle(CompanionStyle.From(_settings, GeneralBehaviour.ReducedMotion()));
}
