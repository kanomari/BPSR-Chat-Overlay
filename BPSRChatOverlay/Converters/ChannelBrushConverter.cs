using System.Globalization;
using System.Windows.Data;
using BPSRChatOverlay.UIResources;
using BPSRChatOverlay.Models;

namespace BPSRChatOverlay.Converters;

public sealed class ChannelBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        bool useConfiguredChatTextColor =
            parameter is string text &&
            text == "ChatText";

        if (value is ChatMessage message &&
            message.NotificationType == InAppNotificationType.FieldBossAlarm)
        {
            return ChatColors.FieldBossAlarmText;
        }

        int? resolvedChannelType = value switch
        {
            int channel => channel,
            ChatMessage chatMessage => chatMessage.ChannelType,
            _ => null
        };

        if (resolvedChannelType is not { } channelType)
        {
            return useConfiguredChatTextColor
                ? ChatColors.Unknown
                : System.Windows.Media.Brushes.Gray;
        }

        return channelType switch
        {
            1 => useConfiguredChatTextColor
                ? ChatColors.World
                : System.Windows.Media.Brushes.MediumOrchid,
            2 => useConfiguredChatTextColor
                ? ChatColors.Channel
                : System.Windows.Media.Brushes.White,
            3 => useConfiguredChatTextColor
                ? ChatColors.Party
                : System.Windows.Media.Brushes.DeepSkyBlue,
            4 => useConfiguredChatTextColor
                ? ChatColors.Guild
                : System.Windows.Media.Brushes.YellowGreen,
            5 => ChatColors.Talk,
            9 => ChatColors.Newbie,
            _ => useConfiguredChatTextColor
                ? ChatColors.Unknown
                : System.Windows.Media.Brushes.Gray
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
