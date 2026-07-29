using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BPSRChatOverlay.UIResources;

namespace BPSRChatOverlay.Converters;

public sealed class ChatHighlightConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (values.Length > 2 && values[2] is true)
        {
            return values[1] is true
                ? ChatColors.TalkHighlight
                : ChatColors.TalkHighlightDim;
        }

        return values.Length > 0 && values[0] is true
            ? ChatColors.MentionHighlight
            : Brushes.Transparent;
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
