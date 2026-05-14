using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;

namespace WorkIQChat.Server.Hubs;

/// <summary>
/// SignalR hub that streams AI chat responses back to the calling client token by token.
/// Receives a user message, forwards it to the registered <see cref="IChatClient"/>,
/// and yields each response chunk as it arrives.
/// </summary>
public class ChatHub(IChatClient chatClient) : Hub
{
    /// <summary>
    /// Invoked by the client with a user message. Calls the <see cref="IChatClient"/>
    /// and streams each response token back to the caller via server-to-client streaming.
    /// </summary>
    /// <param name="message">The user's chat message text.</param>
    /// <param name="cancellationToken">Cancellation token forwarded to the AI client stream.</param>
    /// <returns>An async stream of response text chunks from the AI model.</returns>
    public async IAsyncEnumerable<string> StreamMessage(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chatMessages = new List<ChatMessage> { new(ChatRole.User, message) };

        await foreach (var update in chatClient.GetStreamingResponseAsync(chatMessages, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}