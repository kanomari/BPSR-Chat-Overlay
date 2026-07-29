using System.IO;
using System.Text.Json;
using Serilog;

namespace BPSRChatOverlay.Config;

public static class ConfigManager
{
    private const double MinWindowWidth = 280;
    private const double MinWindowHeight = 100;
    private const double MaxWindowDimension = 10000;

    private static readonly object SaveLock = new();
    private static readonly string ConfigFilePath = AppPaths.ConfigPath;
    private static readonly string BackupFilePath = AppPaths.ConfigBackupPath;
    private static readonly string TemporaryFilePath = AppPaths.ConfigTempPath;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static AppConfig Load()
    {
        RemoveStaleTemporaryFile();

        if (!File.Exists(ConfigFilePath))
        {
            if (TryLoadFile(BackupFilePath, "backup", out AppConfig backup))
            {
                Log.Warning(
                    "Configuration file was missing. Recovered from backup. BackupPath: {BackupPath}",
                    BackupFilePath);
                TryRepairConfigFile(backup);
                return backup;
            }

            Log.Information(
                "Configuration file does not exist. Starting with defaults. ConfigPath: {ConfigPath}",
                ConfigFilePath);

            var defaultConfig = new AppConfig();
            TryRepairConfigFile(defaultConfig);
            return defaultConfig;
        }

        if (TryLoadFile(ConfigFilePath, "config", out AppConfig config))
        {
            Log.Information(
                "Configuration loaded successfully. ConfigPath: {ConfigPath}",
                ConfigFilePath);
            return config;
        }

        Log.Warning(
            "Failed to load configuration. Attempting backup recovery. ConfigPath: {ConfigPath}",
            ConfigFilePath);

        if (TryLoadFile(BackupFilePath, "backup", out AppConfig recovered))
        {
            Log.Warning(
                "Configuration recovered from backup. BackupPath: {BackupPath}",
                BackupFilePath);
            TryRepairConfigFile(recovered);
            return recovered;
        }

        Log.Error(
            "Configuration and backup could not be loaded. Starting with defaults. ConfigPath: {ConfigPath}, BackupPath: {BackupPath}",
            ConfigFilePath,
            BackupFilePath);

        var fallbackConfig = new AppConfig();
        TryRepairConfigFile(fallbackConfig);
        return fallbackConfig;
    }

    public static void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (SaveLock)
        {
            string stage = "create directory";

            try
            {
                string? configDirectory =
                    Path.GetDirectoryName(ConfigFilePath);
                if (!string.IsNullOrEmpty(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                }

                RemoveStaleTemporaryFile();
                Normalize(config);

                stage = "write temporary";
                using (var stream = new FileStream(
                           TemporaryFilePath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    JsonSerializer.Serialize(
                        stream,
                        config,
                        SerializerOptions);
                    stream.Flush(flushToDisk: true);
                }

                stage = "validate temporary";
                _ = DeserializeFile(TemporaryFilePath);

                stage = "replace configuration";
                bool currentConfigIsValid =
                    File.Exists(ConfigFilePath) &&
                    CanDeserialize(ConfigFilePath);

                if (currentConfigIsValid)
                {
                    File.Replace(
                        TemporaryFilePath,
                        ConfigFilePath,
                        BackupFilePath,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(
                        TemporaryFilePath,
                        ConfigFilePath,
                        overwrite: true);
                }

                Log.Debug(
                    "Configuration saved successfully. ConfigPath: {ConfigPath}, BackupPath: {BackupPath}",
                    ConfigFilePath,
                    BackupFilePath);
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Configuration save failed. Stage: {Stage}, ConfigPath: {ConfigPath}, BackupPath: {BackupPath}, TemporaryPath: {TemporaryPath}",
                    stage,
                    ConfigFilePath,
                    BackupFilePath,
                    TemporaryFilePath);
                throw;
            }
            finally
            {
                TryDeleteTemporaryFileAfterSave();
            }
        }
    }

    private static bool TryLoadFile(
        string path,
        string fileKind,
        out AppConfig config)
    {
        config = null!;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            config = DeserializeFile(path);
            Normalize(config);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to read configuration file. FileKind: {FileKind}, Path: {Path}",
                fileKind,
                path);
            return false;
        }
    }

    private static AppConfig DeserializeFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return JsonSerializer.Deserialize<AppConfig>(
                   stream,
                   SerializerOptions)
               ?? throw new JsonException(
                   "The configuration file contained no settings.");
    }

    private static bool CanDeserialize(string path)
    {
        try
        {
            _ = DeserializeFile(path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Existing configuration is not valid and will not replace the backup. ConfigPath: {ConfigPath}",
                path);
            return false;
        }
    }

    private static void TryRepairConfigFile(AppConfig config)
    {
        try
        {
            Save(config);
            Log.Information(
                "Recovered configuration was saved successfully. ConfigPath: {ConfigPath}",
                ConfigFilePath);
        }
        catch
        {
            // Save already logged the stage and exception. Continue startup.
        }
    }

    private static void RemoveStaleTemporaryFile()
    {
        if (!File.Exists(TemporaryFilePath))
        {
            return;
        }

        try
        {
            File.Delete(TemporaryFilePath);
            Log.Warning(
                "Removed a stale temporary configuration file. TemporaryPath: {TemporaryPath}",
                TemporaryFilePath);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to remove a stale temporary configuration file. TemporaryPath: {TemporaryPath}",
                TemporaryFilePath);
        }
    }

    private static void TryDeleteTemporaryFileAfterSave()
    {
        if (!File.Exists(TemporaryFilePath))
        {
            return;
        }

        try
        {
            File.Delete(TemporaryFilePath);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to clean up the temporary configuration file. TemporaryPath: {TemporaryPath}",
                TemporaryFilePath);
        }
    }

    private static void Normalize(AppConfig config)
    {
        var defaults = new AppConfig();

        if (config.ExeNames is null || config.ExeNames.Count == 0)
        {
            LogCorrection(
                nameof(AppConfig.ExeNames),
                "The executable name list was null or empty.");
            config.ExeNames = [.. defaults.ExeNames];
        }
        else
        {
            List<string> validExeNames = config.ExeNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validExeNames.Count != config.ExeNames.Count)
            {
                LogCorrection(
                    nameof(AppConfig.ExeNames),
                    "Null, empty, or duplicate executable names were removed.");
                config.ExeNames = validExeNames.Count > 0
                    ? validExeNames
                    : [.. defaults.ExeNames];
            }
        }

        config.FontSize = NormalizeInt(
            config.FontSize,
            8,
            48,
            defaults.FontSize,
            nameof(AppConfig.FontSize));
        config.TimeColumnWidth = NormalizeInt(
            config.TimeColumnWidth,
            AppConfig.MinTimeColumnWidth,
            AppConfig.MaxTimeColumnWidth,
            defaults.TimeColumnWidth,
            nameof(AppConfig.TimeColumnWidth));
        config.SenderNameColumnWidth = NormalizeInt(
            config.SenderNameColumnWidth,
            AppConfig.MinSenderNameColumnWidth,
            AppConfig.MaxSenderNameColumnWidth,
            defaults.SenderNameColumnWidth,
            nameof(AppConfig.SenderNameColumnWidth));
        config.BackgroundOpacity = NormalizeOpacity(
            config.BackgroundOpacity,
            defaults.BackgroundOpacity,
            nameof(AppConfig.BackgroundOpacity));
        config.TextOpacity = NormalizeOpacity(
            config.TextOpacity,
            defaults.TextOpacity,
            nameof(AppConfig.TextOpacity));
        config.MenuBackgroundOpacity = NormalizeOpacity(
            config.MenuBackgroundOpacity,
            defaults.MenuBackgroundOpacity,
            nameof(AppConfig.MenuBackgroundOpacity));
        config.WindowWidth = NormalizeWindowDimension(
            config.WindowWidth,
            MinWindowWidth,
            defaults.WindowWidth,
            nameof(AppConfig.WindowWidth));
        config.WindowHeight = NormalizeWindowDimension(
            config.WindowHeight,
            MinWindowHeight,
            defaults.WindowHeight,
            nameof(AppConfig.WindowHeight));

        if (config.WindowLeft is { } left && !double.IsFinite(left))
        {
            LogCorrection(
                nameof(AppConfig.WindowLeft),
                "The window position was not finite.");
            config.WindowLeft = null;
        }

        if (config.WindowTop is { } top && !double.IsFinite(top))
        {
            LogCorrection(
                nameof(AppConfig.WindowTop),
                "The window position was not finite.");
            config.WindowTop = null;
        }

        string normalizedBandPosition =
            AppConfig.NormalizeChatColorBandPosition(
                config.ChatColorBandPosition);
        if (!string.Equals(
                config.ChatColorBandPosition,
                normalizedBandPosition,
                StringComparison.Ordinal))
        {
            LogCorrection(
                nameof(AppConfig.ChatColorBandPosition),
                "The value was not a supported position.");
            config.ChatColorBandPosition = normalizedBandPosition;
        }

        NormalizeRequiredStrings(config, defaults);
    }

    private static int NormalizeInt(
        int value,
        int minimum,
        int maximum,
        int fallback,
        string propertyName)
    {
        if (value >= minimum && value <= maximum)
        {
            return value;
        }

        LogCorrection(
            propertyName,
            $"The value was outside the supported range {minimum}-{maximum}.");
        return fallback;
    }

    private static double NormalizeOpacity(
        double value,
        double fallback,
        string propertyName)
    {
        if (double.IsFinite(value) && value >= 0.0 && value <= 1.0)
        {
            return value;
        }

        LogCorrection(
            propertyName,
            "The value was not finite or was outside the range 0.0-1.0.");
        return fallback;
    }

    private static double NormalizeWindowDimension(
        double value,
        double minimum,
        double fallback,
        string propertyName)
    {
        if (double.IsFinite(value) &&
            value >= minimum &&
            value <= MaxWindowDimension)
        {
            return value;
        }

        LogCorrection(
            propertyName,
            $"The value was not finite or was outside the range {minimum}-{MaxWindowDimension}.");
        return fallback;
    }

    private static void NormalizeRequiredStrings(
        AppConfig config,
        AppConfig defaults)
    {
        config.ChatFilterKeywords = NormalizeString(
            config.ChatFilterKeywords,
            defaults.ChatFilterKeywords,
            nameof(AppConfig.ChatFilterKeywords));
        config.WorldChatTextColor = NormalizeString(
            config.WorldChatTextColor,
            defaults.WorldChatTextColor,
            nameof(AppConfig.WorldChatTextColor));
        config.ChannelChatTextColor = NormalizeString(
            config.ChannelChatTextColor,
            defaults.ChannelChatTextColor,
            nameof(AppConfig.ChannelChatTextColor));
        config.PartyChatTextColor = NormalizeString(
            config.PartyChatTextColor,
            defaults.PartyChatTextColor,
            nameof(AppConfig.PartyChatTextColor));
        config.GuildChatTextColor = NormalizeString(
            config.GuildChatTextColor,
            defaults.GuildChatTextColor,
            nameof(AppConfig.GuildChatTextColor));
        config.NewbieChatTextColor = NormalizeString(
            config.NewbieChatTextColor,
            defaults.NewbieChatTextColor,
            nameof(AppConfig.NewbieChatTextColor));
        config.TalkChatTextColor = NormalizeString(
            config.TalkChatTextColor,
            defaults.TalkChatTextColor,
            nameof(AppConfig.TalkChatTextColor));
        config.ChatBackgroundColor = NormalizeString(
            config.ChatBackgroundColor,
            defaults.ChatBackgroundColor,
            nameof(AppConfig.ChatBackgroundColor));
        config.MenuBackgroundColor = NormalizeString(
            config.MenuBackgroundColor,
            defaults.MenuBackgroundColor,
            nameof(AppConfig.MenuBackgroundColor));
        config.MentionKeywords = NormalizeString(
            config.MentionKeywords,
            defaults.MentionKeywords,
            nameof(AppConfig.MentionKeywords));
        config.MentionHighlightColor = NormalizeString(
            config.MentionHighlightColor,
            defaults.MentionHighlightColor,
            nameof(AppConfig.MentionHighlightColor));
        config.TalkHighlightBackgroundColor = NormalizeString(
            config.TalkHighlightBackgroundColor,
            defaults.TalkHighlightBackgroundColor,
            nameof(AppConfig.TalkHighlightBackgroundColor));
        config.MentionSoundFilePath = NormalizeString(
            config.MentionSoundFilePath,
            defaults.MentionSoundFilePath,
            nameof(AppConfig.MentionSoundFilePath));
        config.TalkSoundFilePath = NormalizeString(
            config.TalkSoundFilePath,
            defaults.TalkSoundFilePath,
            nameof(AppConfig.TalkSoundFilePath));
    }

    private static string NormalizeString(
        string? value,
        string fallback,
        string propertyName)
    {
        if (value is not null)
        {
            return value;
        }

        LogCorrection(propertyName, "The value was null.");
        return fallback;
    }

    private static void LogCorrection(string propertyName, string reason)
    {
        Log.Warning(
            "Corrected invalid configuration value. Property: {Property}, Reason: {Reason}",
            propertyName,
            reason);
    }
}
