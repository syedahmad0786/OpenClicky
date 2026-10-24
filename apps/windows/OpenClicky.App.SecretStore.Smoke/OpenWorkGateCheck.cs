using System.Text.Json;
using System.IO;
using OpenClicky.Agent;
using OpenClicky.App;
using OpenClicky.Capture;
using OpenClicky.Core;
using OpenClicky.Providers;

internal static class OpenWorkGateCheck
{
    public static int Run(string runtimeDirectory, string evidencePath)
    {
        var exit = 1;
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            using var form = new Form { Text = "OpenClicky OpenWork live gate", ClientSize = new(590, 310), StartPosition = FormStartPosition.CenterScreen };
            var green = new Button { Text = "Green button", AccessibleName = "Green button", Bounds = new(20, 70, 160, 45), BackColor = Color.SeaGreen, ForeColor = Color.White };
            var field = new TextBox { AccessibleName = "Type target", Bounds = new(20, 150, 545, 28) };
            var status = new Label { Text = "Green button clicked 0 times", AccessibleName = "Green button clicked 0 times", Bounds = new(20, 200, 545, 50) };
            var start = new Button { Text = "Start live OpenWork check", Bounds = new(20, 15, 240, 38) };
            var stop = new Button { Text = "Stop", Bounds = new(455, 15, 110, 38) };
            form.Controls.AddRange([green, field, status, start, stop]);
            var clicks = 0;
            green.Click += (_, _) => { clicks++; status.Text = status.AccessibleName = $"Green button clicked {clicks} times"; };
            using var job = new CancellationTokenSource();
            stop.Click += (_, _) => job.Cancel();
            form.FormClosing += (_, _) => job.Cancel();
            start.Click += async (_, _) =>
            {
                start.Enabled = false;
                job.CancelAfter(TimeSpan.FromMinutes(5));
                var settings = SettingsStore.Load();
                using var capture = new WgcScreenCaptureService();
                var executor = new CompositeAgentExecutor();
                var confirm = new WinFormsAgentConfirm(SynchronizationContext.Current!,
                    () => (form.Handle, form.ActiveControl?.Handle ?? nint.Zero));
                var targetHandle = form.Handle;
                var events = new List<string>();
                void Report(AgentProgress update)
                {
                    lock (events) events.Add(update.Status + ":" + update.Tool);
                    Console.WriteLine(update.Status + (update.Tool is null ? "" : ":" + update.Tool));
                }
                var host = new WindowsDesktopToolHost(capture, confirm, async (request, approved, ct) =>
                {
                    var result = await executor.ExecuteAsync(new(request.ToolName, request.Arguments, request.Rationale), approved, ct);
                    return new(result.Success, result.Message, result.RequiresConfirmation);
                }, ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (ForegroundWindowInfo.GetHandle() != targetHandle)
                        throw new InvalidOperationException("Live gate is scoped to its disposable fixture. Bring that window forward.");
                    return Task.CompletedTask;
                }, () => "", Report, () => { }, []);
                try
                {
                    var result = await OpenWorkCompanionRunner.RunAsync(
                        "In this disposable OpenClicky OpenWork live gate window only, click the Green button once, click Type target and type exactly openwork verified. Do not click Start, Stop or close the window. Read the resulting control values and report what you verified.",
                        settings, new WindowsCredentialSecretStore(), host, AgentAccessMode.ConfirmEach, Report, job.Token,
                        runtimeDirectory, Path.Combine(Path.GetTempPath(), "OpenClicky", "gate-workspace-" + Guid.NewGuid().ToString("N")));
                    var verified = clicks == 1 && field.Text == "openwork verified";
                    File.WriteAllText(evidencePath, JsonSerializer.Serialize(new { verified, clicks, field = field.Text, session = result.SessionId, model = settings.ResolveVisionModel(), events,
                        answer = result.Messages.LastOrDefault(m => m.Role == "assistant")?.Text }, new JsonSerializerOptions { WriteIndented = true }));
                    status.Text = status.AccessibleName = verified ? "PASS: OpenWork + ChatGPT + Windows destination verified" : "FAIL: model finished but destination did not match";
                    exit = verified ? 0 : 1;
                    Console.WriteLine(status.Text);
                }
                catch (Exception ex)
                {
                    status.Text = status.AccessibleName = "FAIL: " + ex.Message;
                    Console.WriteLine("FAIL " + ex.GetType().Name + ": " + ex.Message);
                }
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join(); return exit;
    }
}
