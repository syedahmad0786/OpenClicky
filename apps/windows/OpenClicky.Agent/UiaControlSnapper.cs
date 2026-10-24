using System.Runtime.InteropServices;
using System.Windows.Automation;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Agent;

/// <summary>
/// Snaps a POINT to the bounds of the real control under it with UI Automation, so the ring and the walkthrough's click target
/// fit that control. Reads only bounds, control types and process ids: never names or values. Refuses OpenClicky's own windows,
/// unreadable or deny-listed windows (the same check as <see cref="UiaTextHarvester"/>) and password fields.
/// </summary>
public static class UiaControlSnapper
{
    public static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);
    private const int MaxDepth = 16;

    private static readonly HashSet<ControlType> Interactive =
    [
        ControlType.Button, ControlType.MenuItem, ControlType.Hyperlink, ControlType.Edit, ControlType.ComboBox, ControlType.CheckBox,
        ControlType.RadioButton, ControlType.TabItem, ControlType.ListItem, ControlType.TreeItem, ControlType.Slider, ControlType.SplitButton,
    ];

    /// <summary>
    /// The frame of the first interactive control (the element at the point, then its ancestors) per <see cref="TargetSnap.Choose"/>,
    /// or null. Call it off the UI thread: it waits at most <paramref name="budget"/>; a hung target app can hold one UIA call
    /// past it, but never the caller.
    /// </summary>
    /// <param name="extraDenyFragments">Extra title fragments the user excluded (Settings → Access), on top of the built-in deny list.</param>
    public static Box? Snap(int x, int y, TimeSpan budget, IEnumerable<string>? extraDenyFragments = null)
    {
        if (budget <= TimeSpan.Zero) return null;
        var own = Environment.ProcessId;
        var extras = extraDenyFragments?.ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var work = Task.Run(() => Chain(x, y, own, extras, clock, budget));
        try
        {
            if (!work.Wait(budget) || work.Result is not { } chain) return null;
            var monitor = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).Bounds;
            return TargetSnap.Choose(chain, new Pt(x, y), new Box(monitor.X, monitor.Y, monitor.Width, monitor.Height));
        }
        catch (AggregateException) { return null; }
    }

    private static List<SnapCandidate>? Chain(int x, int y, int own, IReadOnlyList<string>? extras, System.Diagnostics.Stopwatch clock, TimeSpan budget)
    {
        if (UiaTextHarvester.RefusedAt(x, y, own, extras)) return null;
        var chain = new List<SnapCandidate>();
        // One cross-process round trip per level: the four properties come with the element instead of one call each.
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.ProcessIdProperty);
        cache.Add(AutomationElement.IsPasswordProperty);
        try
        {
            using var _ = cache.Activate(); // this thread only: FromPoint fills the cache too
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element is null || element.Cached.IsPassword) return null;
            var root = AutomationElement.RootElement;
            for (var depth = 0; element is not null && depth < MaxDepth && clock.Elapsed < budget; depth++)
            {
                if (Automation.Compare(element, root)) break;
                var cached = element.Cached;
                var r = cached.BoundingRectangle;
                chain.Add(new(new Box((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
                    Interactive.Contains(cached.ControlType), cached.ProcessId == own));
                element = TreeWalker.ControlViewWalker.GetParent(element, cache);
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException) { }
        return chain;
    }
}
