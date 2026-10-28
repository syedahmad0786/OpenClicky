using System.Diagnostics;
using Microsoft.Win32;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Core.Browser;
using OpenClicky.Core.Shell;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>
/// The browser bridge: one supervised <see cref="BrowserBridgeServer"/>, a <see cref="ChromeBridgeSession"/> per task while a
/// browser is connected, and the browser card's buttons (chrome.setup). Registration writes only our own HKCU native-host key,
/// on the user's Connect click; install help opens pages and copies the path, it never clicks or installs anything.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private BrowserBridgeServer? _browserBridge;
    /// <summary>Released by Connect: wakes a supervisor that waits because nothing is registered.</summary>
    private readonly SemaphoreSlim _browserRegistered = new(0, 1);
    /// <summary>Each live browser session's stop callback by task id, from creation to EndAsync.</summary>
    private readonly BrowserStopRegistry _browserStops = new();

    private static string BrowserManifestPath =>
        Path.Combine(BrowserNativeHostRegistrar.DefaultBridgeFolder, BrowserNativeHostRegistrar.HostName + ".json");

    private static string ExtensionManifestPath => Path.Combine(BrowserNativeHostRegistrar.DefaultExtensionFolder, "manifest.json");

    private void StartBrowserBridge() => _ = Task.Run(SuperviseBrowserBridgeAsync);

    private static bool BrowserRegistered() => BrowserConnectionStore.Load(BrowserConnectionStore.DefaultPath).Registered;

    /// <summary>
    /// The pipe is open only while the user's Connect stands, so a disconnected browser's host can't get back in.
    /// A server instance runs once: a disposed one (Disconnect) is replaced at once, a faulted one 2 s later.
    /// </summary>
    private async Task SuperviseBrowserBridgeAsync()
    {
        byte[] secret;
        try { secret = ChromeBridgeSecretStore.LoadOrCreate(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("browser_secret_error=" + ex.GetType().Name); return; }
        if (BrowserRegistered()) RefreshBrowserInstall();
        while (!_lifetime.IsCancellationRequested)
        {
            if (!BrowserRegistered())
            {
                try { await _browserRegistered.WaitAsync(_lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            var server = new BrowserBridgeServer(secret, BrowserBridgeServer.PipeName,
                () => BrowserConnectionStore.Load(BrowserConnectionStore.DefaultPath).SelectedInstance,
                id => SaveBrowserConnection(c => c with { SelectedInstance = id }));
            server.Changed += OnBrowserChanged;
            server.StopRequested += OnBrowserStopRequested;
            _browserBridge = server;
            var faulted = false;
            try { await server.RunAsync(_lifetime.Token).ConfigureAwait(false); } // returns on app exit or Disconnect's dispose
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("browser_bridge_error=" + ex.GetType().Name); faulted = true; }
            Interlocked.CompareExchange(ref _browserBridge, null, server); // a disposed server may still report IsConnected
            server.Changed -= OnBrowserChanged;
            server.StopRequested -= OnBrowserStopRequested;
            try { await server.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
            OnBrowserChanged(); // its profiles are gone from the card
            if (!faulted) continue;
            try { await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// After an update or a move the registered manifest can name an old exe and the loaded extension folder can hold old files.
    /// Rewrites only our own files when they differ; the registry key already points at the same manifest path, so no registry writes.
    /// </summary>
    private void RefreshBrowserInstall()
    {
        try
        {
            if (BrowserNativeHostRegistrar.ManifestDiffers(BrowserNativeHostRegistrar.DefaultBridgeFolder, Application.ExecutablePath))
                BrowserNativeHostRegistrar.WriteManifest(BrowserNativeHostRegistrar.DefaultBridgeFolder, Application.ExecutablePath);
            var source = DeploymentPathResolver.ResolveDirectory("chrome-extension");
            if (BrowserNativeHostRegistrar.ExtensionDiffers(source, BrowserNativeHostRegistrar.DefaultExtensionFolder))
                BrowserNativeHostRegistrar.InstallExtension(source, BrowserNativeHostRegistrar.DefaultExtensionFolder);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("browser_refresh_error=" + ex.GetType().Name); }
    }

    // Raised on pipe threads: never throw back into the server.
    private void OnBrowserChanged()
    {
        try { OnUi(() => SettingsProbe.Refresh()); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("browser_changed_error=" + ex.GetType().Name); }
    }

    /// <summary>Stop from the extension: its popup sends no task (stop every run with a browser session); a group's Stop names its task.</summary>
    private void OnBrowserStopRequested(string? task)
    {
        try
        {
            OnUi(() =>
            {
                try { _browserStops.Stop(task); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Log("browser_stop_error=" + ex.GetType().Name); }
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("browser_stop_error=" + ex.GetType().Name); }
    }

    /// <summary>
    /// The task's browser gate, on its access snapshot; null when no browser is connected. Its owner calls EndAsync on every exit.
    /// The run's hooks: <paramref name="stop"/> cancels it (the extension's Stop), <paramref name="pause"/> holds every page command
    /// while it is paused, and <paramref name="beforeApproval"/> runs before each approval and its lease is held while it asks.
    /// </summary>
    private ChromeBridgeSession? CreateBrowserSession(AgentAccessMode access, JobRun? work, string runId, Action stop,
        Func<CancellationToken, Task> pause, Func<CancellationToken, Task<IAsyncDisposable?>> beforeApproval)
    {
        if (_browserBridge is not { } bridge || !BrowserGate.OffersSession(bridge.IsConnected, BrowserRegistered())) return null;
        var identity = new BrowserTaskIdentity(runId, work?.Job.Id ?? "task-" + runId, work?.Job.Title ?? "OpenClicky task", work?.Job.Colour ?? "blue");
        return new ChromeBridgeSession(new PausableBrowserBridge(bridge, pause), new BrowserApprovalBroker(_confirm.ConfirmNonInputAsync, beforeApproval),
            access, identity, AgentDenyList.Fragments(AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra)),
            work is null ? null : _work.OutputFolder(work.Job), _browserStops.Register(runId, stop));
    }

    /// <summary>Settings → Integrations, where the browser card lives (the classic "Set up Chrome tabs" button).</summary>
    private void ShowBrowserSettings()
    {
        if (_shell is { IsActive: true }) _shell.OpenNotch(NotchTab.Settings, "integrations");
        else ShowSettings();
    }

    /// <summary>The browser card's buttons. Runs on the UI thread; failures toast through the settings action handler.</summary>
    private void RunBrowserSetup(string? arg)
    {
        if (BrowserSetupArgs.Parse(arg) is not { } request) { Log("settings: unknown browser setup arg"); return; }
        switch (request.Op)
        {
            case "connect": ConnectBrowser(request.Browser!, request.Profile!); break;
            case "openExtensions":
                var stored = BrowserConnectionStore.Load(BrowserConnectionStore.DefaultPath);
                if (FindBrowser(stored.BrowserId, stored.ProfileDirectory ?? "") is { } found) OpenExtensionsPage(found.Browser, found.Profile);
                else _shell?.AddToast("That browser or profile wasn't found.");
                break;
            case "showFolder": ShowExtensionFolder(); break;
            case "copyPath":
                Clipboard.SetText(BrowserNativeHostRegistrar.DefaultExtensionFolder);
                _shell?.AddToast("Extension folder path copied.");
                break;
            case "select": _browserBridge?.Select(request.Id!); break;
            case "get":
                OpenUrl(request.Browser switch
                {
                    "edge" => "https://www.microsoft.com/edge/download",
                    "brave" => "https://brave.com/download/",
                    _ => "https://www.google.com/chrome/",
                });
                break;
            case "disconnect": DisconnectBrowser(); break;
        }
        SettingsProbe.Refresh();
    }

    private void ConnectBrowser(string browserId, string profileDir)
    {
        if (FindBrowser(browserId, profileDir) is not { } found) { _shell?.AddToast("That browser or profile wasn't found."); return; }
        var (browser, profile) = found;
        BrowserNativeHostRegistrar.InstallExtension(DeploymentPathResolver.ResolveDirectory("chrome-extension"), BrowserNativeHostRegistrar.DefaultExtensionFolder);
        var manifest = BrowserNativeHostRegistrar.WriteManifest(BrowserNativeHostRegistrar.DefaultBridgeFolder, Application.ExecutablePath);
        BrowserNativeHostRegistrar.Register(Registry.CurrentUser, browser.Kind.VendorKey, manifest);
        SaveBrowserConnection(c => c with { BrowserId = browser.Kind.Id, ProfileDirectory = profile.Directory, Registered = true });
        try { _browserRegistered.Release(); } catch (SemaphoreFullException) { } // already signalled
        OpenExtensionsPage(browser, profile);
        ShowExtensionFolder();
    }

    /// <summary>Unregisters every vendor key that points at our manifest; the extension itself stays until the user removes it.</summary>
    private void DisconnectBrowser()
    {
        foreach (var vendorKey in BrowserCatalog.Known.Select(k => k.VendorKey).Distinct(StringComparer.OrdinalIgnoreCase))
            if (BrowserNativeHostRegistrar.IsRegistered(Registry.CurrentUser, vendorKey, BrowserManifestPath))
                BrowserNativeHostRegistrar.Unregister(Registry.CurrentUser, vendorKey);
        SaveBrowserConnection(c => c with { Registered = false });
        // Tell the extension (it shows setup and hands its tabs over), then drop live connections: the supervisor sees
        // nothing registered and keeps the pipe closed until the next Connect.
        if (_browserBridge is { } bridge)
            _ = Task.Run(async () =>
            {
                try { await bridge.NotifyUnregisteredAsync().ConfigureAwait(false); }
                finally { try { await bridge.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) when (ex is not OutOfMemoryException) { } }
            });
        _shell?.AddToast("The browser is disconnected. You can remove the OpenClicky extension from your browser's extensions page.");
    }

    /// <summary>The detected browser and its profile; "" picks the first profile (Default).</summary>
    private static (DetectedBrowser Browser, BrowserProfile Profile)? FindBrowser(string? browserId, string profileDir)
    {
        if (BrowserCatalog.Detect(new WindowsBrowserProbe()).FirstOrDefault(b => b.Kind.Id == browserId) is not { } browser) return null;
        var profile = profileDir.Length == 0 ? browser.Profiles.FirstOrDefault() : browser.Profiles.FirstOrDefault(p => p.Directory == profileDir);
        return profile is null ? null : (browser, profile);
    }

    private static void OpenExtensionsPage(DetectedBrowser browser, BrowserProfile profile)
    {
        var start = new ProcessStartInfo(browser.ExePath) { UseShellExecute = false };
        start.ArgumentList.Add("--profile-directory=" + profile.Directory);
        start.ArgumentList.Add("chrome://extensions/");
        Launch(start);
    }

    private static void ShowExtensionFolder() => Launch(new ProcessStartInfo("explorer.exe", $"/select,\"{ExtensionManifestPath}\""));

    private static void SaveBrowserConnection(Func<BrowserConnection, BrowserConnection> change) =>
        BrowserConnectionStore.Save(BrowserConnectionStore.DefaultPath, change(BrowserConnectionStore.Load(BrowserConnectionStore.DefaultPath)));

    /// <summary>The card's state and view for the settings probe; called on its worker thread (detection reads the disk and registry).</summary>
    private (ChromeState Chrome, BrowserCardView Browser) BrowserCardState()
    {
        var detected = BrowserCatalog.Detect(new WindowsBrowserProbe());
        var connection = BrowserConnectionStore.Load(BrowserConnectionStore.DefaultPath);
        var bridge = _browserBridge;
        var instances = bridge?.Instances ?? Array.Empty<BrowserInstance>();
        var view = new BrowserCardView(connection.BrowserId, connection.ProfileDirectory,
            detected.Select(b => new BrowserOptionView(b.Kind.Id, b.Kind.Name, b.Profiles.Select(p => new BrowserProfileView(p.Directory, p.Name)).ToList())).ToList(),
            instances.Select(i => new BrowserInstanceView(i.Id, i.Brand + " · " + i.Id[..Math.Min(8, i.Id.Length)], i.Selected)).ToList(),
            BrowserNativeHostRegistrar.DefaultExtensionFolder);
        var kind = BrowserCatalog.Known.FirstOrDefault(k => k.Id == connection.BrowserId);
        var registered = connection.Registered && kind is not null
            && BrowserNativeHostRegistrar.IsRegistered(Registry.CurrentUser, kind.VendorKey, BrowserManifestPath);
        return (BrowserGate.CardState(detected.Count > 0, registered, instances.Count, bridge?.IsWorking == true), view);
    }
}
