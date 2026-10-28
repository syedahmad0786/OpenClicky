using Microsoft.Win32;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.App.Settings;

/// <summary>Reads the Windows privacy consent ("Allow"/"Deny") for each area agents may reach. Registry I/O: call off the UI thread.</summary>
internal static class PrivacyConsent
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public static IReadOnlyList<ReachItem> Read() =>
        SettingsActionArgs.Reach.Select(r => new ReachItem(r.Id, r.Label, State(r.ConsentKey))).ToList();

    private static ReachState State(string key)
    {
        try { return PrivacyConsentMapping.Resolve(key, ReadValue); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return ReachState.Unknown;
        }
    }

    private static string? ReadValue(string subKey)
    {
        using var consent = Registry.CurrentUser.OpenSubKey(ConsentStore + subKey);
        return consent?.GetValue("Value") as string;
    }
}
