using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

/// <summary>
/// Windows keeps desktop (non-packaged) apps' consent under <c>&lt;area&gt;\NonPackaged\Value</c> and store apps' under
/// <c>&lt;area&gt;\Value</c>. OpenClicky is a desktop app, so the NonPackaged value wins when present.
/// </summary>
public static class PrivacyConsentMapping
{
    /// <param name="readValue">Reads the "Value" string under a ConsentStore sub-path (e.g. "microphone\NonPackaged"); null when missing.</param>
    public static ReachState Resolve(string consentKey, Func<string, string?> readValue)
    {
        var value = readValue(consentKey + @"\NonPackaged");
        if (string.IsNullOrEmpty(value)) value = readValue(consentKey);
        return SettingsActionArgs.ConsentState(value);
    }
}
