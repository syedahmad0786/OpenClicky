using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using OpenClicky.Agent;
using OpenClicky.App;

internal static class ClipboardSequenceCheck
{
    private const string First = "first shelf item\r\n";
    private const string Second = "second shelf item";

    public static int Run()
    {
        var exit = 1;
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            var directory = Path.Combine(Path.GetTempPath(), "OpenClicky-ClipboardSequence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sandboxExe = ResolveSandboxExe() ?? throw new InvalidOperationException("Build OpenClicky.Sandbox Release before starting this check.");
            var sandboxStart = new ProcessStartInfo(sandboxExe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(sandboxExe)! };
            sandboxStart.ArgumentList.Add("--multiline");
            using var sandbox = Process.Start(sandboxStart)
                ?? throw new InvalidOperationException("Could not start the clipboard sequence sandbox.");
            using var shelf = new ClipboardShelfForm(Path.Combine(directory, "shelf.json"), []);
            using var form = new Form { Text = "OpenClicky clipboard sequence acceptance", ClientSize = new(680, 260), StartPosition = FormStartPosition.CenterScreen };
            var start = new Button { Text = "Start clipboard sequence check", AccessibleName = "Start clipboard sequence check", Bounds = new(20, 20, 300, 42) };
            var status = new Label { Text = "Click Start. The check inserts two saved items into the disposable sandbox and never sends or submits.", AccessibleName = "Clipboard sequence check status", Bounds = new(20, 82, 640, 145), AutoEllipsis = true };
            AutomationElement? destinationEdit = null;
            form.Controls.AddRange([start, status]);
            form.FormClosed += (_, _) => { shelf.Close(); if (!sandbox.HasExited) sandbox.Kill(entireProcessTree: true); };
            start.Click += async (_, _) =>
            {
                start.Enabled = false;
                try
                {
                    var window = await WaitForSandboxWindowAsync(sandbox.Id, TimeSpan.FromSeconds(10));
                    shelf.Show(form);
                    var manual = Find<TextBox>(shelf, "Text to save manually");
                    manual.Text = First; FindButton(shelf, "Add text").PerformClick();
                    manual.Text = Second; FindButton(shelf, "Add text").PerformClick();
                    FindButton(shelf, "Choose destination").PerformClick();
                    form.WindowState = FormWindowState.Minimized;

                    var handle = new IntPtr(window.Current.NativeWindowHandle);
                    if (!ForegroundFocusRestorer.Restore(handle)) throw new InvalidOperationException("The sandbox could not receive focus.");
                    var edit = destinationEdit = window.FindFirst(TreeScope.Descendants,
                        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                            new PropertyCondition(AutomationElement.NameProperty, "Type target")))
                        ?? throw new InvalidOperationException("The sandbox Type target was not found.");
                    var point = edit.GetClickablePoint();
                    var click = await new CompositeAgentExecutor().ExecuteAsync(new(AgentToolNames.Click,
                        new Dictionary<string, string> { ["x"] = ((int)Math.Round(point.X)).ToString(), ["y"] = ((int)Math.Round(point.Y)).ToString() },
                        "Focus the disposable clipboard sequence field"), userConfirmed: true);
                    if (!click.Success) throw new InvalidOperationException(click.Message);
                    await WaitUntilAsync(() => Find<Label>(shelf, "Clipboard shelf status").Text.StartsWith("Destination chosen:", StringComparison.Ordinal),
                        TimeSpan.FromSeconds(6), () => "The shelf did not capture the selected destination. " + ShelfStatus(shelf));

                    FindButton(shelf, "Paste all").PerformClick();
                    await WaitUntilAsync(() => ReadValue(edit) == First + Second, TimeSpan.FromSeconds(8),
                        () => $"The two shelf items did not land in exact sequence. Expected={Quote(First + Second)}; actual={Quote(ReadValue(edit))}; {ShelfStatus(shelf)}");
                    await WaitUntilAsync(() => Find<Label>(shelf, "Clipboard shelf status").Text.Contains("verified 2 items", StringComparison.OrdinalIgnoreCase),
                        TimeSpan.FromSeconds(2), () => "The shelf did not report two verified insertions. " + ShelfStatus(shelf));
                    if (window.FindFirst(TreeScope.Descendants, new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Submit"), new PropertyCondition(AutomationElement.NameProperty, "Send"))) is not null)
                        throw new InvalidOperationException("The disposable destination unexpectedly exposes a submit control.");

                    exit = 0;
                    form.Show(); form.Activate();
                    status.Text = status.AccessibleName = "PASS: two exact saved items were inserted in order and read back from the disposable field. No Send or Enter action exists in the fixture. Shelf and sandbox remain visible for inspection.";
                    Console.WriteLine(status.Text);
                }
                catch (Exception ex)
                {
                    var detail = $"{ex.Message} FinalReadback={Quote(destinationEdit is null ? null : ReadValue(destinationEdit))}; {ShelfStatus(shelf)}";
                    form.WindowState = FormWindowState.Normal; form.Show(); form.Activate(); status.Text = status.AccessibleName = "FAIL: " + detail;
                    Console.WriteLine($"FAIL {ex.GetType().Name}: {detail}");
                }
                finally { start.Enabled = true; }
            };
            Application.Run(form);
            if (!sandbox.HasExited) sandbox.Kill(entireProcessTree: true);
            try { Directory.Delete(directory, recursive: true); } catch { }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        return exit;
    }

    private static string? ReadValue(AutomationElement element)
    {
        try { return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ? ((ValuePattern)pattern).Current.Value : null; }
        catch (ElementNotAvailableException) { return null; }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, Func<string> error)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) { if (condition()) return; await Task.Delay(50); }
        throw new TimeoutException(error());
    }

    private static string ShelfStatus(Control shelf) => "ShelfStatus=" + Quote(Find<Label>(shelf, "Clipboard shelf status").Text);
    private static string Quote(string? value) => value is null ? "<unavailable>" : '"' + value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"") + '"';

    private static async Task<AutomationElement> WaitForSandboxWindowAsync(int processId, TimeSpan timeout)
    {
        AutomationElement? found = null;
        await WaitUntilAsync(() => (found = AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "OpenClicky Agent Sandbox"),
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId)))) is not null, timeout, () => "The sandbox did not open.");
        return found!;
    }

    private static Button FindButton(Control root, string text) => Descendants(root).OfType<Button>().Single(control => control.Text == text);
    private static T Find<T>(Control root, string accessibleName) where T : Control => Descendants(root).OfType<T>().Single(control => control.AccessibleName == accessibleName);
    private static IEnumerable<Control> Descendants(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static string? ResolveSandboxExe()
    {
        var isolated = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "OpenClicky.Sandbox", "release", "OpenClicky.Sandbox.exe"));
        if (File.Exists(isolated)) return isolated;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
            foreach (var path in new[] { Path.Combine(directory.FullName, "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe"), Path.Combine(directory.FullName, "apps", "windows", "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe") })
                if (File.Exists(path)) return path;
        return null;
    }
}
