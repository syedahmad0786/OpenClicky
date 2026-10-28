using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace OpenClicky.Agent;

/// <summary>Only launches entries discovered in Windows' installed application catalogue.</summary>
internal sealed class InstalledAppExecutor
{
    private readonly Dictionary<string, (string Path, string Name, string? ProcessPath, string? AppUserModelId)> _listed = new(StringComparer.Ordinal);

    public Task<AgentActionResult> ExecuteAsync(AgentActionRequest request, bool confirmed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.ToolName == AgentToolNames.OpenApp && !confirmed)
            return Task.FromResult(new AgentActionResult(false, "Confirmation required for open_app.", true));
        if (request.ToolName == AgentToolNames.ListApps &&
            (!request.Arguments.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query) || query.Length > 80))
            return Task.FromResult(new AgentActionResult(false, "Provide an app name to search (1–80 characters).", false));
        var completion = new TaskCompletionSource<AgentActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? shell = null, folder = null, items = null;
            try
            {
                token.ThrowIfCancellationRequested();
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
                folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
                if (folder is null) throw new InvalidOperationException("Windows' app catalogue is unavailable.");
                items = ((dynamic)folder).Items();
                string? expectedPath = null, expectedName = null, expectedProcessPath = null, expectedAppUserModelId = null;
                if (request.ToolName == AgentToolNames.OpenApp)
                {
                    var id = request.Arguments.GetValueOrDefault("app_id", "");
                    lock (_listed)
                    {
                        if (!_listed.TryGetValue(id, out var app))
                            throw new InvalidOperationException("Search with list_apps first and use an exact returned app_id.");
                        expectedPath = app.Path;
                        expectedName = app.Name;
                        expectedProcessPath = app.ProcessPath;
                        expectedAppUserModelId = app.AppUserModelId;
                    }
                }
                var matches = new List<object>();
                var count = Math.Min((int)((dynamic)items).Count, 2000);
                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    object? item = null;
                    try
                    {
                        item = ((dynamic)items).Item(i);
                        var name = (string)((dynamic)item).Name;
                        var path = (string)((dynamic)item).Path;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                        if (expectedPath is not null)
                        {
                            if (!string.Equals(path, expectedPath, StringComparison.Ordinal) || name != expectedName) continue;
                            token.ThrowIfCancellationRequested();
                            ((dynamic)item).InvokeVerb("open");
                            AppLaunchObservation? observed = null;
                            var deadline = DateTime.UtcNow.AddSeconds(8);
                            while (DateTime.UtcNow < deadline)
                            {
                                token.ThrowIfCancellationRequested();
                                observed = AppLaunchVerifier.ObserveForeground(expectedAppUserModelId);
                                if (AppLaunchVerifier.Matches(expectedProcessPath, expectedAppUserModelId, observed))
                                {
                                    completion.TrySetResult(new(true, $"Verified {name} is open in the foreground.", false));
                                    return;
                                }
                                Thread.Sleep(100);
                            }
                            var destination = string.IsNullOrWhiteSpace(observed?.Title) ? "no visible destination window" : $"'{observed.Title}'";
                            var expectedKind = !string.IsNullOrWhiteSpace(expectedProcessPath) ? "process-path"
                                : !string.IsNullOrWhiteSpace(expectedAppUserModelId) ? "app-user-model-id" : "none";
                            var diagnostic = observed is null ? "identity observation unavailable"
                                : $"pid={observed.ProcessId}; expected={expectedKind}; path={(string.IsNullOrWhiteSpace(observed.ProcessPath) ? "missing" : "available")}; app_id={(string.IsNullOrWhiteSpace(observed.AppUserModelId) ? "missing" : "available")}; app_id_result={(observed.AppUserModelIdResult?.ToString() ?? "unavailable")}";
                            completion.TrySetResult(new(false, $"Windows accepted opening {name}, but OpenClicky could not verify that destination. Observed {destination}. Identity check: {diagnostic}.", false));
                            return;
                        }
                        if (!name.Contains(request.Arguments["query"].Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                        var processPath = TryExtendedProperty(item, "System.Link.TargetParsingPath");
                        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath))
                            processPath = Path.IsPathFullyQualified(path) ? path : null;
                        var appUserModelId = TryExtendedProperty(item, "System.AppUserModel.ID");
                        if (string.IsNullOrWhiteSpace(appUserModelId) && path.Contains('!', StringComparison.Ordinal))
                            appUserModelId = path;
                        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
                        lock (_listed)
                        {
                            if (_listed.Count >= 200) _listed.Clear();
                            _listed[id] = (path, name, processPath, appUserModelId);
                        }
                        matches.Add(new { app_id = id, name });
                        if (matches.Count == 20) break;
                    }
                    finally { Release(item); }
                }
                completion.TrySetResult(expectedPath is null
                    ? new(true, matches.Count == 0 ? "No installed app matched. Ask the user which app or website to use; do not invent a path or launch command." : JsonSerializer.Serialize(matches), false)
                    : new(false, "The app catalogue changed. Search again before launching.", false));
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetResult(new(false, $"App lookup/launch failed: {ex.Message}", false)); }
            finally { Release(items); Release(folder); Release(shell); }
        }) { IsBackground = true, Name = "OpenClicky installed apps" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static string? TryExtendedProperty(object item, string property)
    {
        try { return ((dynamic)item).ExtendedProperty(property) as string; }
        catch { return null; }
    }
}

internal sealed record AppLaunchObservation(nint Window, string? Title, string? ProcessPath, string? AppUserModelId,
    uint ProcessId = 0, int? AppUserModelIdResult = null);

internal sealed record HostedAppIdentityCandidate(nint Window, uint ProcessId, bool Visible, string? AppUserModelId);

internal static class AppLaunchVerifier
{
    public static bool Matches(string? expectedProcessPath, string? expectedAppUserModelId, AppLaunchObservation? observed)
    {
        if (observed is null || observed.Window == nint.Zero) return false;
        if (!string.IsNullOrWhiteSpace(expectedProcessPath))
            return !string.IsNullOrWhiteSpace(observed.ProcessPath)
                && string.Equals(Path.GetFullPath(observed.ProcessPath), Path.GetFullPath(expectedProcessPath), StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(expectedAppUserModelId))
            return string.Equals(observed.AppUserModelId, expectedAppUserModelId, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public static AppLaunchObservation? ObserveForeground(string? expectedAppUserModelId = null)
    {
        var window = GetForegroundWindow();
        if (window == nint.Zero) return null;
        var length = GetWindowTextLength(window);
        var title = length <= 0 ? null : new StringBuilder(length + 1);
        if (title is not null) _ = GetWindowText(window, title, title.Capacity);
        _ = GetWindowThreadProcessId(window, out var processId);
        var process = ObserveProcess(processId);
        var appUserModelId = process.AppUserModelId;
        if (string.IsNullOrWhiteSpace(appUserModelId) && !string.IsNullOrWhiteSpace(expectedAppUserModelId))
            appUserModelId = ResolveHostedIdentity(expectedAppUserModelId, ObserveHostedCandidates(window, processId));
        return new(window, title?.ToString(), process.ProcessPath, appUserModelId, processId, process.AppUserModelIdResult);
    }

    internal static string? ResolveHostedIdentity(string expectedAppUserModelId,
        IEnumerable<HostedAppIdentityCandidate> candidates) =>
        candidates.FirstOrDefault(candidate => candidate.Window != nint.Zero
            && candidate.ProcessId != 0
            && candidate.Visible
            && string.Equals(candidate.AppUserModelId, expectedAppUserModelId, StringComparison.OrdinalIgnoreCase))
        ?.AppUserModelId;

    private static IReadOnlyList<HostedAppIdentityCandidate> ObserveHostedCandidates(nint foregroundWindow, uint foregroundProcessId)
    {
        var candidates = new List<HostedAppIdentityCandidate>();
        _ = EnumChildWindows(foregroundWindow, (child, data) =>
        {
            _ = GetWindowThreadProcessId(child, out var childProcessId);
            if (childProcessId == 0 || childProcessId == foregroundProcessId) return true;
            var childProcess = ObserveProcess(childProcessId);
            candidates.Add(new(child, childProcessId, IsWindowVisible(child), childProcess.AppUserModelId));
            return true;
        }, nint.Zero);
        return candidates;
    }

    internal static (string? ProcessPath, string? AppUserModelId, int? AppUserModelIdResult) ObserveProcess(uint processId)
    {
        if (processId == 0) return (null, null, null);
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == nint.Zero) return (null, null, null);
        try
        {
            var appId = ReadAppUserModelId(process);
            return (ReadProcessPath(process), appId.Value, appId.Result);
        }
        finally { _ = CloseHandle(process); }
    }

    private static string? ReadProcessPath(nint process)
    {
        var capacity = 32_768;
        var value = new StringBuilder(capacity);
        return QueryFullProcessImageName(process, 0, value, ref capacity) ? value.ToString() : null;
    }

    private static (string? Value, int Result) ReadAppUserModelId(nint process)
    {
        uint length = 256;
        var value = new StringBuilder((int)length);
        var result = GetApplicationUserModelId(process, ref length, value);
        if (result == ErrorInsufficientBuffer && length > 0 && length <= 32_768)
        {
            value = new StringBuilder((int)length);
            result = GetApplicationUserModelId(process, ref length, value);
        }
        return (result == 0 ? value.ToString() : null, result);
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder appUserModelId);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    private delegate bool EnumWindowsProc(nint window, nint data);
}
