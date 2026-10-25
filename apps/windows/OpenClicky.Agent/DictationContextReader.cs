using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using OpenClicky.Core.Dictation;

namespace OpenClicky.Agent;

/// <summary>
/// Reads the text around the caret of a captured dictation field over UI Automation, on a background thread within a time budget.
/// The text is untrusted data for the tidy-up prompt. Never reads a password element, and never reads a web page: the site host
/// comes only from a browser's address bar, found by a walk that does not enter the page document.
/// </summary>
public static class DictationContextReader
{
    private const int AddressBarBudgetMs = 150, AddressBarMaxDepth = 12;
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "vivaldi", "opera" };

    /// <summary>The field's context, or null when the budget runs out, the field is protected or unreadable, or UIA fails.</summary>
    public static async Task<DictationFieldContext?> ReadAsync(DictationTarget target, TimeSpan budget, CancellationToken ct)
    {
        if (budget <= TimeSpan.Zero) return null;
        var clock = Stopwatch.StartNew();
        var work = Task.Run(() => Read(target, clock, budget), CancellationToken.None); // thread-pool threads are MTA, as UIA wants
        var finished = await Task.WhenAny(work, Task.Delay(budget, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // A hung target app can hold one UIA call past the budget; it never holds the caller.
        return finished == work ? await work.ConfigureAwait(false) : null;
    }

    private static DictationFieldContext? Read(DictationTarget target, Stopwatch clock, TimeSpan budget)
    {
        try
        {
            var element = target.Element;
            if (element.Current.IsPassword) return null;
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern)) return null;
            var value = ((ValuePattern)valuePattern).Current.Value ?? "";

            var start = value.Length; // a caret that cannot be read is at the end
            var length = 0;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern)
                && ((TextPattern)textPattern).GetSelection() is { Length: > 0 } selection)
            {
                var range = selection[0];
                var head = ((TextPattern)textPattern).DocumentRange.Clone();
                head.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
                start = head.GetText(-1).Length;
                length = range.GetText(-1).Length;
            }

            var processId = element.Current.ProcessId;
            var (appName, processName) = Describe(processId);
            var singleLine = element.Current.ControlType == ControlType.Edit && !value.Contains('\n') && !value.Contains('\r');
            var host = Browsers.Contains(processName) ? SiteHost(target.Window, clock, budget) : null;
            return DictationFieldContext.FromValue(appName, host, value, start, length, singleLine);
        }
        catch (Exception) { return null; } // any UIA failure (element gone, COM, access) means "no context"
    }

    private static (string AppName, string ProcessName) Describe(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            string? description = null;
            try { description = process.MainModule?.FileVersionInfo.FileDescription; }
            catch (Exception) { } // elevated or protected process: the name below will do
            return (string.IsNullOrWhiteSpace(description) ? process.ProcessName : description.Trim(), process.ProcessName);
        }
        catch (Exception) { return ("Unknown app", ""); }
    }

    /// <summary>
    /// The host in the first Edit control named like an address bar, walking the window's browser chrome only (a Document subtree
    /// is the page and is skipped), within <see cref="AddressBarBudgetMs"/> and what is left of <paramref name="budget"/>.
    /// </summary>
    private static string? SiteHost(IntPtr window, Stopwatch clock, TimeSpan budget)
    {
        if (window == IntPtr.Zero) return null;
        var deadline = Math.Min(clock.ElapsedMilliseconds + AddressBarBudgetMs, budget.TotalMilliseconds);
        var cache = new CacheRequest { TreeFilter = Automation.ControlViewCondition };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        using var _ = cache.Activate();
        var walker = TreeWalker.ControlViewWalker;

        string? Walk(AutomationElement parent, int depth)
        {
            for (var child = walker.GetFirstChild(parent, cache); child is not null; child = walker.GetNextSibling(child, cache))
            {
                if (clock.ElapsedMilliseconds >= deadline) return null;
                var type = child.Cached.ControlType;
                if (type == ControlType.Document) continue; // the page: never read
                if (type == ControlType.Edit)
                {
                    if (child.Cached.Name.Contains("address", StringComparison.OrdinalIgnoreCase))
                        return child.TryGetCurrentPattern(ValuePattern.Pattern, out var p)
                            ? DictationFieldContext.HostOf(((ValuePattern)p).Current.Value) : null;
                    continue;
                }
                if (depth < AddressBarMaxDepth && Walk(child, depth + 1) is { } found) return found;
            }
            return null;
        }

        try { return Walk(AutomationElement.FromHandle(window), 0); }
        catch (Exception) { return null; }
    }
}
