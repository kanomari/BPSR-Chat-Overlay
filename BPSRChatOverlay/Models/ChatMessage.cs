namespace BPSRChatOverlay.Models;

public sealed class ChatMessage
{
    public int ChannelType { get; init; }

    public string SenderName { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; } = DateTime.Now;
}