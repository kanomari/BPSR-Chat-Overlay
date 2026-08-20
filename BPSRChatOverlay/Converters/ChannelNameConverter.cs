using System.Globalization;
using System.Windows.Data;
using BPSRChatOverlay.Models;

namespace BPSRChatOverlay.Converters;

public sealed class ChannelNameConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is ChatMessage message &&
            message.NotificationType != InAppNotificationType.None)
        {
            return "通知";
        }

        int? resolvedChannelType = value switch
        {
            int channel => channel,
            ChatMessage chatMessage => chatMessage.ChannelType,
            _ => null
        };

        if (resolvedChannelType is not { } channelType)
        {
            return "不明";
        }

        return channelType switch
        {
            1 => "W",
            2 => "Ch",
            3 => "P",
            4 => "G",
            5 => "✉",
            9 => "N",
            _ => "?"
        };
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
