using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenClicky.Providers;

/// <summary>Adapts the OpenAI Responses shape emitted by OpenCode to the SIWC preview contract.</summary>
public static class ChatGptInferenceProtocol
{
    public const int MaxRequestBytes = 4 * 1024 * 1024;
    public const int MaxModelsResponseBytes = 1024 * 1024;
    public const string ToolNamespace = "opencode";
    public const string AllowedToolPrefix = "openclicky_";

    private static readonly HashSet<string> UnsupportedFields = new(StringComparer.Ordinal)
    {
        "background", "conversation", "max_output_tokens", "max_tool_calls", "metadata", "moderation",
        "multi_agent", "previous_response_id", "prompt", "prompt_cache_retention", "safety_identifier",
        "temperature", "top_logprobs", "top_p", "truncation", "user",
    };

    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "model", "input", "instructions", "tools", "tool_choice", "parallel_tool_calls", "reasoning",
        "text", "include", "service_tier", "prompt_cache_key", "prompt_cache_options", "store", "stream",
    };

    public static byte[] AdaptResponsesRequest(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length == 0 || utf8Json.Length > MaxRequestBytes)
            throw new InvalidDataException($"Responses request must be between 1 and {MaxRequestBytes} bytes.");

        JsonNode? parsed;
        try { parsed = JsonNode.Parse(utf8Json, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException ex) { throw new InvalidDataException("Responses request is not valid JSON.", ex); }
        if (parsed is not JsonObject request) throw new InvalidDataException("Responses request must be a JSON object.");

        foreach (var property in request.ToArray())
        {
            if (UnsupportedFields.Contains(property.Key)) request.Remove(property.Key);
            else if (!AllowedFields.Contains(property.Key))
                throw new InvalidDataException($"Responses field '{property.Key}' is not supported by this bridge.");
        }

        if (request["model"] is not JsonValue model || !model.TryGetValue<string>(out var modelName) || string.IsNullOrWhiteSpace(modelName))
            throw new InvalidDataException("Responses request requires a model.");
        if (request["input"] is not JsonArray input) throw new InvalidDataException("Responses input must contain the full history as an array.");

        var allowedCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in input)
        {
            if (node is not JsonObject item) continue;
            if (String(item["role"]) == "system") item["role"] = "developer";
            if (String(item["type"]) is "function_call" or "custom_tool_call")
            {
                RequireAllowedToolName(item);
                var callId = String(item["call_id"]);
                if (string.IsNullOrWhiteSpace(callId) || !allowedCallIds.Add(callId))
                    throw new InvalidDataException("Responses tool calls require unique call IDs.");
                item["namespace"] = ToolNamespace;
            }
        }
        foreach (var node in input)
        {
            if (node is not JsonObject item || String(item["type"]) is not ("function_call_output" or "custom_tool_call_output")) continue;
            var callId = String(item["call_id"]);
            if (callId is null || !allowedCallIds.Contains(callId))
                throw new InvalidDataException("Responses tool output does not reference an allowed OpenClicky call in this history.");
        }

        if (request["tools"] is JsonArray tools)
        {
            var grouped = new JsonArray();
            foreach (var node in tools)
            {
                if (node is not JsonObject tool) throw new InvalidDataException("Each Responses tool must be an object.");
                var type = String(tool["type"]);
                if (type == "namespace")
                {
                    if (tool["tools"] is not JsonArray nested)
                        throw new InvalidDataException("Responses namespace tools require a tools array.");
                    foreach (var nestedTool in nested)
                    {
                        if (nestedTool is not JsonObject nestedObject) continue;
                        if (String(nestedObject["type"]) is not ("function" or "custom")) continue;
                        if (!IsAllowedToolName(nestedObject)) continue;
                        grouped.Add(nestedObject.DeepClone());
                    }
                    continue;
                }
                if (type is not ("function" or "custom")) continue;
                if (!IsAllowedToolName(tool)) continue;
                grouped.Add(tool.DeepClone());
            }
            if (tools.Count > 0 && grouped.Count == 0)
                throw new InvalidDataException("Responses request declared tools but none belong to the OpenClicky desktop broker.");
            request["tools"] = grouped.Count == 0 ? new JsonArray() : new JsonArray(new JsonObject
            {
                ["type"] = "namespace",
                ["name"] = ToolNamespace,
                ["description"] = "Tools provided by the local OpenCode runtime.",
                ["tools"] = grouped,
            });
            if (request["tool_choice"] is JsonObject choice && String(choice["type"]) is "function" or "custom")
            {
                RequireAllowedToolName(choice);
                choice["namespace"] = ToolNamespace;
            }
        }

        request["store"] = false;
        request["stream"] = true;
        return JsonSerializer.SerializeToUtf8Bytes(request);
    }

    public static byte[] AdaptModelsResponse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length == 0 || utf8Json.Length > MaxModelsResponseBytes)
            throw new InvalidDataException("ChatGPT model catalog exceeded its size limit.");
        using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("ChatGPT returned an invalid model catalog.");

        var data = new JsonArray();
        foreach (var model in models.EnumerateArray())
        {
            if (!model.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String
                || !model.TryGetProperty("visibility", out var visibility) || visibility.GetString() != "list") continue;
            var id = slug.GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 200) continue;
            data.Add(new JsonObject { ["id"] = id, ["object"] = "model", ["created"] = 0, ["owned_by"] = "openai" });
        }
        return JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["object"] = "list", ["data"] = data });
    }

    private static string? String(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void RequireAllowedToolName(JsonObject tool)
    {
        if (!IsAllowedToolName(tool))
            throw new InvalidDataException("Only OpenClicky desktop tools are allowed by this bridge.");
    }

    private static bool IsAllowedToolName(JsonObject tool)
    {
        var name = String(tool["name"]);
        return name is not null && name.StartsWith(AllowedToolPrefix, StringComparison.Ordinal)
            && name.Length <= 128 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }
}
