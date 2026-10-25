using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenClicky.Core.Work;

public sealed record WorkIndexJob(string Title, string RelativeFolder, string Status);

public sealed record WorkIndexRun(DateTimeOffset At, string Outcome, string Summary);

/// <summary>
/// Renders WORK.md: "# Work", the job list, then the app-owned runs block. Only the heading with the "- " and blank lines
/// right under it, and the block, are regenerated; every other line is the user's and is kept, across any number of renders.
/// </summary>
public static class WorkIndex
{
    public const string Heading = "# Work";
    public const string RunsOpen = "<!-- openclicky:runs -->";
    public const string RunsClose = "<!-- /openclicky:runs -->";
    public const int MaxRuns = 50;
    private const int MaxSummary = 300;
    private static readonly Regex HeadingLine = new(@"^# Work[ \t]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static string Render(string existing, IReadOnlyList<WorkIndexJob> jobs, IReadOnlyList<WorkIndexRun> runs)
    {
        var text = existing.ReplaceLineEndings("\n");
        var block = RunsBlock(runs);
        var list = Heading + "\n\n" + string.Concat(jobs.Select(j => $"- {Clean(j.Title)} — {Clean(j.RelativeFolder)} — {StatusWord(j.Status)}\n")) + (jobs.Count > 0 ? "\n" : "");
        if (text.Trim().Length == 0) return list + block + "\n";
        var hasBlock = FindBlock(text, out var open, out var end);
        var heading = HeadingLine.Match(text);
        if (heading.Success && (!hasBlock || heading.Index < open))
        {
            // The app owns only the heading and the job-list lines right under it ("- " lines and blank lines);
            // anything else, even between the list and the block, is the user's and stays where it is.
            var listEnd = JobListEnd(text, heading.Index + heading.Length, hasBlock ? open : text.Length);
            var rest = text[listEnd..];
            return text[..heading.Index] + list + (hasBlock
                ? text[listEnd..open] + block + text[end..]
                : block + (rest.Length == 0 ? "\n" : "\n\n" + rest)); // repair: the fresh block goes straight after the list
        }
        // No heading of ours yet: the list and block go before an existing block, else at the end; all text is kept.
        return hasBlock ? text[..open] + list + block + text[end..] : text.TrimEnd('\n') + "\n\n" + list + block + "\n";
    }

    /// <summary>End of the job-list lines that follow the heading line ending at <paramref name="from"/>, never past <paramref name="limit"/>.</summary>
    private static int JobListEnd(string text, int from, int limit)
    {
        var pos = from < limit && text[from] == '\n' ? from + 1 : from;
        while (pos < limit)
        {
            var newline = text.IndexOf('\n', pos);
            var lineEnd = newline < 0 ? text.Length : newline + 1;
            if (lineEnd > limit) break;
            var line = text[pos..(newline < 0 ? text.Length : newline)];
            if (line.Trim().Length > 0 && !line.StartsWith("- ", StringComparison.Ordinal)) break;
            pos = lineEnd;
        }
        return pos;
    }

    public static string StatusWord(string status) => status switch
    {
        WorkJobStates.Queued => "queued",
        WorkJobStates.Running => "working",
        WorkJobStates.NeedsYou => "needs you",
        WorkJobStates.Completed => "done",
        WorkJobStates.Failed => "failed",
        WorkJobStates.Stopped => "stopped",
        _ => "ready",
    };

    private static string RunsBlock(IReadOnlyList<WorkIndexRun> runs)
    {
        var block = new StringBuilder(RunsOpen).Append('\n');
        foreach (var run in runs.OrderByDescending(r => r.At).Take(MaxRuns))
        {
            var summary = Clean(run.Summary);
            if (summary.Length > MaxSummary) summary = summary[..(MaxSummary - 1)] + "…";
            block.Append(run.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" · ").Append(Outcome(run.Outcome))
                .Append(summary.Length > 0 ? " · " + summary : "").Append('\n');
        }
        return block.Append(RunsClose).ToString();
    }

    private static string Outcome(string status) => status switch
    {
        WorkJobStates.Completed or "done" => "done",
        WorkJobStates.Failed => "failed",
        WorkJobStates.Stopped => "stopped",
        _ => "working",
    };

    /// <summary>One line, and no comment opener, so model or user text can never forge or break a marker.</summary>
    private static string Clean(string value) => value.ReplaceLineEndings(" ").Replace('\t', ' ').Replace("<!--", "<! --", StringComparison.Ordinal).Trim();

    /// <summary>The first closing marker that has an opening marker before it, paired with the nearest such opener.</summary>
    private static bool FindBlock(string text, out int open, out int end)
    {
        for (var close = text.IndexOf(RunsClose, StringComparison.Ordinal); close >= 0; close = text.IndexOf(RunsClose, close + 1, StringComparison.Ordinal))
        {
            open = close == 0 ? -1 : text.LastIndexOf(RunsOpen, close - 1, StringComparison.Ordinal);
            if (open >= 0) { end = close + RunsClose.Length; return true; }
        }
        open = end = -1;
        return false;
    }
}
