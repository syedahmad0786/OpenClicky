using System.IO;
using System.Text.Json;
using OpenClicky.App;
using OpenClicky.Core;
using OpenClicky.Providers;

internal static class VisualWorkspaceCheck
{
    public static int Run(string runtimeDirectory, string nodeExecutable)
    {
        var exit = 1;
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            var temporary = Path.Combine(Path.GetTempPath(), "OpenClicky-workspace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            var store = Path.Combine(temporary, "fixture.json");
            using var form = new Form { Text = "OpenClicky workspace acceptance", ClientSize = new(1160, 820), MinimumSize = new(800, 600) };
            var status = new Label { Text = "Starting the actual embedded workspace. Fixture replies only; no microphone, AI account or desktop actions.", Dock = DockStyle.Bottom, Height = 45, AccessibleName = "Workspace acceptance status" };
            var reopen = new Button { Text = "Reload saved artifacts", Dock = DockStyle.Bottom, Height = 35 };
            form.Controls.AddRange([reopen, status]);
            using var lifetime = new CancellationTokenSource();
            CompanionWorkspaceRuntime? runtime = null;
            VisualWorkspacePanel? panel = null;
            var agent = new CompanionAgUiServer(async (prompt, response, token) =>
            {
                response("The shared stream", false);
                await Task.Delay(100, token);
                response("The shared stream is working. You asked: " + prompt, true);
                form.BeginInvoke(() => { status.Text = status.AccessibleName = "PASS: CopilotKit → AG-UI → native controller → streamed reply. Save and reopen an edited visual next."; exit = 0; });
            });
            agent.Start();
            void LoadSaved() => panel?.PostSessionSnapshot("fixture", VisualArtifactStore.Load("fixture", store).Select(item => (object)JsonSerializer.Deserialize<JsonElement>(item.SourceJson)).ToArray());
            reopen.Click += (_, _) => { LoadSaved(); status.Text = "Reloaded " + VisualArtifactStore.Load("fixture", store).Count + " saved artifacts from the isolated SQLite fixture."; };
            form.Shown += async (_, _) =>
            {
                try
                {
                    runtime = await CompanionWorkspaceRuntime.StartAsync(runtimeDirectory, nodeExecutable, agent.Endpoint, agent.Token, lifetime.Token);
                    panel = new VisualWorkspacePanel(runtime.Origin, runtime.SessionToken) { Dock = DockStyle.Fill };
                    form.Controls.Add(panel); panel.BringToFront();
                    panel.WorkspaceReady += (_, _) => LoadSaved();
                    panel.WorkspaceMessage += (_, message) =>
                    {
                        if (message.Kind == "ready") { LoadSaved(); return; }
                        if (message.Kind is "artifact.changed" or "artifact.save")
                        {
                            try
                            {
                                using var data = JsonDocument.Parse(message.Json);
                                var saved = VisualArtifactStore.Save("fixture", "fixture-turn", data.RootElement.GetProperty("artifact"), store);
                                status.Text = status.AccessibleName = "Saved " + saved.Title + " · actual local SQLite fixture";
                            }
                            catch (Exception ex) { status.Text = "FAIL: " + ex.Message; exit = 1; }
                        }
                    };
                    await panel.InitializeAsync(Path.Combine(temporary, "webview"));
                }
                catch (Exception ex) { status.Text = status.AccessibleName = "FAIL: " + ex.GetType().Name + ": " + ex.Message; Console.WriteLine(status.Text); }
            };
            form.FormClosed += (_, _) => lifetime.Cancel();
            Application.Run(form);
            Task.Run(async () => { if (runtime is not null) await runtime.DisposeAsync(); await agent.DisposeAsync(); }).GetAwaiter().GetResult();
            Console.WriteLine("Visual workspace fixture retained for review: " + temporary);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        return exit;
    }
}
