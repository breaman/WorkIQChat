using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkIQChat.Server.Services;

/// <summary>
/// Calls the Microsoft Work IQ API using the Agent-to-Agent (A2A) v1.0 protocol
/// over JSON-RPC to retrieve organizational context grounded in the user's
/// Microsoft 365 data (emails, meetings, files, Teams chats, and people).
/// </summary>
/// <remarks>
/// <para>
/// Work IQ endpoint: <c>https://workiq.svc.cloud.microsoft/a2a/</c><br/>
/// Required auth scope: <c>api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask</c><br/>
/// Protocol reference: https://a2a-protocol.org/latest/specification/<br/>
/// Work IQ docs: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-overview
/// </para>
/// <para>
/// Requests execute in the context of the signed-in user. Microsoft 365 permissions,
/// sensitivity labels, and compliance policies are enforced automatically.
/// Application-only (app-only) authentication is not supported.
/// </para>
/// </remarks>
public sealed class WorkIQService(IHttpClientFactory httpClientFactory, ILogger<WorkIQService> logger) : IWorkIQService
{
    /// <summary>The named <see cref="HttpClient"/> key registered in DI for Work IQ API calls.</summary>
    internal const string HttpClientName = "WorkIQ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public async Task<WorkIQResult?> SendMessageAsync(
        string query,
        string accessToken,
        string? contextId = null,
        CancellationToken cancellationToken = default)
    {
        // Create a per-request HttpClient so we can set the Authorization header
        // without mutating a shared client instance.
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var requestBody = new A2ARequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Params = new A2AMessageParams
            {
                Message = new A2AMessage
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    ContextId = contextId,
                    Parts = [new A2APart { Text = query }],
                    Metadata = new A2AMetadata
                    {
                        Location = new A2ALocation
                        {
                            // Work IQ requires location metadata to ground time-sensitive
                            // queries (e.g., "today" or "this week") in the user's local time.
                            TimeZoneOffset = (int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes,
                            TimeZone = TimeZoneInfo.Local.Id,
                        },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;

        try
        {
            response = await client.PostAsync(string.Empty, content, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Work IQ API call failed for query: {Query}", query);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Work IQ API returned {StatusCode} for query: {Query}. Body: {Body}",
                response.StatusCode,
                query,
                errorBody);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        A2AResponse? a2aResponse;

        try
        {
            a2aResponse = JsonSerializer.Deserialize<A2AResponse>(responseJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize Work IQ API response");
            return null;
        }

        if (a2aResponse?.Error is { } error)
        {
            logger.LogWarning("Work IQ API returned JSON-RPC error {Code}: {Message}", error.Code, error.Message);
            return null;
        }

        var task = a2aResponse?.Result?.Task;

        if (task is null)
        {
            logger.LogDebug("Work IQ API returned no task result for query: {Query}", query);
            return null;
        }

        // Extract all text content from the task artifacts. Work IQ delivers the answer
        // in Artifact.Parts; StatusUpdate.Message.Parts contain chain-of-thought, not the answer.
        var text = string.Join("\n",
            task.Artifacts
                .SelectMany(a => a.Parts)
                .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                .Select(p => p.Text!));

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogDebug("Work IQ API returned no text content for query: {Query}", query);
            return null;
        }

        return new WorkIQResult(text, task.ContextId);
    }

    // ── JSON-RPC A2A v1.0 request types ─────────────────────────────────────

    private sealed class A2ARequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("method")]
        public string Method => "SendMessage";

        [JsonPropertyName("params")]
        public required A2AMessageParams Params { get; init; }
    }

    private sealed class A2AMessageParams
    {
        [JsonPropertyName("message")]
        public required A2AMessage Message { get; init; }
    }

    private sealed class A2AMessage
    {
        [JsonPropertyName("role")]
        public string Role => "ROLE_USER";

        [JsonPropertyName("messageId")]
        public required string MessageId { get; init; }

        [JsonPropertyName("contextId")]
        public string? ContextId { get; init; }

        [JsonPropertyName("parts")]
        public required List<A2APart> Parts { get; init; }

        [JsonPropertyName("metadata")]
        public required A2AMetadata Metadata { get; init; }
    }

    private sealed class A2APart
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed class A2AMetadata
    {
        [JsonPropertyName("Location")]
        public required A2ALocation Location { get; init; }
    }

    private sealed class A2ALocation
    {
        [JsonPropertyName("timeZoneOffset")]
        public required int TimeZoneOffset { get; init; }

        [JsonPropertyName("timeZone")]
        public required string TimeZone { get; init; }
    }

    // ── JSON-RPC A2A v1.0 response types ────────────────────────────────────

    private sealed class A2AResponse
    {
        [JsonPropertyName("result")]
        public A2AResult? Result { get; init; }

        [JsonPropertyName("error")]
        public A2AErrorResult? Error { get; init; }
    }

    private sealed class A2AResult
    {
        [JsonPropertyName("task")]
        public A2ATask? Task { get; init; }
    }

    private sealed class A2ATask
    {
        [JsonPropertyName("contextId")]
        public string? ContextId { get; init; }

        [JsonPropertyName("artifacts")]
        public List<A2AArtifact> Artifacts { get; init; } = [];
    }

    private sealed class A2AArtifact
    {
        [JsonPropertyName("parts")]
        public List<A2APart> Parts { get; init; } = [];
    }

    private sealed class A2AErrorResult
    {
        [JsonPropertyName("code")]
        public int Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}
