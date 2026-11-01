using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Core.Browser;

namespace OpenClicky.Providers;

/// <summary>Who a job's browser work belongs to: its task id, tab-group owner, group title and colour.</summary>
public sealed record BrowserTaskIdentity(string Task, string Owner, string GroupTitle, string Color);

/// <summary>
/// One job's gate in front of the browser extension: op validation, the access snapshot (fixed at creation, never widened),
/// approvals, upload checks, then one command at a time. The deny list travels with every command; it is a convenience
/// the extension checks, not a security boundary.
/// </summary>
public sealed class ChromeBridgeSession
{
    public static readonly string[] ToolNames = ["browser"];
    public static readonly string[] McpToolNames = ["openclicky_browser"];
    private static readonly HashSet<string> AppSideArgs = new(["op", "rationale", "say", "save"], StringComparer.Ordinal);
    private readonly IBrowserBridge _bridge;
    private readonly IChromeActionBroker _broker;
    private readonly AgentAccessMode _access;
    private readonly BrowserTaskIdentity _identity;
    private readonly IReadOnlyList<string> _deny;
    private readonly string? _outputFolder;
    private readonly IDisposable _tracker;
    private readonly IDisposable? _lease;
    private readonly SemaphoreSlim _calls = new(1, 1);
    private volatile bool _declined;
    private volatile bool _failedApprovedAction;
    private int _ended;
    public AgentAccessMode AccessSnapshot => _access;
    public bool HasFailedApprovedAction => _failedApprovedAction;
    public bool WasDeclined => _declined;

    public static bool TryMapToolName(string name, out string mapped)
    {
        const string prefix = "openclicky_";
        mapped = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : "";
        return ToolNames.Contains(mapped, StringComparer.Ordinal);
    }

    public ChromeBridgeSession(IBrowserBridge bridge, IChromeActionBroker broker, AgentAccessMode access,
        BrowserTaskIdentity identity, IReadOnlyList<string> deny, string? outputFolder, IDisposable? lease = null)
    {
        _bridge = bridge;
        _broker = broker;
        _access = Enum.IsDefined(access) ? access : AgentAccessMode.ConfirmEach;
        _identity = identity.GroupTitle.Length > 32 ? identity with { GroupTitle = identity.GroupTitle[..32] } : identity;
        _deny = deny;
        _outputFolder = outputFolder;
        _tracker = bridge.TrackTask(identity.Task);
        _lease = lease;
    }

    public async Task<DesktopToolResult> CallAsync(string tool, JsonElement arguments, CancellationToken token)
    {
        await _calls.WaitAsync(token).ConfigureAwait(false);
        try { return await CallCoreAsync(tool, arguments, token).ConfigureAwait(false); }
        finally { _calls.Release(); }
    }

    /// <summary>Sends <c>end{tidy}</c> once, stops tracking the task and releases <c>lease</c> (e.g. its stop registration). Never throws.</summary>
    public async Task EndAsync(bool tidy, CancellationToken token)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1) return;
        try
        {
            await _bridge.SendAsync(Command("end", null, JsonSerializer.SerializeToElement(new { tidy })), TimeSpan.FromSeconds(10), token)
                .ConfigureAwait(false);
        }
        catch { /* best effort: the next sweep releases the task's tabs */ }
        finally { _tracker.Dispose(); _lease?.Dispose(); }
    }

    private async Task<DesktopToolResult> CallCoreAsync(string tool, JsonElement args, CancellationToken token)
    {
        if (!ToolNames.Contains(tool, StringComparer.Ordinal)) return Error($"Unknown tool '{tool}'.");
        if (Volatile.Read(ref _ended) == 1) return Error("This job's browser session has ended.");
        if (_declined) return Error("This job's browser work ended when you declined an action.");
        var op = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("op", out var o) && o.ValueKind == JsonValueKind.String
            ? o.GetString()! : "";
        if (BrowserOps.Validate(op, args) is { } problem) return Error(problem);
        var kind = BrowserOps.Kind(op, args);
        if (kind == BrowserOpKind.Lifecycle) return Error("Only OpenClicky ends or sweeps browser work.");
        var save = op == "screenshot" && Bool(args, "save");
        if (_access == AgentAccessMode.Preview && (kind is not (BrowserOpKind.Read or BrowserOpKind.Local) || save))
            return Error("Preview mode: the browser is read-only for this job.");
        var uploads = BrowserOps.UploadPaths(op, args);
        if (uploads.Count > 0 && BrowserUploadPolicy.Validate(uploads) is { } refused) return Error(refused);

        var rationale = Text(args, "rationale");
        var request = new ActionRequest("browser." + op, Flatten(op, args), rationale);
        var ask = kind switch
        {
            BrowserOpKind.AlwaysAsk => _access != AgentAccessMode.FullAccess,
            BrowserOpKind.Act => AgentAccessPolicy.RequiresConfirmation(_access, request),
            _ => false,
        };
        if (ask && !await _broker.ApproveAsync(request, token).ConfigureAwait(false))
        {
            _declined = true;
            return Error("You declined that browser action.");
        }

        var acting = kind is BrowserOpKind.Act or BrowserOpKind.AlwaysAsk;
        BrowserReply reply;
        var say = Text(args, "say") is { } note && note.Length > 160 ? note[..160] : Text(args, "say");
        try { reply = await _bridge.SendAsync(Command(op, say, ModelArgs(args)), Timeout(op, args), token).ConfigureAwait(false); }
        catch { if (acting) _failedApprovedAction = true; throw; }
        if (!reply.Ok)
        {
            if (acting) _failedApprovedAction = true;
            return Error(reply.Text);
        }
        if (reply.ImageBase64 is not { } image) return new(false, [new("text", reply.Text)]);
        var mime = reply.ImageMimeType ?? "image/jpeg";
        var text = save ? reply.Text + "\n" + Save(image) : reply.Text;
        return new(false, [new("text", text), new("image", null, image, mime)]);
    }

    private string Save(string base64)
    {
        if (string.IsNullOrWhiteSpace(_outputFolder)) return "This job has no output folder, so the screenshot was not saved.";
        try
        {
            Directory.CreateDirectory(_outputFolder);
            var stem = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}";
            var name = stem + ".jpg";
            for (var n = 2; File.Exists(Path.Combine(_outputFolder, name)); n++) name = $"{stem}-{n}.jpg";
            File.WriteAllBytes(Path.Combine(_outputFolder, name), Convert.FromBase64String(base64));
            return $"Saved to the job's output folder as {name}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return "The screenshot could not be saved to the job's output folder.";
        }
    }

    private BrowserCommand Command(string op, string? say, JsonElement args) =>
        new(op, _identity.Task, _identity.Owner, _identity.GroupTitle, _identity.Color, _deny, say, args);

    // The extension gives wait_for its wait + 10 s; the app adds 5 s more so the extension's answer arrives first.
    private static TimeSpan Timeout(string op, JsonElement args) => op switch
    {
        "download" => TimeSpan.FromSeconds(60),
        "batch" => TimeSpan.FromSeconds(45),
        "wait_for" => TimeSpan.FromSeconds((args.TryGetProperty("timeout", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : 10) + 15),
        _ => TimeSpan.FromSeconds(25),
    };

    /// <summary>The args the extension sees: everything but the app-side keys, with nulls (meaning absent) dropped.</summary>
    private static JsonElement ModelArgs(JsonElement args) => JsonSerializer.SerializeToElement(
        args.EnumerateObject().Where(p => !AppSideArgs.Contains(p.Name) && p.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(p => p.Name, p => DropNulls(p.Value)));

    private static object? DropNulls(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(p => p.Name, p => DropNulls(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(DropNulls).ToArray(),
        _ => value,
    };

    /// <summary>What the approval shows: one entry per arg, or one line per batch step.</summary>
    private static Dictionary<string, string> Flatten(string op, JsonElement args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in args.EnumerateObject().Where(p => !AppSideArgs.Contains(p.Name) && p.Value.ValueKind != JsonValueKind.Null))
        {
            if (op == "batch" && p.Name == "steps")
            {
                var i = 0;
                foreach (var step in p.Value.EnumerateArray())
                    values[$"step {++i}"] = Text(step, "op") + " " + string.Join(" ", step.EnumerateObject()
                        .Where(s => s.Name != "op" && s.Value.ValueKind != JsonValueKind.Null).Select(s => $"{s.Name}={Value(s.Value)}"));
            }
            else values[p.Name] = Value(p.Value);
        }
        return values;
    }

    private static string Value(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
    private static string? Text(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement args, string name) => args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static DesktopToolResult Error(string message) => new(true, [new("text", message)]);
}
