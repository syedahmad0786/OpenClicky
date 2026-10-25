using System.Diagnostics;
using System.Windows.Automation;

namespace OpenClicky.Agent;

/// <summary>
/// Reads a browser's address-bar text over UI Automation: the first Edit control named like an address bar, found by walking the
/// window's browser chrome only (a Document subtree is the page and is skipped). The address is untrusted and private: callers
/// compare or reduce it and never log or store it. Call from a thread-pool (MTA) thread; the walk stops at its deadline, but one
/// UIA call into a hung browser can run past it, so async callers should not wait on it unbounded (see <see cref="ReadAsync"/>).
/// </summary>
public static class BrowserAddressReader
{
    private const int MaxDepth = 12;
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "vivaldi", "opera" };

    public static bool IsBrowser(string processName) => Browsers.Contains(processName);

    /// <summary>The foreground window's address text when it belongs to a browser, within <paramref name="budget"/>; else null.</summary>
    public static string? Read(TimeSpan budget)
    {
        try
        {
            var window = ForegroundWindowInfo.GetHandle();
            if (window == IntPtr.Zero || ForegroundWindowInfo.ProcessIdOf(window) is not (> 0 and var processId)) return null;
            using var process = Process.GetProcessById((int)processId);
            return IsBrowser(process.ProcessName) ? Read(window, Stopwatch.StartNew(), budget.TotalMilliseconds) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; } // the window or process went away
    }

    /// <summary><see cref="Read(TimeSpan)"/> off the caller's thread, never held longer than the budget (plus a moment) by a hung browser.</summary>
    public static async Task<string?> ReadAsync(TimeSpan budget, CancellationToken ct)
    {
        var work = Task.Run(() => Read(budget), CancellationToken.None); // thread-pool threads are MTA, as UIA wants
        var finished = await Task.WhenAny(work, Task.Delay(budget + TimeSpan.FromMilliseconds(200), ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return finished == work ? await work.ConfigureAwait(false) : null;
    }

    /// <summary>The address text of <paramref name="window"/>'s chrome, giving up when <paramref name="clock"/> passes <paramref name="deadlineMs"/>.</summary>
    internal static string? Read(IntPtr window, Stopwatch clock, double deadlineMs)
    {
        if (window == IntPtr.Zero) return null;
        var cache = new CacheRequest { TreeFilter = Automation.ControlViewCondition };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        using var _ = cache.Activate();
        var walker = TreeWalker.ControlViewWalker;

        string? Walk(AutomationElement parent, int depth)
        {
            for (var child = walker.GetFirstChild(parent, cache); child is not null; child = walker.GetNextSibling(child, cache))
            {
                if (clock.ElapsedMilliseconds >= deadlineMs) return null;
                var type = child.Cached.ControlType;
                if (type == ControlType.Document) continue; // the page: never read
                if (type == ControlType.Edit)
                {
                    if (child.Cached.Name.Contains("address", StringComparison.OrdinalIgnoreCase))
                        return child.TryGetCurrentPattern(ValuePattern.Pattern, out var p) ? ((ValuePattern)p).Current.Value : null;
                    continue;
                }
                if (depth < MaxDepth && Walk(child, depth + 1) is { } found) return found;
            }
            return null;
        }

        try { return Walk(AutomationElement.FromHandle(window), 0); }
        catch (Exception) { return null; } // any UIA failure (window gone, COM, access) means "no address"
    }
}
