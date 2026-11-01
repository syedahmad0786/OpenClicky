namespace OpenClicky.Core.Browser;

// View records for Settings -> Integrations -> Browser. Mirror of integrations.browser in
// apps/companion-workspace/src/shell/contract.ts; ShellSettingsBuilder.Clamp keeps them inside the contract caps.

public sealed record BrowserProfileView(string Dir, string Name);

public sealed record BrowserOptionView(string Id, string Name, IReadOnlyList<BrowserProfileView> Profiles);

/// <summary>One connected browser profile; <paramref name="Selected"/> is the one agents use.</summary>
public sealed record BrowserInstanceView(string Id, string Label, bool Selected);

/// <summary>What the card shows: the stored choice, the detected browsers with their profiles, the connected profiles and where the extension lives.</summary>
public sealed record BrowserCardView(
    string? BrowserId,
    string? ProfileDir,
    IReadOnlyList<BrowserOptionView> Browsers,
    IReadOnlyList<BrowserInstanceView> Instances,
    string ExtensionFolder)
{
    public static BrowserCardView Empty { get; } = new(null, null, [], [], "");
}
