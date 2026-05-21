namespace WorkIQChat.Shared;

/// <summary>
/// Represents a single message in the conversation history exchanged between
/// the chat client and <c>ChatHub</c>.
/// </summary>
/// <param name="Text">The message text.</param>
/// <param name="IsUser">
/// <see langword="true"/> if the message was sent by the user;
/// <see langword="false"/> for AI assistant responses.
/// </param>
public record ConversationMessage(string Text, bool IsUser);
