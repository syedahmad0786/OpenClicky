using OpenClicky.App;
using OpenClicky.Core;
using System.IO;

internal static class ClipboardShelfFormCheck
{
    public static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "OpenClicky-ClipboardShelf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "shelf.json");
                using var form = new ClipboardShelfForm(path, ["private fixture"]);
                form.Show();
                Application.DoEvents();
                form.IgnoreNextAppClipboardWrite();
                if (!form.ConsumeAppClipboardWriteSuppression(DateTimeOffset.UtcNow)
                    || form.ConsumeAppClipboardWriteSuppression(DateTimeOffset.UtcNow))
                    throw new InvalidOperationException("App clipboard write suppression was not bounded to one update.");
                if (!OpenClicky.Agent.ForegroundWindowInfo.IsCurrentProcessWindow(form.Handle)
                    || OpenClicky.Agent.ForegroundWindowInfo.IsCurrentProcessWindow(IntPtr.Zero))
                    throw new InvalidOperationException("App-owned clipboard window detection failed.");
                var workingArea = Screen.FromHandle(form.Handle).WorkingArea;
                if (!workingArea.Contains(form.Bounds))
                    throw new InvalidOperationException($"Shelf bounds {form.Bounds} escaped working area {workingArea}.");

                var manual = Find<TextBox>(form, "Text to save manually");
                manual.Text = "00123\r\nhttps://example.test/a?x=1";
                FindButton(form, "Add text").PerformClick();
                manual.Text = "second";
                FindButton(form, "Add text").PerformClick();

                var list = Find<ListBox>(form, "Saved clipboard items");
                list.SelectedIndex = 0;
                FindButton(form, "Pin").PerformClick();
                FindButton(form, "Move down").PerformClick();
                var editor = Find<TextBox>(form, "Exact selected item content");
                editor.AppendText("\r\nkept exactly");
                FindButton(form, "Save edit").PerformClick();

                var search = Find<TextBox>(form, "Search clipboard shelf");
                search.Text = "kept exactly";
                if (list.Items.Count != 1) throw new InvalidOperationException("Search did not filter the saved shelf.");

                var restored = new ClipboardShelf(path);
                if (restored.Items.Count != 2
                    || restored.Items[1].Text != "00123\r\nhttps://example.test/a?x=1\r\nkept exactly"
                    || !restored.Items[1].IsPinned)
                    throw new InvalidOperationException("Edit, order, pin, URL, number, or line breaks did not persist.");
            }
            catch (Exception ex) { failure = ex; }
            finally { Directory.Delete(directory, recursive: true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
        Console.WriteLine("PASS clipboard shelf persistent edit, pin, reorder and search using an isolated temporary database");
        return 0;
    }

    private static Button FindButton(Control root, string text) => Descendants(root).OfType<Button>().Single(control => control.Text == text);
    private static T Find<T>(Control root, string accessibleName) where T : Control => Descendants(root).OfType<T>().Single(control => control.AccessibleName == accessibleName);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
