using System.Globalization;
using System.Windows.Data;
using BPSRChatOverlay.UIResources;

namespace BPSRChatOverlay.Converters;

public sealed class ChannelBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not int channelType)
        {
            return ChatColors.Unknown;
        }

        return channelType switch
        {
            1 => ChatColors.World,
            2 => ChatColors.Channel,
            3 => ChatColors.Party,
            4 => ChatColors.Guild,
            _ => ChatColors.Unknown
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
