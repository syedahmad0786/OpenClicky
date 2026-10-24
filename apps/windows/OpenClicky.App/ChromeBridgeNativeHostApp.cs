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
            await ChromeBridgeNativeRelay.RunAsync(input, output, ChromeBridgeSecretStore.LoadOrCreate(), token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return 0; }
        catch { return 1; }
    }
}
