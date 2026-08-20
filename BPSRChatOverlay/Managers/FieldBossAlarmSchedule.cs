namespace BPSRChatOverlay.Managers;

internal sealed class FieldBossAlarmSchedule
{
    private static readonly TimeSpan CandidateCutoff =
        TimeSpan.FromMinutes(3);

    public DateTime? TargetTime { get; private set; }

    public void Advance(DateTime now)
    {
        DateTime firstCandidate = GetFirstCandidate(now);
        DateTime secondCandidate = firstCandidate.AddMinutes(30);

        TargetTime = TargetTime switch
        {
            null => firstCandidate,
            DateTime target when target == firstCandidate => secondCandidate,
            DateTime target when target == secondCandidate => null,
            _ => firstCandidate
        };
    }

    public void Clear()
    {
        TargetTime = null;
    }

    internal static DateTime GetFirstCandidate(DateTime now)
    {
        DateTime startOfHour = new(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            0,
            0,
            now.Kind);
        DateTime candidate = now.Minute < 30
            ? startOfHour.AddMinutes(30)
            : startOfHour.AddHours(1);

        return candidate - now <= CandidateCutoff
            ? candidate.AddMinutes(30)
            : candidate;
    }
}
