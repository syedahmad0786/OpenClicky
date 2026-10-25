using System.Net;
using System.Net.Sockets;
using OpenClicky.Core;
using OpenClicky.Core.Work;
using OpenClicky.Providers;

namespace OpenClicky.App;

internal static class OpenWorkCompanionRunner
{
    public static async Task<OpenCodeTaskResult> RunAsync(string task, AppSettings settings, ISecretStore secrets,
        IDesktopToolHost host, AgentAccessMode access, Action<AgentProgress> progress,
        CancellationToken token, string? runtimeDirectory = null, string? workspaceDirectory = null, ChromeBridgeSession? browser = null,
        JobOutputTools? jobFiles = null, IReadOnlyList<RemoteMcpConnector>? connectors = null, Action<OpenCodeToolCall>? toolUsed = null)
    {
        if (settings.AuthProvider != AuthProvider.ChatGPT || !ProviderFactory.IsChatGptSignedIn(secrets))
            throw new InvalidOperationException("Continue with ChatGPT in Settings before running a task.");
        var model = settings.ResolveVisionModel();
        if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("Choose a ChatGPT model in Settings.");
        var runtime = runtimeDirectory ?? Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "runtime", "openwork");
        var node = Path.Combine(runtime, "node.exe");
        var server = Path.Combine(runtime, "node_modules", "openwork-server", "bin", "openwork-server.mjs");
        var engine = Path.Combine(runtime, "engine", "opencode.exe");
        if (!File.Exists(node) || !File.Exists(server) || !File.Exists(engine))
            throw new InvalidOperationException("The OpenWork runtime is missing from this build. Install the complete OpenClicky package.");
        var workspace = workspaceDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky", "openwork-workspace");
        Directory.CreateDirectory(workspace);
        var oauth = new ChatGptOAuthService();
        await using var inference = await ChatGptInferenceBridge.StartAsync(ct => oauth.GetAccessTokenAsync(settings, secrets, ct), cancellationToken: token);
        var session = new DesktopToolSession(host, access, AgentDenyList.ParseExtraFragments(settings.AgentDenyListExtra),
            token, browser, destination => progress(new(0, "observing", destination)), jobFiles, connectors);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await using var tools = new McpDesktopHttpServer(port, new McpDesktopBridge(session));
        tools.Start();
        var profile = new OpenCodeCompanionConfiguration(inference.BaseUri, inference.EphemeralToken, tools.Endpoint, tools.Token, model);
        progress(new(0, "planning", "Starting the private OpenWork task runtime"));
        await using var work = await OpenWorkServerService.StartAsync(workspace, node, server, engine, profile,
            cancellationToken: token, startupProgress: (stage, completed, elapsedMilliseconds) =>
            {
                var label = stage switch
                {
                    "dependencies" => "Preparing bundled task components",
                    "health" => "Starting the private task service",
                    "workspace" => "Opening the private task workspace",
                    "profile" => "Applying the protected companion profile",
                    "effective-profile" => "Verifying the protected companion profile",
                    "engine-reload" => "Reloading the private task engine",
                    _ => "Preparing the private task runtime",
                };
                progress(new(0, "planning", completed ? $"{label} completed in {elapsedMilliseconds} ms" : label));
            });
        // In order, on the polling thread: a plan update must not be overtaken by an older one (Progress<T> posts to the pool).
        var updates = new InlineProgress<OpenCodeTaskUpdate>(update =>
        {
            if (update is { Kind: OpenCodeTaskUpdateKind.ToolUsed, Tool: { } call }) toolUsed?.Invoke(call);
            // The reply streams in as it is written; its trailing result block is never shown as progress.
            else if (update.Kind == OpenCodeTaskUpdateKind.Message && JobProgress.LiveText(update.Text) is { Length: > 0 } text)
                progress(new(0, "planning", text));
        });
        var result = await work.RunTaskAsync(task[..Math.Min(task.Length, 80)], task, updates, token);
        if (session.WasDeclined)
            throw new OperationCanceledException("The task stopped because the user declined an action.", token);
        if (session.HasFailedApprovedAction)
            throw new InvalidOperationException("OpenWork returned a result after an approved action failed or did not execute. The task was not marked successful.");
        if (session.HasUnverifiedAppLaunch)
            throw new InvalidOperationException("OpenWork returned a result after an app launch whose destination was not verified. The task was not marked successful.");
        return result;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
