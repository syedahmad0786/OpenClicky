using OpenClicky.App.Shell;
using OpenClicky.Core.CompanionCursor;

namespace OpenClicky.App;

/// <summary>The on-screen companion: its look and presence follow the cursor settings, Pause and reduced motion.</summary>
internal sealed partial class TrayApplicationContext
{
    /// <summary>UI thread: at startup, after a cursor.* patch, Pause/Resume and the classic settings form.</summary>
    private void ApplyCompanionStyle() => _overlay.ApplyStyle(CompanionStyle.From(_settings, GeneralBehaviour.ReducedMotion()));
}
