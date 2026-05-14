namespace WorkIQChat.Server.Services;

/// <summary>
/// Defines the contract for calling the Microsoft Work IQ API to retrieve
/// organizational context grounded in the signed-in user's Microsoft 365 data.
/// </summary>
/// <remarks>
/// Work IQ uses the Agent-to-Agent (A2A) protocol over JSON-RPC.
/// Requests execute in the context of the signed-in user and honor Microsoft 365
/// permissions and sensitivity labels automatically.
/// Documentation: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-overview
/// </remarks>
public interface IWorkIQService
{
    /// <summary>
    /// Sends a query to the Work IQ API and returns a response grounded in the
    /// user's Microsoft 365 organizational data (emails, meetings, files, chats).
    /// </summary>
    /// <param name="query">The natural-language query to send to Work IQ.</param>
    /// <param name="accessToken">
    /// A valid Microsoft Entra access token with the <c>WorkIQAgent.Ask</c> scope
    /// (audience: <c>api://workiq.svc.cloud.microsoft</c>).
    /// </param>
    /// <param name="contextId">
    /// Optional context ID returned by a previous call on the same conversation.
    /// Pass this to maintain multi-turn conversation state with Work IQ.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="WorkIQResult"/> containing the grounded response text and an
    /// updated context ID, or <see langword="null"/> if the call fails or Work IQ
    /// returns no relevant content.
    /// </returns>
    Task<WorkIQResult?> SendMessageAsync(
        string query,
        string accessToken,
        string? contextId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a successful response from the Work IQ API.
/// </summary>
/// <param name="Text">The grounded response text from Work IQ.</param>
/// <param name="ContextId">
/// The context ID to pass in the next call to maintain multi-turn conversation continuity.
/// </param>
public sealed record WorkIQResult(string Text, string? ContextId);
