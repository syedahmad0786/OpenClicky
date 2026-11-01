using System.Text.Json;
using System.Text;

namespace OpenClicky.Core;

public sealed record IntelligentUiParseResult(string PlainText, IntelligentUiDocument? Document);

public sealed record IntelligentUiDocument(
    string Title,
    string Summary,
    IReadOnlyList<IntelligentUiCard> Cards,
    IReadOnlyList<IntelligentUiNode> Nodes,
    IReadOnlyList<IntelligentUiEdge> Edges,
    IReadOnlyList<IntelligentUiAction> Actions);

public sealed record IntelligentUiCard(string Title, string Value, string Detail, string Tone);
public sealed record IntelligentUiNode(string Id, string Title, string Detail, string Status);
public sealed record IntelligentUiEdge(string From, string To, string Label);
public sealed record IntelligentUiAction(string Label, string Prompt);

/// <summary>
/// Reads OpenClicky's bounded rich-response payload from an AG-UI CUSTOM event embedded in an assistant reply.
/// The surrounding text remains the spoken and history-safe answer.
/// </summary>
public static class IntelligentUiParser
{
    public const string EventName = "openclicky.ui.document";
    public const int MaxReplyLength = 100_000;
    public const int MaxPayloadLength = 32_000;
    public const int MaxCards = 6;
    public const int MaxNodes = 8;
    public const int MaxEdges = 12;
    public const int MaxActions = 4;
    public const string SafeFallbackText = "I couldn't display that interactive answer. Ask me to try again.";

    private const string FenceStart = "```ag-ui";

    public static IntelligentUiParseResult Parse(string assistantReply)
    {
        ArgumentNullException.ThrowIfNull(assistantReply);
        var normalized = assistantReply.ReplaceLineEndings("\n");
        var (plainText, payloads, allComplete) = ExtractPayloads(normalized);
        var document = assistantReply.Length <= MaxReplyLength && allComplete && payloads.Count == 1
            && payloads[0].Length is > 0 and <= MaxPayloadLength
            ? TryReadDocument(payloads[0]) : null;
        if (plainText.Length == 0 && document is not null)
            plainText = string.IsNullOrWhiteSpace(document.Summary) ? document.Title : document.Summary;
        if (plainText.Length == 0 && payloads.Count > 0) plainText = SafeFallbackText;
        if (plainText.Length > MaxReplyLength) plainText = plainText[..MaxReplyLength].TrimEnd() + "…";
        return new IntelligentUiParseResult(plainText, document);
    }

    private static (string PlainText, List<string> Payloads, bool AllComplete) ExtractPayloads(string text)
    {
        var plain = new StringBuilder(Math.Min(text.Length, MaxReplyLength));
        var payloads = new List<string>();
        var position = 0;
        var allComplete = true;
        while (FindFenceStart(text, position) is { } fence)
        {
            AppendPlain(plain, text, position, fence.LineStart - position);
            var payloadStart = text.IndexOf('\n', fence.MarkerStart + FenceStart.Length);
            if (payloadStart < 0)
            {
                payloads.Add("");
                allComplete = false;
                position = text.Length;
                break;
            }
            payloadStart++;

            if (FindClosingFence(text, payloadStart) is not { } closing)
            {
                payloads.Add("");
                allComplete = false;
                position = text.Length;
                break;
            }

            var payloadLength = closing.LineStart - payloadStart;
            payloads.Add(payloadLength <= MaxPayloadLength ? text.Substring(payloadStart, payloadLength) : "");
            position = closing.AfterFence;
        }
        AppendPlain(plain, text, position, text.Length - position);
        return (plain.ToString().Trim(), payloads, allComplete);
    }

    private static void AppendPlain(StringBuilder destination, string source, int start, int count)
    {
        var remaining = MaxReplyLength + 1 - destination.Length;
        if (remaining > 0) destination.Append(source, start, Math.Min(count, remaining));
    }

    private static (int LineStart, int MarkerStart)? FindFenceStart(string text, int position)
    {
        while (position < text.Length)
        {
            var lineEnd = text.IndexOf('\n', position);
            if (lineEnd < 0) lineEnd = text.Length;
            var marker = position;
            while (marker < lineEnd && text[marker] is ' ' or '\t') marker++;
            if (lineEnd - marker >= FenceStart.Length
                && text.AsSpan(marker, FenceStart.Length).Equals(FenceStart, StringComparison.OrdinalIgnoreCase))
                return (position, marker);
            position = lineEnd + 1;
        }
        return null;
    }

    private static (int LineStart, int AfterFence)? FindClosingFence(string text, int position)
    {
        while (position < text.Length)
        {
            var lineEnd = text.IndexOf('\n', position);
            if (lineEnd < 0) lineEnd = text.Length;
            var marker = position;
            while (marker < lineEnd && text[marker] is ' ' or '\t') marker++;
            if (lineEnd - marker >= 3 && text.AsSpan(marker, 3).SequenceEqual("```"))
                return (position, lineEnd < text.Length ? lineEnd + 1 : lineEnd);
            position = lineEnd + 1;
        }
        return null;
    }

    private static IntelligentUiDocument? TryReadDocument(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "type", 16) != "CUSTOM"
                || Text(root, "name", 80) != EventName
                || !root.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var versionNumber)
                || versionNumber != 1)
                return null;

            var title = Text(value, "title", 120, required: true);
            var summary = Text(value, "summary", 1_000);
            if (title is null || summary is null) return null;

            var cards = ReadArray(value, "cards", MaxCards, ReadCard);
            var nodes = ReadArray(value, "nodes", MaxNodes, ReadNode);
            var edges = ReadArray(value, "edges", MaxEdges, ReadEdge);
            var actions = ReadArray(value, "actions", MaxActions, ReadAction);
            if (cards is null || nodes is null || edges is null || actions is null) return null;

            var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            if (ids.Count != nodes.Count
                || edges.Any(edge => !ids.Contains(edge.From) || !ids.Contains(edge.To) || edge.From == edge.To))
                return null;

            return new IntelligentUiDocument(title, summary, cards, nodes, edges, actions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IntelligentUiCard? ReadCard(JsonElement item)
    {
        var title = Text(item, "title", 80, required: true);
        var value = Text(item, "value", 80, required: true);
        var detail = Text(item, "detail", 240);
        var tone = item.TryGetProperty("tone", out _) ? Text(item, "tone", 16) : "neutral";
        return title is null || value is null || detail is null || tone is not ("neutral" or "good" or "warning")
            ? null : new(title, value, detail, tone);
    }

    private static IntelligentUiNode? ReadNode(JsonElement item)
    {
        var id = Text(item, "id", 40, required: true);
        var title = Text(item, "title", 80, required: true);
        var detail = Text(item, "detail", 320);
        var status = item.TryGetProperty("status", out _) ? Text(item, "status", 16) : "next";
        return id is null || title is null || detail is null || status is not ("done" or "current" or "next" or "blocked")
            ? null : new(id, title, detail, status);
    }

    private static IntelligentUiEdge? ReadEdge(JsonElement item)
    {
        var from = Text(item, "from", 40, required: true);
        var to = Text(item, "to", 40, required: true);
        var label = Text(item, "label", 60);
        return from is null || to is null || label is null ? null : new(from, to, label);
    }

    private static IntelligentUiAction? ReadAction(JsonElement item)
    {
        var label = Text(item, "label", 60, required: true);
        var prompt = Text(item, "prompt", 500, required: true);
        return label is null || prompt is null ? null : new(label, prompt);
    }

    private static List<T>? ReadArray<T>(
        JsonElement parent,
        string property,
        int maximum,
        Func<JsonElement, T?> read) where T : class
    {
        if (!parent.TryGetProperty(property, out var array)) return [];
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximum) return null;

        var values = new List<T>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || read(item) is not { } value) return null;
            values.Add(value);
        }
        return values;
    }

    private static string? Text(JsonElement parent, string property, int maximum, bool required = false)
    {
        if (!parent.TryGetProperty(property, out var value)) return required ? null : "";
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim() ?? "";
        return text.Length > maximum || required && text.Length == 0 ? null : text;
    }
}
