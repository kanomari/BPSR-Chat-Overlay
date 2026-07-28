using System.IO;
using System.Text.Json;

namespace BPSRChatOverlay.Config;

public static class ConfigManager
{
    private static readonly string ConfigFilePath =
        Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            var newConfig = new AppConfig();
            Save(newConfig);
            return newConfig;
        }

        string json = File.ReadAllText(ConfigFilePath);

        AppConfig config =
            JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

        config.TimeColumnWidth = Math.Clamp(
            config.TimeColumnWidth,
            AppConfig.MinTimeColumnWidth,
            AppConfig.MaxTimeColumnWidth);
        config.SenderNameColumnWidth = Math.Clamp(
            config.SenderNameColumnWidth,
            AppConfig.MinSenderNameColumnWidth,
            AppConfig.MaxSenderNameColumnWidth);
        config.ChatColorBandPosition =
            AppConfig.NormalizeChatColorBandPosition(
                config.ChatColorBandPosition);

        return config;
    }

    public static void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(ConfigFilePath, json);
    }
}
