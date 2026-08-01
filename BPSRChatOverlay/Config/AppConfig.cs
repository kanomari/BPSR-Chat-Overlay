namespace BPSRChatOverlay.Config;

public sealed class AppConfig
{
    public const int DefaultTimeColumnWidth = 40;
    public const int MinTimeColumnWidth = 36;
    public const int MaxTimeColumnWidth = 80;
    public const int DefaultSenderNameColumnWidth = 108;
    public const int MinSenderNameColumnWidth = 60;
    public const int MaxSenderNameColumnWidth = 240;
    public const string ChatColorBandPositionLeft = "Left";
    public const string ChatColorBandPositionRight = "Right";
    public const string CollapseSideLeft = "Left";
    public const string CollapseSideRight = "Right";
    public const string CollapseSideTop = "Top";
    public const string CollapseSideBottom = "Bottom";
    public const double DefaultEdgeHandleThickness = 16.0;
    public const double MinEdgeHandleThickness = 8.0;
    public const double MaxEdgeHandleThickness = 32.0;
    public const double DefaultEdgeHandleOpacity = 0.25;

    public string? CaptureDeviceName { get; set; }

    public List<string> ExeNames { get; set; } =
    [
        "BPSR",
        "BPSR_STEAM",
        "BPSR_EPIC",
        "StarSEA",
        "StarASIA",
        "StarSEA_STEAM",
        "StarASIA_STEAM",
        "Star"
    ];

    public int FontSize { get; set; } = 15;

    public string ChatFontFamily { get; set; } = "Meiryo UI";

    public bool EnableBoldMessageText { get; set; } = false;

    public int TimeColumnWidth { get; set; } = DefaultTimeColumnWidth;

    public int SenderNameColumnWidth { get; set; } =
        DefaultSenderNameColumnWidth;

    public double BackgroundOpacity { get; set; } = 0.7;

    public double TextOpacity { get; set; } = 1.0;

    public double MenuBackgroundOpacity { get; set; } = 0.9;

    public bool ClickThrough { get; set; } = false;

    public bool HighlightNewChatRows { get; set; } = true;

    public bool EnableChatTextShadow { get; set; } = true;

    public string ChatTextShadowColor { get; set; } = "#FF203040";

    public bool ShowChatToggleButtons { get; set; } = true;

    public bool ShowChatFilterToggle { get; set; } = true;

    public bool ShowMentionHighlightToggle { get; set; } = true;

    public bool ShowCollapseButton { get; set; } = true;

    public bool ShowChatSeparators { get; set; } = true;

    public bool ShowChatZebraStripes { get; set; } = true;

    public bool ShowChatColorBand { get; set; } = true;

    public string ChatColorBandPosition { get; set; } =
        ChatColorBandPositionLeft;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 800;

    public double WindowHeight { get; set; } = 600;

    public bool ShowWorldChat { get; set; } = true;

    public bool ShowChannelChat { get; set; } = true;

    public bool ShowPartyChat { get; set; } = true;

    public bool ShowGuildChat { get; set; } = true;

    public bool ShowNewbieChat { get; set; } = true;

    public bool ShowTalkChat { get; set; } = true;

    public bool EnableChatFilter { get; set; } = true;

    public string ChatFilterKeywords { get; set; } = string.Empty;

    public string HiddenChatKeywords { get; set; } = string.Empty;

    public bool IncludeSenderNameInHiddenChatKeywords { get; set; } = false;

    public string WorldChatTextColor { get; set; } = "#FFBA55D3";

    public string ChannelChatTextColor { get; set; } = "#FFFFFFFF";

    public string PartyChatTextColor { get; set; } = "#FF00BFFF";

    public string GuildChatTextColor { get; set; } = "#FF9ACD32";

    public string NewbieChatTextColor { get; set; } = "#FF808080";

    public string TalkChatTextColor { get; set; } = "#FFFFB6C1";

    public string ChatBackgroundColor { get; set; } = "#FF000000";

    public string MenuBackgroundColor { get; set; } = "#FFF2F2F2";

    public bool EnableMentionNotification { get; set; } = true;

    public bool EnableMentionSound { get; set; } = true;

    public string MentionKeywords { get; set; } = string.Empty;

    public string MentionHighlightColor { get; set; } = "#60FFD54F";

    public string MentionSoundFilePath { get; set; } = string.Empty;

    public bool EnableTalkHighlight { get; set; } = true;

    public string TalkHighlightBackgroundColor { get; set; } = "#704B9CD3";

    public bool EnableTalkSound { get; set; } = true;

    public string TalkSoundFilePath { get; set; } = string.Empty;

    public bool ShowDebugPanel { get; set; } = false;

    public bool TopMost { get; set; } = true;

    public string CollapseSide { get; set; } = CollapseSideRight;

    public double EdgeHandleThickness { get; set; } =
        DefaultEdgeHandleThickness;

    public double EdgeHandleOpacity { get; set; } =
        DefaultEdgeHandleOpacity;

    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public HotkeySettings Hotkeys { get; set; } = new();

    public DateTime? LastSuccessfulUpdateCheckUtc { get; set; }

    public string LastNotifiedVersion { get; set; } = string.Empty;

    public static string NormalizeChatColorBandPosition(string? position)
    {
        return string.Equals(
            position,
            ChatColorBandPositionRight,
            StringComparison.OrdinalIgnoreCase)
            ? ChatColorBandPositionRight
            : ChatColorBandPositionLeft;
    }

    public static string NormalizeCollapseSide(string? side)
    {
        if (string.Equals(
                side,
                CollapseSideLeft,
                StringComparison.OrdinalIgnoreCase))
        {
            return CollapseSideLeft;
        }

        if (string.Equals(
                side,
                CollapseSideTop,
                StringComparison.OrdinalIgnoreCase))
        {
            return CollapseSideTop;
        }

        if (string.Equals(
                side,
                CollapseSideBottom,
                StringComparison.OrdinalIgnoreCase))
        {
            return CollapseSideBottom;
        }

        return CollapseSideRight;
    }
}
