namespace BPSRChatOverlay.Updates;

public enum UpdateCheckStatus
{
    Success,
    NoStableRelease,
    Failed,
    Cancelled
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version CurrentVersion,
    Version? LatestVersion = null,
    Uri? ReleasePageUri = null)
{
    public bool IsSuccess =>
        Status is UpdateCheckStatus.Success or
            UpdateCheckStatus.NoStableRelease;

    public bool IsUpdateAvailable =>
        Status == UpdateCheckStatus.Success &&
        LatestVersion is not null &&
        LatestVersion > CurrentVersion;

    public string CurrentVersionText =>
        AppVersionProvider.FormatVersion(CurrentVersion);

    public string? LatestVersionText =>
        LatestVersion is null
            ? null
            : AppVersionProvider.FormatVersion(LatestVersion);
}
