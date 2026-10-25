using System.Diagnostics;
using System.Text.Json;
using OpenClicky.Providers;
using Xunit;

namespace OpenClicky.Core.Tests;

/// <summary>Skips with a clear message when the app has not been built, instead of failing a checkout that only runs tests.</summary>
public sealed class BuiltExeFactAttribute : FactAttribute
{
    public BuiltExeFactAttribute() { if (BrowserNativeHostExeSmokeTests.BuiltExe() is null) Skip = "OpenClicky.App.exe is not built (run dotnet build OpenClicky.sln -c Release); skipping the built-exe relay smoke."; }
}

/// <summary>
/// The built OpenClicky.App.exe, started the way Chrome starts a native host, relays a hello to a bridge server and back.
/// Everything is private to the test: a random pipe name and a key file in a fresh temp folder (Windows ignores a changed
/// LOCALAPPDATA, so OPENCLICKY_BROWSER_KEY moves the key). The user's running app, pipe and real key are never touched.
/// </summary>
public sealed class BrowserNativeHostExeSmokeTests
{
    private const string ExtensionOrigin = "chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/";
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(20);

    internal static string? BuiltExe()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var bin = Path.Combine(dir.FullName, "apps", "windows", "OpenClicky.App", "bin");
            if (!File.Exists(Path.Combine(dir.FullName, "OpenClicky.sln")) || !Directory.Exists(bin)) continue;
            return Directory.EnumerateFiles(bin, "OpenClicky.App.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        return null;
    }

    [BuiltExeFact]
    public async Task BuiltExeAnswersAppFalseThenAppTrueWhenTheAppStarts()
    {
        var exe = BuiltExe()!;
        var root = Path.Combine(Path.GetTempPath(), "openclicky-g10-" + Guid.NewGuid().ToString("N"));
        var key = Path.Combine(root, "OpenClicky", "chrome-bridge.key");
        var pipe = "OpenClicky.Test." + Guid.NewGuid().ToString("N");
        var realKey = ChromeBridgeSecretStore.DefaultPath;
        var realKeyExisted = File.Exists(realKey);
        Directory.CreateDirectory(root);

        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(ExtensionOrigin);
        start.Environment["LOCALAPPDATA"] = root;
        start.Environment["OPENCLICKY_BROWSER_PIPE"] = pipe;
        start.Environment["OPENCLICKY_BROWSER_KEY"] = key;

        using var host = Process.Start(start)!; // only this child is ever killed
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        BrowserBridgeServer? server = null;
        Task? serverRun = null;
        try
        {
            await ChromeBridgeNativeProtocol.WriteAsync(host.StandardInput.BaseStream,
                new { type = "hello", protocol = 2, profileId = "smoke-profile", brand = "Chrome" }, cts.Token);

            var down = await NextReadyAsync(host, cts.Token);
            Assert.False(down.GetProperty("app").GetBoolean());
            Assert.False(down.GetProperty("selected").GetBoolean());

            // The host reads its key only once it reaches a pipe, so make the test key now; authenticating with it proves
            // the host used OPENCLICKY_BROWSER_KEY and not the user's real key.
            string? stored = null;
            server = new BrowserBridgeServer(ChromeBridgeSecretStore.LoadOrCreate(key), pipe, () => stored, id => stored = id);
            serverRun = server.RunAsync(cts.Token);

            // The host retries every 2 s, re-sends its hello after authenticating and gets the app's ready.
            JsonElement up;
            do { up = await NextReadyAsync(host, cts.Token); } while (!up.GetProperty("app").GetBoolean());
            Assert.True(up.GetProperty("selected").GetBoolean());
            Assert.Equal("smoke-profile", stored);
            Assert.Equal("smoke-profile", Assert.Single(server.Instances).Id);
        }
        finally
        {
            try { if (!host.HasExited) host.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await host.WaitForExitAsync().WaitAsync(Step);
            cts.Cancel();
            if (server is not null) await server.DisposeAsync();
            if (serverRun is not null) { try { await serverRun.WaitAsync(Step); } catch (OperationCanceledException) { } }
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
        Assert.Equal(realKeyExisted, File.Exists(realKey));
    }

    private static async Task<JsonElement> NextReadyAsync(Process host, CancellationToken token)
    {
        while (true)
        {
            using var message = await ChromeBridgeNativeProtocol.ReadAsync(host.StandardOutput.BaseStream, token).WaitAsync(Step, token)
                ?? throw new EndOfStreamException("The native host closed stdout.");
            var root = message.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "ready") return root.Clone();
        }
    }
}
