using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;

using WorkIQChat.Server.Services;

namespace WorkIQChat.Server.Hubs;

/// <summary>
/// SignalR hub that streams AI chat responses back to the calling client token by token.
/// Receives a user message, optionally enriches it with organizational context from the
/// Microsoft Work IQ API, then forwards everything to the registered <see cref="IChatClient"/>
/// and yields each response chunk as it arrives.
/// </summary>
/// <remarks>
/// Work IQ context is retrieved only when the signed-in user has authenticated via Microsoft
/// and holds a valid access token with the <c>WorkIQAgent.Ask</c> scope. If the token is absent
/// or the Work IQ call fails, the hub falls back to the base Azure OpenAI response.
/// Multi-turn Work IQ context is maintained per connection via <see cref="HubCallerContext.Items"/>.
/// </remarks>
public class ChatHub(IChatClient chatClient, IWorkIQService workIQService) : Hub
{
    /// <summary>
    /// Key used in <see cref="HubCallerContext.Items"/> to persist the Work IQ context ID
    /// across hub method invocations on the same SignalR connection.
    /// </summary>
    private const string WorkIQContextIdKey = "WorkIQContextId";

    /// <summary>
    /// Invoked by the client with a user message. Optionally enriches the request with
    /// Work IQ organizational context, then calls the <see cref="IChatClient"/> and streams
    /// each response token back to the caller via server-to-client streaming.
    /// </summary>
    /// <param name="message">The user's chat message text.</param>
    /// <param name="cancellationToken">Cancellation token forwarded to the AI client stream.</param>
    /// <returns>An async stream of response text chunks from the AI model.</returns>
    public async IAsyncEnumerable<string> StreamMessage(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var workIQContext = await TryGetWorkIQContextAsync(message, cancellationToken);
        var chatMessages = BuildChatMessages(message, workIQContext);

        await foreach (var update in chatClient.GetStreamingResponseAsync(chatMessages, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve relevant organizational context from Work IQ for the given message.
    /// Uses the Microsoft access token stored in the current user's authentication session.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> if:
    /// <list type="bullet">
    ///   <item>The user is not authenticated via Microsoft (no external login token present).</item>
    ///   <item>The Work IQ API call fails for any reason.</item>
    ///   <item>Work IQ returns no relevant content for the query.</item>
    /// </list>
    /// On success, the Work IQ context ID is stored in <see cref="HubCallerContext.Items"/>
    /// so subsequent messages in the same connection maintain conversation continuity.
    /// </remarks>
    /// <param name="message">The user's message to use as the Work IQ query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Work IQ response text to use as grounding context, or <see langword="null"/>.</returns>
    private async Task<string?> TryGetWorkIQContextAsync(string message, CancellationToken cancellationToken)
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext is null)
        {
            return null;
        }

        // Retrieve the access token saved during Microsoft external login (SaveTokens = true).
        // Returns null if the user authenticated via a local account or a different provider.
        var accessToken = await httpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        // Restore the Work IQ conversation context from the previous message on this connection.
        Context.Items.TryGetValue(WorkIQContextIdKey, out var storedContextId);

        var result = await workIQService.SendMessageAsync(
            message,
            accessToken,
            contextId: storedContextId as string,
            cancellationToken);

        if (result is null)
        {
            return null;
        }

        // Persist the updated context ID so the next message continues the same Work IQ conversation.
        Context.Items[WorkIQContextIdKey] = result.ContextId;

        return result.Text;
    }

    /// <summary>
    /// Constructs the list of <see cref="ChatMessage"/> objects to send to the AI model,
    /// prepending a system message with Work IQ context when available.
    /// </summary>
    /// <param name="userMessage">The user's chat message.</param>
    /// <param name="workIQContext">
    /// Optional Work IQ organizational context to ground the AI response.
    /// When present, it is injected as a system message before the user message.
    /// </param>
    /// <returns>Ordered list of messages ready for the AI model.</returns>
    private static List<ChatMessage> BuildChatMessages(string userMessage, string? workIQContext)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(workIQContext))
        {
            // Grounding the AI with Work IQ context allows it to provide responses
            // that reference the user's actual emails, meetings, files, and people data.
            messages.Add(new ChatMessage(
                ChatRole.System,
                $"The following is relevant context retrieved from the user's Microsoft 365 " +
                $"work data via the Work IQ API. Use this context to provide a more informed " +
                $"and grounded response:\n\n{workIQContext}"));
        }

        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        return messages;
    }
}