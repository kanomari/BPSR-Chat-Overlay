using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BPSRChatOverlay.UIResources;

namespace BPSRChatOverlay.Converters;

public sealed class MentionHighlightConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is true
            ? ChatColors.MentionHighlight
            : Brushes.Transparent;
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
