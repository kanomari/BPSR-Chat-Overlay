namespace BPSR_ZDPSLib;

public enum CaptureDeviceSelectionReason
{
    SavedNameMatch,
    SavedDescriptionMatch,
    ConfigurationEmptyEthernet,
    ConfigurationEmptyFirstDevice,
    ConfiguredDeviceMissingEthernet,
    ConfiguredDeviceMissingFirstDevice
}

public sealed record CaptureDeviceSelectionInfo(
    string ActualDeviceName,
    string? FriendlyName,
    string? Description,
    string DisplayName,
    CaptureDeviceSelectionReason SelectionReason,
    bool WasFallback,
    bool ConfiguredDeviceMissing,
    bool WasConfigurationEmpty,
    string? ConfiguredDeviceName)
{
    public string SelectionReasonText => SelectionReason switch
    {
        CaptureDeviceSelectionReason.SavedNameMatch =>
            "保存されたNameに一致",
        CaptureDeviceSelectionReason.SavedDescriptionMatch =>
            "保存されたDescriptionに一致",
        CaptureDeviceSelectionReason.ConfigurationEmptyEthernet =>
            "ネットワークカード未指定のためEthernetを自動選択",
        CaptureDeviceSelectionReason.ConfigurationEmptyFirstDevice =>
            "ネットワークカード未指定のため先頭デバイスを自動選択",
        CaptureDeviceSelectionReason.ConfiguredDeviceMissingEthernet =>
            "保存済みNICが見つからないためEthernetへフォールバック",
        CaptureDeviceSelectionReason.ConfiguredDeviceMissingFirstDevice =>
            "保存済みNICが見つからないため先頭デバイスへフォールバック",
        _ => SelectionReason.ToString()
    };
}
