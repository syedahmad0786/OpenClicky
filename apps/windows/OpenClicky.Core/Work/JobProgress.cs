using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Work;

/// <summary>What a running job shows while it works: the model's checklist, the last 12 step phrases and the newest as the live line.</summary>
public sealed record JobLiveState(IReadOnlyList<PlanStep> Plan, IReadOnlyList<string> Steps, string? LiveLine)
{
    public const int MaxSteps = 12;

    public static JobLiveState Empty { get; } = new([], [], null);

    /// <summary>The state after the engine used <paramref name="tool"/>: a todo list replaces the plan, any other known tool adds a phrase.</summary>
    public JobLiveState Apply(string tool, JsonElement? input)
    {
        if (JobProgress.IsTodoTool(tool))
            return input is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("todos", out var todos) ? this with { Plan = JobProgress.FromTodos(todos) } : this;
        return JobProgress.Phrase(tool, input) is { } phrase
            ? this with { Steps = [.. Steps.Append(phrase).TakeLast(MaxSteps)], LiveLine = phrase }
            : this;
    }
}

/// <summary>Turns the engine's tool calls into the plan checklist and short step phrases. Tool input is untrusted: only a few short fields are read.</summary>
public static class JobProgress
{
    public const int MaxPlan = 24, MaxName = 40, MaxStepText = 200;

    // Longest key first: "browser_click" must win over "click" for "openclicky_browser_click".
    private static readonly (string Tool, string Phrase)[] Fixed = new (string, string)[]
    {
        ("observe_screen", "Looking at the screen"), ("uia", "Reading the window"), ("click", "Clicking"), ("type_text", "Typing"),
        ("hotkey", "Pressing keys"), ("scroll", "Scrolling"), ("list_outputs", "Checking saved files"),
        ("browser_observe", "Reading the page"), ("browser_click", "Clicking"), ("browser_type_text", "Typing"), ("browser_follow_link", "Following a link"),
        ("webfetch", "Reading a web page"), ("websearch", "Searching the web"), ("bash", "Running a command"), ("read", "Reading a file"),
        ("edit", "Editing a file"), ("write", "Editing a file"), ("grep", "Looking through files"), ("glob", "Looking through files"), ("list", "Looking through files"),
    }.OrderByDescending(x => x.Item1.Length).ToArray();

    /// <summary>Does the tool name (possibly prefixed by the engine) end in <paramref name="key"/>?</summary>
    private static bool Is(string tool, string key) =>
        tool.Equals(key, StringComparison.OrdinalIgnoreCase) || tool.EndsWith("_" + key, StringComparison.OrdinalIgnoreCase);

    public static bool IsTodoTool(string tool) => Is(tool, "todowrite");

    /// <summary>The opencode todowrite "todos" array as plan steps: up to 24, Done for completed or cancelled. Anything else gives an empty plan.</summary>
    public static IReadOnlyList<PlanStep> FromTodos(JsonElement todos)
    {
        if (todos.ValueKind != JsonValueKind.Array) return [];
        var plan = new List<PlanStep>();
        foreach (var item in todos.EnumerateArray())
        {
            if (plan.Count >= MaxPlan) break;
            if (item.ValueKind != JsonValueKind.Object || Field(item, "content", MaxStepText) is not { } text) continue;
            plan.Add(new PlanStep(text, Field(item, "status", 40) is "completed" or "cancelled"));
        }
        return plan;
    }

    /// <summary>A short phrase for the tool, or null when it is not one the job card talks about.</summary>
    public static string? Phrase(string tool, JsonElement? input)
    {
        if (string.IsNullOrWhiteSpace(tool)) return null;
        if (Is(tool, "open_app")) return Name(input, "Opening ", "an app", "app", "app_id", "name");
        if (Is(tool, "write_output"))
        {
            var file = input is { } o && Field(o, "path", 4_096) is { } path ? Cut(Clean(Path.GetFileName(path))) : "";
            return file.Length > 0 ? "Saving " + file : "Saving a file";
        }
        if (Is(tool, "connector_call")) return Name(input, "Using ", "a connector", "connector");
        foreach (var (key, phrase) in Fixed) if (Is(tool, key)) return phrase;
        return null;
    }

    // Same opening as JobResult's block: a fence may be indented by spaces or tabs.
    private static readonly Regex FenceLine = new(@"^[ \t]*```", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The streamed reply up to its first line that starts a code fence (indented or not), so the raw result block is never shown.</summary>
    public static string LiveText(string text)
    {
        try { return (FenceLine.Match(text) is { Success: true } fence ? text[..fence.Index] : text).Trim(); }
        catch (RegexMatchTimeoutException) { return ""; }
    }

    private static string Name(JsonElement? input, string verb, string fallback, params string[] keys)
    {
        if (input is { } o)
            foreach (var key in keys)
                if (Field(o, key, 4_096) is { } value && Cut(Clean(value)) is { Length: > 0 } name) return verb + name;
        return verb + fallback;
    }

    private static string? Field(JsonElement element, string name, int max) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && v.GetString() is { } s && !string.IsNullOrWhiteSpace(s) ? (s.Length > max ? s[..max] : s).Trim() : null;

    /// <summary>Control characters and runs of spaces become one space.</summary>
    private static string Clean(string value) =>
        string.Join(' ', new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Cut(string value) => value.Length > MaxName ? value[..MaxName].TrimEnd() : value;
}
