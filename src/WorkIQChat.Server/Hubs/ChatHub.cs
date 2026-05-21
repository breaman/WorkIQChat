using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using WorkIQChat.Shared;

namespace WorkIQChat.Server.Hubs;

/// <summary>
/// SignalR hub that streams AI chat responses back to the calling client token by token.
/// Receives a user message, connects to the Microsoft WorkIQ MCP server to retrieve
/// available Microsoft 365 tools (emails, meetings, documents, Teams messages), forwards
/// the message with those tools to the registered <see cref="IChatClient"/>, and yields
/// each response chunk as it arrives.
/// </summary>
public class ChatHub(IChatClient chatClient, ILoggerFactory loggerFactory) : Hub
{
    /// <summary>
    /// Invoked by the client with the full conversation history. Launches the WorkIQ MCP
    /// server as a child process via <c>npx -y @microsoft/workiq mcp</c>, lists its available
    /// tools, then calls the <see cref="IChatClient"/> with the full history and those tools
    /// enabled, streaming each response token back to the caller. Falls back to tool-free
    /// chat if the WorkIQ process cannot be started.
    /// </summary>
    /// <param name="history">
    /// All messages in the conversation so far, ordered oldest-first. The last entry is the
    /// most recent user message.
    /// </param>
    /// <param name="cancellationToken">Cancellation token forwarded to the AI client stream.</param>
    /// <returns>An async stream of response text chunks from the AI model.</returns>
    public async IAsyncEnumerable<string> StreamMessage(
        List<ConversationMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Map the shared DTO history to the Microsoft.Extensions.AI message list,
        // preserving the full conversational context for the model.
        var chatMessages = history
            .Select(m => new ChatMessage(m.IsUser ? ChatRole.User : ChatRole.Assistant, m.Text))
            .ToList();

        // Attempt to start the WorkIQ MCP server and enumerate its tools so the
        // AI model can call them during the conversation. If npx or the package
        // is unavailable, the hub falls back to responding without tools.
        McpClient? mcpClient = null;
        ChatOptions? chatOptions = null;
        List<string> workiqArgs = ["-y", "@microsoft/workiq@0.4.1", "mcp"];
        // List<string> workiqArgs = ["-y", "@microsoft/workiq@latest", "mcp"]; // latest doesn't work on a mac at the moment

        try
        {
            var transport = new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Command = "npx",
                    Arguments = workiqArgs,
                    Name = "workiq"
                },
                loggerFactory);

            mcpClient = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: cancellationToken);
            var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
            chatOptions = new ChatOptions { Tools = [.. tools] };
        }
        catch (Exception)
        {
            // WorkIQ process unavailable — dispose any partial client and
            // continue without tools.
            if (mcpClient is not null)
            {
                await mcpClient.DisposeAsync();
                mcpClient = null;
            }
        }

        try
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(chatMessages, chatOptions, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return update.Text;
                }
            }
        }
        finally
        {
            // Keep the MCP process alive through the full streamed response so
            // tool-call round-trips can complete, then dispose (kills the process).
            if (mcpClient is not null)
            {
                await mcpClient.DisposeAsync();
            }
        }
    }
}