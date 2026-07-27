using System.Diagnostics;
using System.Windows.Media;

namespace BPSRChatOverlay.UIResources;

public static class ChatColors
{
    public const string DefaultChatTextColor = "#FFFFFFFF";
    public const string DefaultChatBackgroundColor = "#FF000000";
    public const string DefaultMenuBackgroundColor = "#FFF2F2F2";
    public const string DefaultMentionHighlightColor = "#60FFD54F";

    public static Brush World { get; private set; } = Brushes.White;

    public static Brush Channel { get; private set; } = Brushes.White;

    public static Brush Party { get; private set; } = Brushes.White;

    public static Brush Guild { get; private set; } = Brushes.White;

    public static Brush Unknown { get; } = Brushes.White;

    public static Brush ChatBackground { get; private set; } = Brushes.Black;

    public static Brush MenuBackground { get; private set; } =
        CreateBrush(DefaultMenuBackgroundColor, DefaultMenuBackgroundColor);

    public static Brush MentionHighlight { get; private set; } =
        CreateBrush(
            DefaultMentionHighlightColor,
            DefaultMentionHighlightColor);

    public static void Apply(
        string? worldTextColor,
        string? channelTextColor,
        string? partyTextColor,
        string? guildTextColor,
        string? chatBackgroundColor,
        string? menuBackgroundColor,
        string? mentionHighlightColor)
    {
        World = CreateBrush(worldTextColor, DefaultChatTextColor);
        Channel = CreateBrush(channelTextColor, DefaultChatTextColor);
        Party = CreateBrush(partyTextColor, DefaultChatTextColor);
        Guild = CreateBrush(guildTextColor, DefaultChatTextColor);
        ChatBackground = CreateBrush(
            chatBackgroundColor,
            DefaultChatBackgroundColor);
        MenuBackground = CreateBrush(
            menuBackgroundColor,
            DefaultMenuBackgroundColor);
        MentionHighlight = CreateBrush(
            mentionHighlightColor,
            DefaultMentionHighlightColor);
    }

    public static SolidColorBrush CreateBrush(
        string? colorText,
        string fallbackColor)
    {
        string colorToUse = string.IsNullOrWhiteSpace(colorText)
            ? fallbackColor
            : colorText;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorToUse);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Invalid color setting '{colorText}'. " +
                $"Using {fallbackColor}. {ex.Message}");

            var fallback =
                (Color)ColorConverter.ConvertFromString(fallbackColor);
            var brush = new SolidColorBrush(fallback);
            brush.Freeze();
            return brush;
        }
    }
}
