using System;
using System.IO;

namespace BPSRChatOverlay;

internal static class AppPaths
{
    private const string ApplicationDirectoryName = "BPSR Chat Overlay";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationDirectoryName);

    public static string ConfigPath { get; } =
        Path.Combine(DataDirectory, "config.json");

    public static string ConfigBackupPath { get; } =
        Path.Combine(DataDirectory, "config.json.bak");

    public static string ConfigTempPath { get; } =
        Path.Combine(DataDirectory, "config.json.tmp");

    public static string LogDirectory { get; } =
        Path.Combine(DataDirectory, "Logs");

    public static string LogFilePathPattern { get; } =
        Path.Combine(LogDirectory, "bpsr-chat-overlay-.log");
}
