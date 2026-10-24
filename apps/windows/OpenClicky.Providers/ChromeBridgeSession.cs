using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;

namespace OpenClicky.Providers;

/// <summary>A bounded browser capability snapshot for one companion task.</summary>
public sealed class ChromeBridgeSession
{
    public static readonly string[] ToolNames = ["browser_observe", "browser_click", "browser_type_text", "browser_follow_link"];
    public static readonly string[] McpToolNames = ["openclicky_browser_observe", "openclicky_browser_click", "openclicky_browser_type_text", "openclicky_browser_follow_link"];
    private readonly IChromeBridgeHost _host;
    private readonly IChromeActionBroker _broker;
    private readonly AgentAccessMode _access;
    private readonly HashSet<string> _allowedOrigins;
    private readonly IReadOnlyList<string> _excludedTitles;
    private readonly SemaphoreSlim _calls = new(1, 1);
    private BrowserObservation? _observation;
    private bool _endedByRejection;
    private bool _failedApprovedAction;
    public AgentAccessMode AccessSnapshot => _access;
    public bool HasFailedApprovedAction => _failedApprovedAction;
    public bool WasDeclined => _endedByRejection;

    public static bool TryMapToolName(string name, out string mapped)
    {
        const string prefix = "openclicky_";
        mapped = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : "";
        return ToolNames.Contains(mapped, StringComparer.Ordinal);
    }

    public ChromeBridgeSession(IChromeBridgeHost host, IChromeActionBroker broker, AgentAccessMode access,
        IEnumerable<string> allowedOrigins, IEnumerable<string>? excludedTitles = null)
    {
        _host = host;
        _broker = broker;
        _access = Enum.IsDefined(access) ? access : AgentAccessMode.ConfirmEach;
        _allowedOrigins = allowedOrigins.Select(NormalizeOrigin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_allowedOrigins.Count == 0) throw new ArgumentException("At least one exact HTTP(S) origin is required.", nameof(allowedOrigins));
        _excludedTitles = excludedTitles?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
    }

    public async Task<DesktopToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken token)
    {
        await _calls.WaitAsync(token).ConfigureAwait(false);
        try { return await CallCoreAsync(tool, arguments, token).ConfigureAwait(false); }
        finally { _calls.Release(); }
    }

    private async Task<DesktopToolResult> CallCoreAsync(string tool, JsonElement arguments, CancellationToken token)
    {
        if (!ToolNames.Contains(tool, StringComparer.Ordinal)) return Error($"Unknown tool '{tool}'.");
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined)) return Error("Tool arguments must be an object.");
        if (_endedByRejection) return Error("This browser task ended when the user declined an action. Start a new task to continue.");

        if (tool == "browser_observe")
        {
            var beforeRead = await _host.GetCurrentContextAsync(token).ConfigureAwait(false);
            if (!Allowed(beforeRead)) return Error("This browser origin or title is excluded. No page content was read.");
            _observation = await _host.ObserveAsync(token).ConfigureAwait(false);
            if (!Allowed(_observation.Context)) { _observation = null; return Error("The browser destination changed to an excluded page during observation."); }
            return new(false, [new("text", JsonSerializer.Serialize(new
            {
                observation_id = _observation.Id,
                tab_id = _observation.Context.TabId,
                frame_id = _observation.Context.FrameId,
                navigation_revision = _observation.Context.NavigationRevision,
                origin = _observation.Context.Origin,
                title = _observation.Context.Title,
                document = _observation.Document,
                links = _observation.Links,
            }))]);
        }

        if (_access == AgentAccessMode.Preview) return Error("Preview mode: browser mutations are disabled for this task.");
        if (_observation is null) return Error("Observe the current browser tab before requesting an action.");
        var values = ReadArguments(arguments);
        if (!Take(values, "observation_id", out var observationId) || observationId != _observation.Id)
            return Error("The action does not match the current browser observation. Observe again.");
        if (!Take(values, "navigation_revision", out var revisionText) || !long.TryParse(revisionText, out var revision)
            || revision != _observation.Context.NavigationRevision)
            return Error("The action does not match the current navigation. Observe again.");

        var validation = Validate(tool, values, _observation);
        if (validation is not null) return Error(validation);
        values["tab_id"] = _observation.Context.TabId;
        values["frame_id"] = _observation.Context.FrameId;
        values["navigation_revision"] = revision.ToString();
        var current = await _host.GetCurrentContextAsync(token).ConfigureAwait(false);
        if (!Allowed(current)) return Error("This browser origin or title is excluded. No action was performed.");
        if (!SameDocument(current, _observation.Context)) return Error("The tab, frame, or navigation changed. Observe again.");

        var rationale = values.Remove("rationale", out var reason) ? reason : null;
        var request = new ActionRequest(tool, values, rationale);
        if (AgentAccessPolicy.RequiresConfirmation(_access, request)
            && !await _broker.ApproveAsync(request, token).ConfigureAwait(false))
        {
            _endedByRejection = true;
            _observation = null;
            return Error("The user declined the browser action.");
        }

        current = await _host.GetCurrentContextAsync(token).ConfigureAwait(false);
        if (!Allowed(current)) { _failedApprovedAction = true; return Error("The browser changed to an excluded page before execution. No action was performed."); }
        if (!SameDocument(current, _observation.Context)) { _failedApprovedAction = true; return Error("The tab, frame, or navigation changed before execution. No action was performed."); }
        BrowserActionResult result;
        try { result = await _host.ExecuteAsync(new(tool, values, rationale), token).ConfigureAwait(false); }
        catch { _failedApprovedAction = true; throw; }
        if (!result.Success) _failedApprovedAction = true;
        _observation = null;
        return new(!result.Success, [new("text", result.Message)]);
    }

    private static string? Validate(string tool, IReadOnlyDictionary<string, string> values, BrowserObservation observation) => tool switch
    {
        "browser_click" when !Present(values, "element_id", 128) => "browser_click requires an element_id returned by the observation.",
        "browser_type_text" when !Present(values, "element_id", 128) || !Present(values, "text", 12_000) => "browser_type_text requires an observed element_id and text (1-12,000 characters).",
        "browser_follow_link" when !values.TryGetValue("link_id", out var id) || !observation.Links.ContainsKey(id) => "browser_follow_link requires a link_id returned by the observation.",
        _ => null,
    };

    private static bool SameDocument(BrowserContext left, BrowserContext right) =>
        left.TabId == right.TabId && left.FrameId == right.FrameId && left.NavigationRevision == right.NavigationRevision
        && Uri.TryCreate(left.Origin, UriKind.Absolute, out var leftOrigin) && Uri.TryCreate(right.Origin, UriKind.Absolute, out var rightOrigin)
        && leftOrigin.Scheme == rightOrigin.Scheme && leftOrigin.Authority == rightOrigin.Authority;

    private bool Allowed(BrowserContext context) => _allowedOrigins.Contains(NormalizeOrigin(context.Origin))
        && !AgentDenyList.IsDenied(context.Title, _excludedTitles);

    private static string NormalizeOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0 || uri.PathAndQuery != "/" || uri.Fragment.Length > 0)
            throw new ArgumentException("Browser origins must be exact HTTP(S) origins ending in '/'.", nameof(value));
        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }

    private static bool Present(IReadOnlyDictionary<string, string> values, string name, int max) =>
        values.TryGetValue(name, out var value) && value.Length is > 0 && value.Length <= max;
    private static bool Take(IDictionary<string, string> values, string name, out string value)
    { if (values.TryGetValue(name, out value!)) { values.Remove(name); return true; } value = ""; return false; }
    private static Dictionary<string, string> ReadArguments(JsonElement arguments) => arguments.ValueKind == JsonValueKind.Undefined
        ? new(StringComparer.OrdinalIgnoreCase)
        : arguments.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText(), StringComparer.OrdinalIgnoreCase);
    private static DesktopToolResult Error(string message) => new(true, [new("text", message)]);
}
