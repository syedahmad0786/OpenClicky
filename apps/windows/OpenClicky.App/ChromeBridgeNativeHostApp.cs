using System.Diagnostics;
using OpenClicky.Providers;

namespace OpenClicky.App;

internal static class ChromeBridgeNativeHostApp
{
    internal const string ExtensionOrigin = "chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/";

    public static async Task<int> RunAsync(string[] args, Stream input, Stream output, CancellationToken token)
    {
        if (args.Length == 0 || !string.Equals(args[0], ExtensionOrigin, StringComparison.Ordinal)) return 2;
        try
        {
            await BrowserNativeHost.RunAsync(input, output, () => ChromeBridgeSecretStore.LoadOrCreate(), BrowserBridgeServer.PipeName,
                LaunchApp, token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return 0; }
        catch { return 1; }
    }

    /// <summary>The popup's "open OpenClicky" while the app is down. Shell-execute: the app must not inherit Chrome's stdio pipes.</summary>
    private static void LaunchApp()
    {
        if (Environment.ProcessPath is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
    }
}
