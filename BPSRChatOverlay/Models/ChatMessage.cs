using System.ComponentModel;

namespace BPSRChatOverlay.Models;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private bool _isTalkHighlightVisible;

    public int ChannelType { get; init; }

    public string SenderName { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; } = DateTime.Now;

    public bool IsMention { get; set; }

    public bool IsTalkHighlighted { get; set; }

    public bool IsTalkHighlightVisible
    {
        get => _isTalkHighlightVisible;
        set
        {
            if (_isTalkHighlightVisible == value)
            {
                return;
            }

            _isTalkHighlightVisible = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsTalkHighlightVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
