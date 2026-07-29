using System.Windows;
using System.Windows.Media;

namespace BPSRChatOverlay.UIResources;

public static class ChatFontCatalog
{
    public const string DefaultFontFamilyName = "Meiryo UI";

    private static readonly string[] PreferredFontFamilyNames =
    [
        DefaultFontFamilyName,
        "Meiryo",
        "Yu Gothic UI",
        "Yu Gothic",
        "BIZ UDPGothic",
        "MS Gothic"
    ];

    private static readonly Lazy<HashSet<string>> InstalledFontFamilyNames =
        new(() => Fonts.SystemFontFamilies
            .Select(fontFamily => fontFamily.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> GetAvailableFontFamilyNames(
        string? configuredFontFamilyName)
    {
        List<string> availableNames = PreferredFontFamilyNames
            .Where(IsInstalled)
            .ToList();

        string? configuredName = Normalize(configuredFontFamilyName);
        if (configuredName is not null &&
            IsInstalled(configuredName) &&
            !availableNames.Contains(
                configuredName,
                StringComparer.OrdinalIgnoreCase))
        {
            availableNames.Add(configuredName);
        }

        if (availableNames.Count == 0)
        {
            availableNames.Add(Resolve(configuredFontFamilyName).Source);
        }

        return availableNames;
    }

    public static FontFamily Resolve(string? configuredFontFamilyName)
    {
        string? configuredName = Normalize(configuredFontFamilyName);
        if (configuredName is not null && IsInstalled(configuredName))
        {
            return new FontFamily(configuredName);
        }

        if (IsInstalled(DefaultFontFamilyName))
        {
            return new FontFamily(DefaultFontFamilyName);
        }

        return SystemFonts.MessageFontFamily;
    }

    private static bool IsInstalled(string fontFamilyName)
    {
        return InstalledFontFamilyNames.Value.Contains(fontFamilyName);
    }

    private static string? Normalize(string? fontFamilyName)
    {
        return string.IsNullOrWhiteSpace(fontFamilyName)
            ? null
            : fontFamilyName.Trim();
    }
}
