namespace BPSRChatOverlay.Models;

public sealed record CaptureDeviceOption(
    string Name,
    string? FriendlyName,
    string? Description,
    string DisplayName);
