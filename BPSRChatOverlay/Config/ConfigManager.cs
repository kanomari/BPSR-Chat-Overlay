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
            var config = new AppConfig();
            Save(config);
            return config;
        }

        string json = File.ReadAllText(ConfigFilePath);

        return JsonSerializer.Deserialize<AppConfig>(json)
               ?? new AppConfig();
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
