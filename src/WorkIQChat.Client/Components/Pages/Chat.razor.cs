using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using WorkIQChat.Shared;

namespace WorkIQChat.Client.Components.Pages;

/// <summary>
/// Code-behind for the Chat page. Manages a SignalR <see cref="HubConnection"/> to the
/// server's <c>ChatHub</c> and streams AI response tokens back to the UI in real time.
/// </summary>
public partial class Chat : ComponentBase, IAsyncDisposable
{
    /// <summary>Shared Markdig pipeline with common extensions for rendering AI responses.</summary>
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    private string _inputMessage = string.Empty;
    private readonly List<ChatMessage> _messages = [];

    // Accumulates tokens from the current streaming response shown live in the UI.
    private string _streamingResponse = string.Empty;
    private bool _isStreaming;

    private HubConnection? _hubConnection;

    /// <summary>
    /// Initialises and starts the SignalR connection to the ChatHub on the server.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri(ChatHubConstants.HubUrl))
            .WithAutomaticReconnect()
            .Build();

        await _hubConnection.StartAsync();
    }

    /// <summary>
    /// Sends the current input to the hub and streams the AI response token by token,
    /// updating the UI after each chunk via <see cref="ComponentBase.StateHasChanged"/>.
    /// </summary>
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_inputMessage) || _isStreaming || _hubConnection is null)
        {
            return;
        }

        var userText = _inputMessage.Trim();
        _inputMessage = string.Empty;
        _messages.Add(new ChatMessage(userText, IsUser: true, DateTimeOffset.UtcNow));

        // Build the full conversation history to send with this request so the AI model
        // has context for the entire session and can answer follow-up questions.
        var history = _messages
            .Select(m => new ConversationMessage(m.Text, m.IsUser))
            .ToList();

        _isStreaming = true;
        _streamingResponse = string.Empty;

        try
        {
            await foreach (var token in _hubConnection.StreamAsync<string>(ChatHubConstants.StreamMessageMethod, history))
            {
                _streamingResponse += token;
                StateHasChanged();
            }

            // Commit the completed response as a permanent message entry.
            _messages.Add(new ChatMessage(_streamingResponse, IsUser: false, DateTimeOffset.UtcNow));
        }
        finally
        {
            _streamingResponse = string.Empty;
            _isStreaming = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Sends the message when the user presses the Enter key.
    /// </summary>
    /// <param name="args">Keyboard event arguments from the input element.</param>
    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await SendMessage();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }

    /// <summary>
    /// Converts a markdown string to a Blazor <see cref="MarkupString"/> for safe HTML rendering.
    /// </summary>
    /// <param name="markdown">The markdown text to convert.</param>
    /// <returns>An HTML <see cref="MarkupString"/> ready for use with <c>@((MarkupString)…)</c>.</returns>
    private static MarkupString ToMarkup(string markdown) =>
        new(Markdown.ToHtml(markdown, MarkdownPipeline));

    /// <summary>
    /// Represents a single message in the conversation history.
    /// </summary>
    /// <param name="Text">The message text.</param>
    /// <param name="IsUser"><c>true</c> if the message was sent by the user; <c>false</c> for AI responses.</param>
    /// <param name="Timestamp">The UTC time the message was finalised.</param>
    private record ChatMessage(string Text, bool IsUser, DateTimeOffset Timestamp);
}
