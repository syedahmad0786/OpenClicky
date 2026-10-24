using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenClicky.Providers;

public sealed record OpenCodeHealth(bool Healthy, string? Version);

public sealed record OpenCodeSession(string Id, string? Title);

public sealed record OpenCodeSessionStatus(string Type, string? Message = null);

public sealed record OpenCodeMessageSummary(
    string Id,
    string Role,
    string Text,
    string? FinishReason,
    string? Error);

public sealed record OpenCodeMessageSnapshot(
    IReadOnlyList<OpenCodeMessageSummary> Messages,
    OpenCodeSessionStatus Status);

public sealed record OpenCodeProviderStatus(
    IReadOnlyList<string> Connected,
    IReadOnlyDictionary<string, string> DefaultModels,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Models);

public sealed record OpenCodeModelSelection(string ProviderId, string ModelId);

public sealed record OpenCodePermissionRequest(
    string Id,
    string SessionId,
    string Permission,
    IReadOnlyList<string> Patterns,
    string? ToolCallId);

/// <summary>A bounded client for an already-running OpenCode loopback server.</summary>
public sealed class OpenCodeTaskClient : IDisposable
{
    public const int MaxResponseBytes = 1_048_576;
    public const int MaxProviderResponseBytes = 8_388_608;
    public const int MaxMessages = 200;
    public const int MaxPermissions = 100;
    public const int MaxSummaryCharacters = 16_000;

    private const int MaxTitleCharacters = 500;
    private const int MaxPromptCharacters = 100_000;
    private const int MaxSessionIdCharacters = 200;
    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly bool _ownsHttp;

    public static OpenCodeTaskClient ForOpenWorkWorkspace(string endpoint, string workspaceId, string bearerToken) =>
        ForOpenWorkWorkspace(endpoint, workspaceId, bearerToken, null);

    internal static OpenCodeTaskClient ForOpenWorkWorkspace(
        string endpoint, string workspaceId, string bearerToken, HttpClient? httpClient)
    {
        var origin = ValidateEndpoint(endpoint);
        ValidateText(workspaceId, MaxSessionIdCharacters, nameof(workspaceId));
        ValidateText(bearerToken, 4_096, nameof(bearerToken));
        if (workspaceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("The OpenWork workspace ID is invalid.", nameof(workspaceId));
        return new OpenCodeTaskClient(
            new Uri(origin, $"workspace/{Uri.EscapeDataString(workspaceId)}/opencode/"),
            new AuthenticationHeaderValue("Bearer", bearerToken),
            httpClient);
    }

    public OpenCodeTaskClient(
        string endpoint,
        string? username = null,
        string? password = null)
        : this(endpoint, username, password, null)
    {
    }

    internal OpenCodeTaskClient(
        string endpoint,
        string? username,
        string? password,
        HttpClient? httpClient)
    {
        _endpoint = ValidateEndpoint(endpoint);
        if (password is null && username is not null)
        {
            throw new ArgumentException("A password is required when a username is provided.", nameof(password));
        }

        if (password is not null)
        {
            username ??= "opencode";
            if (username.Length is 0 or > 256 || username.Contains(':'))
            {
                throw new ArgumentException("The Basic authentication username is invalid.", nameof(username));
            }

            if (password.Length > 4_096)
            {
                throw new ArgumentOutOfRangeException(nameof(password), "The Basic authentication password is too long.");
            }

            _authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        }

        if (httpClient is null)
        {
            _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    private OpenCodeTaskClient(Uri endpoint, AuthenticationHeaderValue authorization, HttpClient? httpClient)
    {
        _endpoint = endpoint;
        _authorization = authorization;
        if (httpClient is null)
        {
            _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _ownsHttp = true;
        }
        else _http = httpClient;
    }

    public async Task<OpenCodeHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var json = await SendJsonAsync(HttpMethod.Get, "global/health", null, cancellationToken)
            .ConfigureAwait(false);
        var root = RequireRootObject(json);
        if (!root.TryGetProperty("healthy", out var healthy) || healthy.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("OpenCode health response did not contain a boolean healthy value.");
        }

        var version = OptionalString(root, "version");
        return new OpenCodeHealth(healthy.GetBoolean(), version);
    }

    public async Task<OpenCodeSession> CreateSessionAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        ValidateText(title, MaxTitleCharacters, nameof(title));
        using var json = await SendJsonAsync(HttpMethod.Post, "session", new { title }, cancellationToken)
            .ConfigureAwait(false);
        var root = RequireRootObject(json);
        var id = RequiredString(root, "id", MaxSessionIdCharacters);
        return new OpenCodeSession(id, OptionalString(root, "title"));
    }

    public async Task<OpenCodeProviderStatus> GetProviderStatusAsync(
        CancellationToken cancellationToken = default)
    {
        using var json = await SendJsonAsync(
                HttpMethod.Get,
                "provider",
                null,
                cancellationToken,
                maxResponseBytes: MaxProviderResponseBytes)
            .ConfigureAwait(false);
        var root = RequireRootObject(json);
        if (!root.TryGetProperty("connected", out var connected) || connected.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("OpenCode provider response did not contain a connected provider list.");
        }

        if (connected.GetArrayLength() > 100)
        {
            throw new InvalidDataException("OpenCode returned an invalid connected provider list.");
        }

        var ids = new List<string>(connected.GetArrayLength());
        foreach (var item in connected.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new InvalidDataException("OpenCode returned an invalid connected provider list.");
            }

            ids.Add(item.GetString()!);
        }

        if (!root.TryGetProperty("default", out var defaultsElement)
            || defaultsElement.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("all", out var allElement)
            || allElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("OpenCode provider response did not contain model status.");
        }

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var models = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (defaultsElement.TryGetProperty(id, out var defaultElement)
                && defaultElement.ValueKind == JsonValueKind.String
                && defaultElement.GetString() is { Length: > 0 and <= 500 } defaultModel)
            {
                defaults[id] = defaultModel;
            }

            var provider = allElement.EnumerateArray().FirstOrDefault(item =>
                item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var providerId)
                && providerId.ValueKind == JsonValueKind.String
                && providerId.GetString() == id);
            if (provider.ValueKind != JsonValueKind.Object
                || !provider.TryGetProperty("models", out var providerModels)
                || providerModels.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"OpenCode did not return usable models for connected provider '{id}'.");
            }

            var modelIds = providerModels.EnumerateObject().Select(property => property.Name).ToArray();
            if (modelIds.Length > 2_000 || modelIds.Any(modelId => modelId.Length is 0 or > 500))
            {
                throw new InvalidDataException($"OpenCode returned an invalid model list for connected provider '{id}'.");
            }

            models[id] = modelIds;
        }

        return new OpenCodeProviderStatus(ids, defaults, models);
    }

    public async Task<string?> GetConfiguredModelAsync(CancellationToken cancellationToken = default)
    {
        using var json = await SendJsonAsync(HttpMethod.Get, "config", null, cancellationToken)
            .ConfigureAwait(false);
        var root = RequireRootObject(json);
        return OptionalString(root, "model", 700);
    }

    public async Task SendPromptAsync(
        string sessionId,
        string prompt,
        CancellationToken cancellationToken = default) =>
        await SendPromptAsync(sessionId, prompt, null, cancellationToken).ConfigureAwait(false);

    public async Task SendPromptAsync(
        string sessionId,
        string prompt,
        OpenCodeModelSelection? model,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ValidateText(prompt, MaxPromptCharacters, nameof(prompt));
        if (model is not null)
        {
            ValidateText(model.ProviderId, 200, nameof(model.ProviderId));
            ValidateText(model.ModelId, 500, nameof(model.ModelId));
        }

        var body = new Dictionary<string, object>
        {
            ["parts"] = new[] { new { type = "text", text = prompt } },
        };
        if (model is not null)
        {
            body["model"] = new { providerID = model.ProviderId, modelID = model.ModelId };
        }

        using var ignored = await SendJsonAsync(
                HttpMethod.Post,
                $"session/{Uri.EscapeDataString(sessionId)}/prompt_async",
                body,
                cancellationToken,
                allowEmpty: true)
            .ConfigureAwait(false);
    }

    public async Task<OpenCodeMessageSnapshot> GetMessagesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        using var messagesJson = await SendJsonAsync(
                HttpMethod.Get,
                $"session/{Uri.EscapeDataString(sessionId)}/message",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var messages = ParseMessages(messagesJson);
        var status = await GetSessionStatusAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return new OpenCodeMessageSnapshot(messages, status);
    }

    public async Task<OpenCodeSessionStatus> GetSessionStatusAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        using var json = await SendJsonAsync(HttpMethod.Get, "session/status", null, cancellationToken)
            .ConfigureAwait(false);
        var root = RequireRootObject(json);
        if (!root.TryGetProperty(sessionId, out var status))
        {
            return new OpenCodeSessionStatus("idle");
        }

        if (status.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("OpenCode session status was not an object.");
        }

        var type = RequiredString(status, "type", 32);
        if (type is not ("idle" or "busy" or "retry"))
        {
            throw new InvalidDataException($"OpenCode returned unsupported session status '{type}'.");
        }

        return new OpenCodeSessionStatus(type, OptionalString(status, "message", MaxSummaryCharacters));
    }

    public async Task AbortAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        using var ignored = await SendJsonAsync(
                HttpMethod.Post,
                $"session/{Uri.EscapeDataString(sessionId)}/abort",
                null,
                cancellationToken,
                allowEmpty: true)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpenCodePermissionRequest>> GetPendingPermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var json = await SendJsonAsync(HttpMethod.Get, "permission", null, cancellationToken)
            .ConfigureAwait(false);
        if (json.RootElement.ValueKind != JsonValueKind.Array
            || json.RootElement.GetArrayLength() > MaxPermissions)
        {
            throw new InvalidDataException("OpenCode returned an invalid permission list.");
        }

        var requests = new List<OpenCodePermissionRequest>(json.RootElement.GetArrayLength());
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("patterns", out var patternsElement)
                || patternsElement.ValueKind != JsonValueKind.Array
                || patternsElement.GetArrayLength() > 100)
            {
                throw new InvalidDataException("OpenCode returned an invalid permission request.");
            }

            var patterns = patternsElement.EnumerateArray()
                .Select(pattern => pattern.ValueKind == JsonValueKind.String ? pattern.GetString() : null)
                .ToArray();
            if (patterns.Any(pattern => pattern is null || pattern.Length > MaxSummaryCharacters))
            {
                throw new InvalidDataException("OpenCode returned an invalid permission pattern.");
            }

            string? toolCallId = null;
            if (item.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.Object)
            {
                toolCallId = OptionalString(tool, "callID", MaxSessionIdCharacters);
            }

            requests.Add(new OpenCodePermissionRequest(
                RequiredString(item, "id", MaxSessionIdCharacters),
                RequiredString(item, "sessionID", MaxSessionIdCharacters),
                RequiredString(item, "permission", 200),
                patterns!,
                toolCallId));
        }

        return requests;
    }

    public async Task ReplyToPermissionAsync(
        string permissionId,
        bool allowOnce,
        CancellationToken cancellationToken = default)
    {
        ValidateText(permissionId, MaxSessionIdCharacters, nameof(permissionId));
        using var ignored = await SendJsonAsync(
                HttpMethod.Post,
                $"permission/{Uri.EscapeDataString(permissionId)}/reply",
                new { reply = allowOnce ? "once" : "reject" },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    public static Uri ValidateEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!endpoint.Equals(endpoint.Trim(), StringComparison.Ordinal)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            throw new ArgumentException("OpenCode endpoint must be an HTTP loopback origin without credentials, path, query, or fragment.", nameof(endpoint));
        }

        var host = uri.Host.Trim('[', ']');
        if (host is not "127.0.0.1" and not "::1"
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("OpenCode endpoint must use 127.0.0.1, [::1], or localhost.", nameof(endpoint));
        }

        return uri;
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowEmpty = false,
        int maxResponseBytes = MaxResponseBytes)
    {
        var requestUri = new Uri(_endpoint, relativePath);
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = _authorization;
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        }

        using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest
            || response.RequestMessage?.RequestUri is { } finalUri && finalUri != requestUri)
        {
            throw new HttpRequestException("OpenCode endpoint redirects are not allowed.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenCode request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        var bytes = await ReadBoundedAsync(response.Content, maxResponseBytes, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0 && allowEmpty)
        {
            return JsonDocument.Parse("null");
        }

        if (bytes.Length == 0)
        {
            throw new InvalidDataException("OpenCode returned an empty response.");
        }

        try
        {
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("OpenCode returned invalid JSON.", ex);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maxResponseBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxResponseBytes)
        {
            throw new InvalidDataException($"OpenCode response exceeds the {maxResponseBytes}-byte limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8_192];
        while (buffer.Length <= maxResponseBytes)
        {
            var read = await stream.ReadAsync(
                    chunk.AsMemory(0, Math.Min(chunk.Length, maxResponseBytes + 1 - (int)buffer.Length)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            buffer.Write(chunk, 0, read);
        }

        throw new InvalidDataException($"OpenCode response exceeds the {maxResponseBytes}-byte limit.");
    }

    private static IReadOnlyList<OpenCodeMessageSummary> ParseMessages(JsonDocument json)
    {
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("OpenCode messages response was not an array.");
        }

        if (json.RootElement.GetArrayLength() > MaxMessages)
        {
            throw new InvalidDataException($"OpenCode returned more than {MaxMessages} messages.");
        }

        var messages = new List<OpenCodeMessageSummary>(json.RootElement.GetArrayLength());
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("info", out var info)
                || info.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("OpenCode returned an invalid message.");
            }

            var text = new StringBuilder();
            if (item.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object
                        || OptionalString(part, "type") != "text"
                        || OptionalString(part, "text", MaxResponseBytes) is not { } partText)
                    {
                        continue;
                    }

                    if (text.Length > 0 && text.Length < MaxSummaryCharacters)
                    {
                        text.AppendLine();
                    }

                    var remaining = MaxSummaryCharacters - text.Length;
                    if (remaining > 0)
                    {
                        text.Append(partText.AsSpan(0, Math.Min(remaining, partText.Length)));
                    }
                }
            }

            string? error = null;
            if (info.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.String)
                {
                    error = OptionalString(info, "error", MaxSummaryCharacters);
                }
                else if (errorElement.ValueKind == JsonValueKind.Object)
                {
                    var name = OptionalString(errorElement, "name", 200);
                    var message = errorElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                        ? OptionalString(data, "message", MaxSummaryCharacters)
                        : null;
                    error = name is not null && message is not null ? $"{name}: {message}" : message ?? name;
                }
            }

            messages.Add(new OpenCodeMessageSummary(
                RequiredString(info, "id", MaxSessionIdCharacters),
                RequiredString(info, "role", 32),
                text.ToString(),
                OptionalString(info, "finish", 200),
                error));
        }

        return messages;
    }

    private static JsonElement RequireRootObject(JsonDocument json)
    {
        if (json.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("OpenCode response was not a JSON object.");
        }

        return json.RootElement;
    }

    private static string RequiredString(JsonElement element, string property, int maxLength)
    {
        var value = OptionalString(element, property, maxLength);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"OpenCode response did not contain a valid {property}.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement element, string property, int maxLength = 500)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"OpenCode response {property} was not text.");
        }

        var text = value.GetString()!;
        if (text.Length > maxLength)
        {
            throw new InvalidDataException($"OpenCode response {property} exceeded the {maxLength}-character limit.");
        }

        return text;
    }

    private static void ValidateSessionId(string sessionId)
    {
        ValidateText(sessionId, MaxSessionIdCharacters, nameof(sessionId));
    }

    private static void ValidateText(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value is limited to {maxLength} characters.");
        }
    }
}
