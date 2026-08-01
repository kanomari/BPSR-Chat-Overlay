namespace BPSRChatOverlay.Config;

public sealed class HotkeySettings
{
    public HotkeyGestureConfig ClickThroughToggle { get; set; } =
        HotkeyGestureConfig.CreateDefaultClickThrough();

    public HotkeyGestureConfig CollapseToggle { get; set; } =
        HotkeyGestureConfig.CreateDefaultCollapse();

    public HotkeySettings Clone()
    {
        return new HotkeySettings
        {
            ClickThroughToggle = ClickThroughToggle.Clone(),
            CollapseToggle = CollapseToggle.Clone()
        };
    }
}

public sealed class HotkeyGestureConfig
{
    public const int F9VirtualKey = 0x78;
    public const int F10VirtualKey = 0x79;

    public bool Control { get; set; }

    public bool Shift { get; set; }

    public bool Alt { get; set; }

    public int? VirtualKey { get; set; }

    public HotkeyGestureConfig Clone()
    {
        return new HotkeyGestureConfig
        {
            Control = Control,
            Shift = Shift,
            Alt = Alt,
            VirtualKey = VirtualKey
        };
    }

    public static HotkeyGestureConfig CreateDefaultClickThrough()
    {
        return new HotkeyGestureConfig
        {
            Control = true,
            Shift = true,
            VirtualKey = F10VirtualKey
        };
    }

    public static HotkeyGestureConfig CreateDefaultCollapse()
    {
        return new HotkeyGestureConfig
        {
            Control = true,
            Shift = true,
            VirtualKey = F9VirtualKey
        };
    }

    public static HotkeyGestureConfig CreateUnset()
    {
        return new HotkeyGestureConfig();
    }
}
