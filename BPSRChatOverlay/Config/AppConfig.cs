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

    public bool TopMost { get; set; } = true;
}
