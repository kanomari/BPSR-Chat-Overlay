namespace BPSRChatOverlay.Models;

public sealed record BuildStatusSnapshot(
    int? ProfessionId,
    int? TalentId,
    string? TypeName,
    bool IsTypeUnselected,
    int? CultivateAreaId,
    string? CultivateName,
    bool IsCultivateDisabled);
