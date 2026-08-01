using System.Windows.Input;
using BPSRChatOverlay.Config;

namespace BPSRChatOverlay.Hotkeys;

internal enum HotkeyAction
{
    ClickThroughToggle,
    CollapseToggle
}

internal enum HotkeyValidationError
{
    None,
    Required,
    InvalidKey,
    ProhibitedKey,
    ProhibitedCombination
}

internal readonly record struct HotkeyGesture(
    bool Control,
    bool Shift,
    bool Alt,
    int VirtualKey)
{
    public static bool TryCreate(
        HotkeyGestureConfig? config,
        out HotkeyGesture gesture)
    {
        if (config?.VirtualKey is not { } virtualKey)
        {
            gesture = default;
            return false;
        }

        gesture = new HotkeyGesture(
            config.Control,
            config.Shift,
            config.Alt,
            virtualKey);
        return true;
    }
}

internal static class HotkeyUtilities
{
    private const int VkLButton = 0x01;
    private const int VkXButton2 = 0x06;
    private const int VkBack = 0x08;
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkKana = 0x15;
    private const int VkKanji = 0x19;
    private const int VkEscape = 0x1B;
    private const int VkConvert = 0x1C;
    private const int VkNonConvert = 0x1D;
    private const int VkSpace = 0x20;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkDelete = 0x2E;
    private const int VkF4 = 0x73;
    private const int VkF12 = 0x7B;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;

    private static readonly HashSet<int> ProhibitedVirtualKeys =
    [
        VkBack,
        VkTab,
        VkReturn,
        VkShift,
        VkControl,
        VkMenu,
        VkKana,
        VkKanji,
        VkEscape,
        VkConvert,
        VkNonConvert,
        VkSpace,
        VkLeft,
        VkUp,
        VkRight,
        VkDown,
        VkLWin,
        VkRWin,
        VkF12,
        VkLShift,
        VkRShift,
        VkLControl,
        VkRControl,
        VkLMenu,
        VkRMenu
    ];

    public static HotkeyValidationError Validate(
        HotkeyGestureConfig? config,
        bool required)
    {
        if (config?.VirtualKey is not { } virtualKey)
        {
            return required
                ? HotkeyValidationError.Required
                : HotkeyValidationError.None;
        }

        if (virtualKey <= 0 ||
            virtualKey > byte.MaxValue ||
            virtualKey is >= VkLButton and <= VkXButton2 ||
            KeyInterop.KeyFromVirtualKey(virtualKey) == Key.None)
        {
            return HotkeyValidationError.InvalidKey;
        }

        if (ProhibitedVirtualKeys.Contains(virtualKey))
        {
            return HotkeyValidationError.ProhibitedKey;
        }

        if (config.Alt && virtualKey == VkF4 ||
            config.Control && config.Alt && virtualKey == VkDelete)
        {
            return HotkeyValidationError.ProhibitedCombination;
        }

        return HotkeyValidationError.None;
    }

    public static bool AreEqual(
        HotkeyGestureConfig? first,
        HotkeyGestureConfig? second)
    {
        return HotkeyGesture.TryCreate(first, out HotkeyGesture firstGesture) &&
               HotkeyGesture.TryCreate(second, out HotkeyGesture secondGesture) &&
               firstGesture == secondGesture;
    }

    public static string FormatGesture(HotkeyGestureConfig? config)
    {
        if (config?.VirtualKey is not { } virtualKey)
        {
            return "未設定";
        }

        var parts = new List<string>(4);
        if (config.Control)
        {
            parts.Add("Ctrl");
        }

        if (config.Shift)
        {
            parts.Add("Shift");
        }

        if (config.Alt)
        {
            parts.Add("Alt");
        }

        parts.Add(FormatKey(virtualKey));
        return string.Join(" + ", parts);
    }

    public static string FormatGesture(HotkeyGesture gesture)
    {
        return FormatGesture(new HotkeyGestureConfig
        {
            Control = gesture.Control,
            Shift = gesture.Shift,
            Alt = gesture.Alt,
            VirtualKey = gesture.VirtualKey
        });
    }

    public static string FormatKey(int virtualKey)
    {
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        return key switch
        {
            Key.Next => "PageDown",
            Key.Capital => "CapsLock",
            Key.Snapshot => "PrintScreen",
            Key.Scroll => "ScrollLock",
            Key.Return => "Enter",
            Key.None => $"VK 0x{virtualKey:X2}",
            _ => key.ToString()
        };
    }

    public static string GetActionDisplayName(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.ClickThroughToggle => "クリック透過切替",
            HotkeyAction.CollapseToggle => "収納／展開切替",
            _ => action.ToString()
        };
    }
}
