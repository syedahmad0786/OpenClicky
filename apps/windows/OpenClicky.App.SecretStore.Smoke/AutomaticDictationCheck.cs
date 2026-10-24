using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using OpenClicky.Agent;

internal static class AutomaticDictationCheck
{
    private const string Draft = "Please keep this as a draft";
    private const string Rejected = "should not appear";

    public static int Run()
    {
        var exit = 1;
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            var sandboxExe = ResolveSandboxExe() ?? throw new InvalidOperationException(
                "Build OpenClicky.Sandbox Release before starting the automatic dictation check.");
            using var sandbox = Process.Start(new ProcessStartInfo(sandboxExe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(sandboxExe)!,
            }) ?? throw new InvalidOperationException("Could not start the dictation sandbox.");
            using var form = new Form
            {
                Text = "OpenClicky automatic dictation acceptance",
                ClientSize = new(650, 260),
                StartPosition = FormStartPosition.CenterScreen,
            };
            var start = new Button
            {
                Text = "Start automatic dictation check",
                AccessibleName = "Start automatic dictation check",
                Bounds = new(20, 20, 280, 42),
            };
            var status = new Label
            {
                Text = "Click Start. This types only into the disposable sandbox; no microphone or model is used.",
                AccessibleName = "Automatic dictation check status",
                Bounds = new(20, 82, 610, 145),
                AutoEllipsis = true,
            };
            form.Controls.AddRange([start, status]);
            form.FormClosed += (_, _) =>
            {
                if (!sandbox.HasExited) sandbox.Kill(entireProcessTree: true);
            };
            start.Click += async (_, _) =>
            {
                start.Enabled = false;
                try
                {
                    var window = await WaitForSandboxWindowAsync(sandbox.Id, TimeSpan.FromSeconds(10));
                    form.WindowState = FormWindowState.Minimized;
                    var sandboxHandle = new IntPtr(window?.Current.NativeWindowHandle ?? 0);
                    if (sandboxHandle == IntPtr.Zero || !ForegroundFocusRestorer.Restore(sandboxHandle))
                        throw new InvalidOperationException("The sandbox window could not be restored.");
                    await WaitUntilAsync(() => ForegroundFocusRestorer.Capture() == sandboxHandle,
                        TimeSpan.FromSeconds(5), "The sandbox window did not become active.");
                    window = await WaitForSandboxWindowAsync(sandbox.Id, TimeSpan.FromSeconds(5));
                    var edit = window?.FindFirst(TreeScope.Descendants,
                        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                            new PropertyCondition(AutomationElement.NameProperty, "Type target")))
                        ?? throw new InvalidOperationException("The sandbox Type target was not found.");
                    if (window!.FindFirst(TreeScope.Descendants,
                        new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Submit"),
                            new PropertyCondition(AutomationElement.NameProperty, "Send"))) is not null)
                        throw new InvalidOperationException("The disposable fixture unexpectedly exposes a submit control.");
                    var point = edit.GetClickablePoint();
                    var executor = new CompositeAgentExecutor();
                    var click = await executor.ExecuteAsync(new(AgentToolNames.Click, new Dictionary<string, string>
                    {
                        ["x"] = ((int)Math.Round(point.X)).ToString(),
                        ["y"] = ((int)Math.Round(point.Y)).ToString(),
                    }, "User started the disposable automatic dictation check"), userConfirmed: true);
                    if (!click.Success) throw new InvalidOperationException("Could not focus the sandbox field: " + click.Message);
                    AutomationElement? focusedEdit = null;
                    await WaitUntilAsync(() =>
                    {
                        try
                        {
                            var focused = AutomationElement.FocusedElement;
                            if (focused?.Current.ProcessId != sandbox.Id
                                || focused.Current.ControlType != ControlType.Edit
                                || focused.Current.Name != "Type target") return false;
                            focusedEdit = focused;
                            return true;
                        }
                        catch (ElementNotAvailableException) { return false; }
                    }, TimeSpan.FromSeconds(5), "The sandbox field did not receive focus.");
                    edit = focusedEdit ?? throw new InvalidOperationException("The focused sandbox field disappeared.");

                    if (!DictationTarget.TryCapture(out var target, out var captureError) || target is null)
                        throw new InvalidOperationException(captureError);
                    var first = await target.InsertAsync(Draft, executor, requireCurrentFocus: true);
                    if (!first.Verified) throw new InvalidOperationException("Stage 1 was not verified: " + first.Message);
                    await WaitUntilAsync(() => ReadValue(edit) == Draft, TimeSpan.FromSeconds(5),
                        "Stage 1 text did not appear exactly in the sandbox field.");
                    var afterFirst = ReadValue(edit);

                    form.WindowState = FormWindowState.Normal;
                    form.Show();
                    form.Activate();
                    start.Focus();
                    await WaitUntilAsync(() => ForegroundFocusRestorer.Capture() == form.Handle,
                        TimeSpan.FromSeconds(5), "The fixture did not receive focus for stage 2.");
                    var second = await target.InsertAsync(Rejected, executor, requireCurrentFocus: true);
                    if (second.InputAttempted) throw new InvalidOperationException("Stage 2 attempted input after focus changed.");
                    if (ReadValue(edit) != afterFirst || ReadValue(edit)?.Contains(Rejected, StringComparison.Ordinal) == true)
                        throw new InvalidOperationException("Stage 2 changed the sandbox destination.");

                    exit = 0;
                    status.Text = status.AccessibleName =
                        "PASS: the exact draft landed and was read back without a submit action. After focus moved to this fixture, the same target rejected input and the destination stayed unchanged. The sandbox remains open for inspection.";
                    Console.WriteLine(status.Text);
                }
                catch (Exception ex)
                {
                    form.WindowState = FormWindowState.Normal;
                    form.Show();
                    form.Activate();
                    status.Text = status.AccessibleName = "FAIL: " + ex.Message;
                    Console.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message}");
                }
                finally { start.Enabled = true; }
            };
            Application.Run(form);
            if (!sandbox.HasExited) sandbox.Kill(entireProcessTree: true);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exit;
    }

    private static string? ReadValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                ? ((ValuePattern)pattern).Current.Value : null;
        }
        catch (ElementNotAvailableException) { return null; }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string error)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException(error);
    }

    private static async Task<AutomationElement?> WaitForSandboxWindowAsync(int processId, TimeSpan timeout)
    {
        AutomationElement? found = null;
        await WaitUntilAsync(() =>
        {
            found = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "OpenClicky Agent Sandbox"),
                    new PropertyCondition(AutomationElement.ProcessIdProperty, processId)));
            return found is not null;
        }, timeout, "The dictation sandbox did not open.");
        return found;
    }

    private static string? ResolveSandboxExe()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
            foreach (var path in new[]
            {
                Path.Combine(directory.FullName, "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe"),
                Path.Combine(directory.FullName, "apps", "windows", "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe"),
            })
                if (File.Exists(path)) return path;
        return null;
    }
}
