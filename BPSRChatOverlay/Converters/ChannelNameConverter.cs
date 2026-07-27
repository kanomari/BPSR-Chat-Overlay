using System.Globalization;
using System.Windows.Data;

namespace BPSRChatOverlay.Converters;

public sealed class ChannelNameConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not int channelType)
        {
            return "不明";
        }

        return channelType switch
        {
            1 => "W",
            2 => "Ch",
            3 => "P",
            4 => "G",
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
