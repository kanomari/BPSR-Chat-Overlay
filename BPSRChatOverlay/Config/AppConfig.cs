namespace BPSRChatOverlay.Config;

public sealed class AppConfig
{
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

    public int FontSize { get; set; } = 16;

    public double BackgroundOpacity { get; set; } = 0.7;

    public double TextOpacity { get; set; } = 1.0;

    public double MenuBackgroundOpacity { get; set; } = 0.9;

    public bool ClickThrough { get; set; } = false;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 800;

    public double WindowHeight { get; set; } = 600;

    public bool ShowWorldChat { get; set; } = true;

    public bool ShowChannelChat { get; set; } = true;

    public bool ShowPartyChat { get; set; } = true;

    public bool ShowGuildChat { get; set; } = true;

    public string ChatFilterKeywords { get; set; } = string.Empty;

    public string WorldChatTextColor { get; set; } = "#FFFFFFFF";

    public string ChannelChatTextColor { get; set; } = "#FFFFFFFF";

    public string PartyChatTextColor { get; set; } = "#FFFFFFFF";

    public string GuildChatTextColor { get; set; } = "#FFFFFFFF";

    public string ChatBackgroundColor { get; set; } = "#FF000000";

    public string MenuBackgroundColor { get; set; } = "#FFF2F2F2";

    public bool EnableMentionNotification { get; set; } = true;

    public bool EnableMentionSound { get; set; } = true;

    public string MentionKeywords { get; set; } = string.Empty;

    public string MentionHighlightColor { get; set; } = "#60FFD54F";

    public string MentionSoundFilePath { get; set; } = string.Empty;

    public bool ShowDebugPanel { get; set; } = false;

    public bool TopMost { get; set; } = true;
}
