using System.Diagnostics;
using System.Windows.Media;

namespace BPSRChatOverlay.UIResources;

public static class ChatColors
{
    public const string DefaultChatTextColor = "#FFFFFFFF";
    public const string DefaultWorldChatTextColor = "#FFBA55D3";
    public const string DefaultChannelChatTextColor = "#FFFFFFFF";
    public const string DefaultPartyChatTextColor = "#FF00BFFF";
    public const string DefaultGuildChatTextColor = "#FF9ACD32";
    public const string DefaultNewbieChatTextColor = "#FF808080";
    public const string DefaultTalkChatTextColor = "#FFFFB6C1";
    public const string DefaultChatBackgroundColor = "#FF000000";
    public const string DefaultMenuBackgroundColor = "#FFF2F2F2";
    public const string DefaultMentionHighlightColor = "#60FFD54F";
    public const string DefaultChatTextShadowColor = "#FF203040";
    public const string DefaultTalkHighlightColor = "#704B9CD3";

    public static Brush World { get; private set; } =
        CreateBrush(
            DefaultWorldChatTextColor,
            DefaultWorldChatTextColor);

    public static Brush Channel { get; private set; } =
        CreateBrush(
            DefaultChannelChatTextColor,
            DefaultChannelChatTextColor);

    public static Brush Party { get; private set; } =
        CreateBrush(
            DefaultPartyChatTextColor,
            DefaultPartyChatTextColor);

    public static Brush Guild { get; private set; } =
        CreateBrush(
            DefaultGuildChatTextColor,
            DefaultGuildChatTextColor);

    public static Brush Newbie { get; private set; } =
        CreateBrush(
            DefaultNewbieChatTextColor,
            DefaultNewbieChatTextColor);

    public static Brush Talk { get; private set; } =
        CreateBrush(
            DefaultTalkChatTextColor,
            DefaultTalkChatTextColor);

    public static Brush Unknown { get; } = Brushes.White;

    public static Brush ChatBackground { get; private set; } = Brushes.Black;

    public static Brush MenuBackground { get; private set; } =
        CreateBrush(DefaultMenuBackgroundColor, DefaultMenuBackgroundColor);

    public static Brush MentionHighlight { get; private set; } =
        CreateBrush(
            DefaultMentionHighlightColor,
            DefaultMentionHighlightColor);

    public static Brush TalkHighlight { get; private set; } =
        CreateBrush(
            DefaultTalkHighlightColor,
            DefaultTalkHighlightColor);

    public static Brush TalkHighlightDim { get; private set; } =
        CreateDimmedBrush(
            DefaultTalkHighlightColor,
            DefaultTalkHighlightColor);

    public static void Apply(
        string? worldTextColor,
        string? channelTextColor,
        string? partyTextColor,
        string? guildTextColor,
        string? newbieTextColor,
        string? talkTextColor,
        string? chatBackgroundColor,
        string? menuBackgroundColor,
        string? mentionHighlightColor,
        string? talkHighlightBackgroundColor)
    {
        World = CreateBrush(
            worldTextColor,
            DefaultWorldChatTextColor);
        Channel = CreateBrush(
            channelTextColor,
            DefaultChannelChatTextColor);
        Party = CreateBrush(
            partyTextColor,
            DefaultPartyChatTextColor);
        Guild = CreateBrush(
            guildTextColor,
            DefaultGuildChatTextColor);
        Newbie = CreateBrush(
            newbieTextColor,
            DefaultNewbieChatTextColor);
        Talk = CreateBrush(
            talkTextColor,
            DefaultTalkChatTextColor);
        ChatBackground = CreateBrush(
            chatBackgroundColor,
            DefaultChatBackgroundColor);
        MenuBackground = CreateBrush(
            menuBackgroundColor,
            DefaultMenuBackgroundColor);
        MentionHighlight = CreateBrush(
            mentionHighlightColor,
            DefaultMentionHighlightColor);
        TalkHighlight = CreateBrush(
            talkHighlightBackgroundColor,
            DefaultTalkHighlightColor);
        TalkHighlightDim = CreateDimmedBrush(
            talkHighlightBackgroundColor,
            DefaultTalkHighlightColor);
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

    private static SolidColorBrush CreateDimmedBrush(
        string? colorText,
        string fallbackColor)
    {
        SolidColorBrush source = CreateBrush(colorText, fallbackColor);
        Color color = source.Color;
        color.A = (byte)Math.Round(color.A * 0.4);

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
