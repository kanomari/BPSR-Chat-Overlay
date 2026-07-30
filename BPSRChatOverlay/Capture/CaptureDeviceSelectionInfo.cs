namespace BPSR_ZDPSLib;

public enum CaptureDeviceSelectionReason
{
    SavedNameMatch,
    SavedDescriptionMatch,
    GameConnectionLocalAddress,
    WindowsBestRoute,
    ActiveGatewayInterface,
    FirstEnumeratedDevice
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
    string? ConfiguredDeviceName,
    string? GameConnectionLocalAddress,
    int? WindowsBestRouteInterfaceIndex)
{
    public string SelectionReasonText => SelectionReason switch
    {
        CaptureDeviceSelectionReason.SavedNameMatch =>
            "保存されたNameに一致",
        CaptureDeviceSelectionReason.SavedDescriptionMatch =>
            "保存されたDescriptionに一致",
        CaptureDeviceSelectionReason.GameConnectionLocalAddress =>
            "ゲーム接続のローカルIPに一致",
        CaptureDeviceSelectionReason.WindowsBestRoute =>
            "Windowsの最適経路に一致",
        CaptureDeviceSelectionReason.ActiveGatewayInterface =>
            "通信可能なアクティブNICに一致",
        CaptureDeviceSelectionReason.FirstEnumeratedDevice =>
            "Npcap列挙の先頭デバイスへフォールバック",
        _ => SelectionReason.ToString()
    };
}
