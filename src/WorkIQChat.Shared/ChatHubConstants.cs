namespace WorkIQChat.Shared;

/// <summary>
/// Constants shared between the server hub and WASM client for the chat SignalR connection.
/// </summary>
public static class ChatHubConstants
{
    /// <summary>The URL path the <c>ChatHub</c> is mapped to.</summary>
    public const string HubUrl = "/hubs/chat";

    /// <summary>The name of the hub method the client invokes to start a streaming response.</summary>
    public const string StreamMessageMethod = "StreamMessage";
}
