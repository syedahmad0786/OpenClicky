using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Agent;

/// <summary>
/// Reads the text of the controls under a highlight stroke with UI Automation. The text is untrusted screen data for the model.
/// Never reads OpenClicky's own windows, a window whose title is on the deny list, or a password field.
/// </summary>
public static class UiaTextHarvester
{
    public const int MaxChars = 500;
    private const int MaxSamples = 25; // 24 along the stroke, plus a circle's centre
    private const uint GaRoot = 2, GaRootOwner = 3;

    /// <summary>
    /// Collects the distinct names and values of the elements at <paramref name="samples"/> (virtual-screen px), each with the
    /// point it was read at, at most <see cref="MaxChars"/> characters in all. Returns what was found when
    /// <paramref name="budget"/> runs out; a hung target app can hold one UIA call past it, but never the caller.
    /// </summary>
    /// <param name="extraDenyFragments">Extra title fragments the user excluded (Settings → Access), on top of the built-in deny list.</param>
    public static IReadOnlyList<HarvestedText> Harvest(IReadOnlyList<Pt> samples, int ownProcessId, TimeSpan budget,
        IEnumerable<string>? extraDenyFragments = null)
    {
        if (samples.Count == 0 || budget <= TimeSpan.Zero) return [];
        var extras = extraDenyFragments?.ToList();
        var found = new List<HarvestedText>();
        var length = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var work = Task.Run(() =>
        {
            foreach (var p in samples.Take(MaxSamples))
            {
                if (clock.Elapsed >= budget) return;
                foreach (var text in At((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), ownProcessId, extras))
                {
                    lock (found)
                    {
                        if (clock.Elapsed >= budget) return;
                        if (length + text.Length > MaxChars || found.Any(f => f.Text == text)) continue;
                        found.Add(new(p, text));
                        length += text.Length;
                    }
                }
            }
        });
        try { work.Wait(budget); }
        catch (AggregateException) { } // a UIA failure keeps whatever was read before it
        lock (found) return found.ToArray();
    }

    /// <summary>
    /// A point refuses when the top-level window under it or that window's root owner belongs to OpenClicky or to no readable
    /// process, or carries a deny-listed title: an owned popup without a title of its own (a protected app's menu or dialog) is
    /// judged by its owner as well.
    /// </summary>
    private static bool Refused(nint window, int ownProcessId, IReadOnlyList<string>? extras)
    {
        var root = GetAncestor(window, GaRoot);
        if (root == 0) root = window;
        foreach (var hwnd in new[] { root, GetAncestor(window, GaRootOwner) }.Where(h => h != 0).Distinct())
        {
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == ownProcessId || AgentDenyList.IsDenied(Title(hwnd), extras)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when UIA must not be asked about (<paramref name="x"/>, <paramref name="y"/>): no window is there, or it is refused.
    /// The window under the point decides first, without asking UIA (or our own UI thread) anything. Shared with <see cref="UiaControlSnapper"/>.
    /// </summary>
    internal static bool RefusedAt(int x, int y, int ownProcessId, IReadOnlyList<string>? extras)
    {
        var window = WindowFromPoint(new NativePoint { X = x, Y = y });
        return window == 0 || Refused(window, ownProcessId, extras);
    }

    private static IEnumerable<string> At(int x, int y, int ownProcessId, IReadOnlyList<string>? extras)
    {
        if (RefusedAt(x, y, ownProcessId, extras)) yield break;

        AutomationElement? element;
        try { element = AutomationElement.FromPoint(new System.Windows.Point(x, y)); }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException) { yield break; }
        if (element is null) yield break;
        string? name = null, value = null;
        try
        {
            var current = element.Current;
            if (current.ProcessId == ownProcessId || current.IsPassword) yield break;
            name = current.Name;
            // A document's value is the whole document, not what is under the stroke.
            if (current.ControlType != ControlType.Document
                && element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern v) value = v.Current.Value;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException) { }
        if (Clean(name) is { } n) yield return n;
        if (Clean(value) is { } val) yield return val;
    }

    /// <summary>Whitespace flattened; null when empty or longer than <see cref="MaxChars"/> (then it is more than what is under the stroke).</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > MaxChars ? null : flat;
    }

    private static string? Title(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return null;
        var sb = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);
}
