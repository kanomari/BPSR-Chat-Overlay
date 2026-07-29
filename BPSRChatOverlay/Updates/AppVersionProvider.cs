using System.Reflection;

namespace BPSRChatOverlay.Updates;

public static class AppVersionProvider
{
    public static Version CurrentVersion { get; } = ResolveCurrentVersion();

    public static string CurrentVersionText { get; } =
        FormatVersion(CurrentVersion);

    public static bool TryParseVersionTag(
        string? versionText,
        out Version version)
    {
        version = new Version();

        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        string normalized = versionText.Trim();
        if (normalized.StartsWith('v') ||
            normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        int metadataSeparator = normalized.IndexOfAny(['+', '-']);
        if (metadataSeparator >= 0)
        {
            normalized = normalized[..metadataSeparator];
        }

        if (!Version.TryParse(normalized, out Version? parsedVersion))
        {
            return false;
        }

        version = NormalizeVersion(parsedVersion);
        return true;
    }

    public static string FormatVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        string numericVersion = version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        return $"v{numericVersion}";
    }

    private static Version ResolveCurrentVersion()
    {
        Assembly assembly =
            Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (TryParseVersionTag(
                informationalVersion,
                out Version informational))
        {
            return informational;
        }

        Version? assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null
            ? new Version(0, 0, 0, 0)
            : NormalizeVersion(assemblyVersion);
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }
}
