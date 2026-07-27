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
            1 => "ワールド",
            2 => "チャンネル",
            3 => "パーティ",
            4 => "ギルド",
            _ => $"不明({channelType})"
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
