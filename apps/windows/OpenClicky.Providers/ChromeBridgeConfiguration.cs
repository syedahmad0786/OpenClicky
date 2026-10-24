using System.Text.Json;

namespace OpenClicky.Providers;

public sealed record ChromeBridgeConfiguration(string ExtensionOrigin, string[] AllowedOrigins)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "ChromeBridge", "bridge-config.json");

    public static ChromeBridgeConfiguration Load(string? path = null)
    {
        path ??= DefaultPath;
        var config = JsonSerializer.Deserialize<ChromeBridgeConfiguration>(File.ReadAllBytes(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Chrome bridge configuration is empty.");
        if (config.ExtensionOrigin != "chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/")
            throw new InvalidDataException("Chrome bridge configuration has an unexpected extension origin.");
        if (config.AllowedOrigins is not { Length: > 0 } || config.AllowedOrigins.Any(origin => !ExactHttpOrigin(origin)))
            throw new InvalidDataException("Chrome bridge configuration must contain exact HTTP(S) origins.");
        return config with { AllowedOrigins = config.AllowedOrigins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    private static bool ExactHttpOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.PathAndQuery == "/" && uri.Fragment.Length == 0;
}
