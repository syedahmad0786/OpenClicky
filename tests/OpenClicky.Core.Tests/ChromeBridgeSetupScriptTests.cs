using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OpenClicky.Core.Tests;

public sealed class ChromeBridgeSetupScriptTests
{
    [Fact]
    public async Task GenerationOnlyWritesExactBomFreeJsonWithoutRegistryStep()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var script = Path.Combine(root, "apps", "chrome-extension", "setup-local.ps1");
        var temp = Path.Combine(Path.GetTempPath(), "openclicky-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var host = Path.Combine(temp, "Custom OpenClicky.exe");
        await File.WriteAllBytesAsync(host, [0x4d, 0x5a]);
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            start.Environment["LOCALAPPDATA"] = temp;
            foreach (var argument in new[] { "-NoProfile", "-File", script, "-NativeHostPath", host, "-AllowedOrigins", "https://example.com/,http://127.0.0.1:3210/", "-GenerationOnly" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);

            var output = Path.Combine(temp, "OpenClicky", "ChromeBridge");
            foreach (var name in new[] { "manifest.json", "native-host.json", "bridge-config.json" })
                Assert.False(File.ReadAllBytes(Path.Combine(output, name)).AsSpan().StartsWith(Encoding.UTF8.Preamble));
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "manifest.json")));
            Assert.Equal(["https://example.com/*", "http://127.0.0.1:3210/*"], manifest.RootElement.GetProperty("content_scripts")[0].GetProperty("matches").EnumerateArray().Select(value => value.GetString()));
            using var native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "native-host.json")));
            Assert.Equal(host, native.RootElement.GetProperty("path").GetString());
            Assert.Equal("chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/", native.RootElement.GetProperty("allowed_origins")[0].GetString());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
