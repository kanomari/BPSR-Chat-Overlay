using System.IO;
using System.Text.Json;

namespace BPSRChatOverlay.Config;

public static class ConfigManager
{
    private const string FileName = "config.json";

    public static AppConfig Load()
    {
        if (!File.Exists(FileName))
        {
            var config = new AppConfig();
            Save(config);
            return config;
        }

        string json = File.ReadAllText(FileName);

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

        File.WriteAllText(FileName, json);
    }
}