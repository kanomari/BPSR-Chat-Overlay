namespace BPSRChatOverlay.Config;

public sealed class BuildStatusRegistration
{
    public int TalentId { get; set; }

    public int CultivateAreaId { get; set; }

    public BuildStatusRegistration Clone()
    {
        return new BuildStatusRegistration
        {
            TalentId = TalentId,
            CultivateAreaId = CultivateAreaId
        };
    }
}
