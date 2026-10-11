using System.Text;
using System.Windows.Automation;

namespace OpenClicky.Agent;

/// <summary>Trimmed UI Automation tree for the foreground window (Phase D).</summary>
public sealed class UiaSnapshotService
{
    public string CaptureForeground(int maxNodes = 80)
    {
        var root = AutomationElement.RootElement;
        var focused = AutomationElement.FocusedElement ?? root;
        var window = FindAncestorWindow(focused) ?? root;

        var name = Safe(() => window.Current.Name) ?? "(unnamed)";
        var sb = new StringBuilder();
        sb.AppendLine($"foreground_window=\"{Escape(name)}\"");

        var condition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

        AutomationElementCollection? nodes = null;
        try
        {
            nodes = window.FindAll(TreeScope.Descendants, condition);
        }
        catch (Exception ex)
        {
            return sb.AppendLine($"uia_error={ex.Message}").ToString();
        }

        var count = 0;
        foreach (AutomationElement el in nodes)
        {
            if (count >= maxNodes)
            {
                sb.AppendLine($"…truncated after {maxNodes} nodes");
                break;
            }

            try
            {
                var current = el.Current;
                if (string.IsNullOrWhiteSpace(current.Name) && current.ControlType == ControlType.Text)
                {
                    continue;
                }

                var rect = current.BoundingRectangle;
                if (rect.Width <= 1 || rect.Height <= 1 || double.IsInfinity(rect.X))
                {
                    continue;
                }

                var centerX = (int)Math.Round(rect.X + rect.Width / 2);
                var centerY = (int)Math.Round(rect.Y + rect.Height / 2);
                sb.AppendLine(
                    $"{current.ControlType.ProgrammaticName} name=\"{Escape(current.Name)}\" " +
                    $"center=({centerX},{centerY}) bounds=[{(int)rect.X},{(int)rect.Y},{(int)rect.Width},{(int)rect.Height}]");
                count++;
            }
            catch
            {
                // skip stale automation peers
            }
        }

        sb.AppendLine($"nodes={count}");
        return sb.ToString();
    }

    private static AutomationElement? FindAncestorWindow(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = element;
        for (var i = 0; i < 24 && current is not null; i++)
        {
            try
            {
                if (current.Current.ControlType == ControlType.Window)
                {
                    return current;
                }

                current = walker.GetParent(current);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace('\\', '/').Replace('"', '\'');

    private static T? Safe<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default;
        }
    }
}
