using BPSRChatOverlay.Models;

namespace BPSRChatOverlay.Managers;

public class ChatManager
{
    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public ChatManager()
    {
        ChatCaptureManager.ChatReceived += OnChatReceived;
    }

    private void OnChatReceived(ChatMessage message)
    {
        _messages.Add(message);

        Console.WriteLine($"Stored: {_messages.Count} messages");
    }
}