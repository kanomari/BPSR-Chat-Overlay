using BPSRChatOverlay.Models;
using BPSR_ZDPSLib;
using Google.Protobuf;
using Serilog;
using Zproto;

namespace BPSRChatOverlay.Managers;

public sealed class BuildStatusCaptureManager
{
    private const ulong WorldNotifyServiceId = (ulong)EServiceId.WorldNtf;
    private const uint SyncContainerDataMethodId = 0x15;
    private const uint SyncContainerDirtyDataMethodId = 0x16;
    private const uint WorldServiceId = 103198054;
    private const uint EquipProfessionMethodId = 0x31001;
    private const uint ActiveProfessionTalentMethodId = 0x3100E;
    private const uint ResetProfessionTalentBySingleNodeMethodId = 0x31016;
    private const uint SwitchProjectMethodId = 0x44002;
    private const uint EnableCultivateLineMethodId = 0x39007;
    private const uint DisableCultivateLineMethodId = 0x39008;
    private const int StandardCultivateSubTypeId = 800522;

    private static readonly IReadOnlyDictionary<int, string> TypeNames =
        new Dictionary<int, string>
        {
            [101] = "雷刃",
            [102] = "月影",
            [104] = "氷牙",
            [105] = "霜天",
            [107] = "烈風",
            [108] = "乱風",
            [110] = "威咲",
            [111] = "森癒",
            [113] = "剛身",
            [114] = "剛守",
            [116] = "狼弓",
            [117] = "鷹弓",
            [119] = "狂音",
            [120] = "響奏",
            [122] = "光砕",
            [123] = "光盾",
            [124] = "双炎",
            [125] = "炎舞"
        };

    private static readonly IReadOnlyDictionary<int, string> CultivateNames =
        new Dictionary<int, string>
        {
            [1] = "イマジンインパクト",
            [2] = "幻夢の断罪",
            [3] = "夢幻の矢",
            [4] = "夢幻迷界",
            [5] = "夢幻の力",
            [6] = "寂滅の夢",
            [7] = "虚妄断罪",
            [8] = "無限思考"
        };

    private static readonly IReadOnlyDictionary<int, int> KnownBranchNodes =
        new Dictionary<int, int>
        {
            [1126010] = 116,
            [1129002] = 117
        };

    private static readonly IReadOnlySet<int> KnownBaseTalentIds =
        new HashSet<int>
        {
            106,
            115
        };

    private readonly object _stateLock = new();
    private int? _professionId;
    private int? _talentId;
    private bool _typeUnselected;
    private int? _cultivateAreaId;
    private bool _cultivateKnown;
    private BuildStatusSnapshot? _lastPublished;

    public event Action<BuildStatusSnapshot>? StatusChanged;

    public static IReadOnlyDictionary<int, string> KnownTypeNames => TypeNames;

    public static IReadOnlyDictionary<int, string> KnownCultivateNames =>
        CultivateNames;

    public BuildStatusSnapshot Current
    {
        get
        {
            lock (_stateLock)
            {
                return CreateSnapshot();
            }
        }
    }

    public void Initialize(NetCap netCap)
    {
        netCap.RegisterNotifyHandler(
            WorldNotifyServiceId,
            SyncContainerDataMethodId,
            ProcessSyncContainerData);
        netCap.RegisterNotifyHandler(
            WorldNotifyServiceId,
            SyncContainerDirtyDataMethodId,
            ProcessSyncContainerDirtyData);
        netCap.RegisterProxyObserver(ProcessWorldCall);
    }

    private void ProcessSyncContainerData(
        ReadOnlySpan<byte> payload,
        ExtraPacketData extraData)
    {
        try
        {
            WorldNtf.Types.SyncContainerData message =
                WorldNtf.Types.SyncContainerData.Parser.ParseFrom(payload);
            CharSerialize? data = message.VData;
            if (data is null)
            {
                return;
            }

            int? professionId = data.ProfessionList?.CurProfessionId;
            int? talentId = null;
            if (professionId is { } currentProfession &&
                data.ProfessionList?.TalentList.TryGetValue(
                    currentProfession,
                    out ProfessionTalentInfo? talent) == true &&
                talent.TalentStageCfgId != 0)
            {
                talentId = talent.TalentStageCfgId;
            }

            int? cultivateAreaId = null;
            bool cultivateKnown = false;
            int seasonId = data.SeasonCenter?.SeasonId ?? 0;
            if (seasonId != 0 &&
                data.SeasonCultivateLineData?.SeasonCultivateLineMap.TryGetValue(
                    seasonId,
                    out CultivateLineData? season) == true &&
                season.CultivateLineMap.TryGetValue(
                    StandardCultivateSubTypeId,
                    out CultivateLineSubTypeData? standard) == true)
            {
                cultivateKnown = true;
                cultivateAreaId = standard.CultivateLineAreaList
                    .FirstOrDefault(id => id is >= 1 and <= 8);
                if (cultivateAreaId == 0)
                {
                    cultivateAreaId = null;
                }
            }

            UpdateState(
                professionId,
                talentId,
                cultivateKnown,
                cultivateAreaId);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Warning(ex, "Failed to parse the initial build status snapshot");
        }
    }

    private void ProcessSyncContainerDirtyData(
        ReadOnlySpan<byte> payload,
        ExtraPacketData extraData)
    {
        try
        {
            WorldNtfCsharp.Types.SyncContainerDirtyData message =
                WorldNtfCsharp.Types.SyncContainerDirtyData.Parser.ParseFrom(payload);
            if (message.VData?.Buffer is not { } buffer)
            {
                return;
            }

            int? talentId = FindTypeTalentId(buffer.Span);
            if (talentId.HasValue)
            {
                UpdateTalent(talentId.Value);
            }
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Warning(ex, "Failed to parse a build status delta");
        }
    }

    private void ProcessWorldCall(
        ulong serviceId,
        uint subId,
        uint methodId,
        uint returnUid,
        ReadOnlySpan<byte> payload,
        ExtraPacketData extraData)
    {
        if (serviceId != WorldServiceId)
        {
            return;
        }

        try
        {
            switch (methodId)
            {
                case EquipProfessionMethodId:
                    World.Types.EquipProfession equip =
                        World.Types.EquipProfession.Parser.ParseFrom(payload);
                    if (equip.VInfo is not null)
                    {
                        UpdateProfession(equip.VInfo.ProfessionId);
                    }
                    break;

                case ActiveProfessionTalentMethodId:
                    World.Types.ActiveProfessionTalent active =
                        World.Types.ActiveProfessionTalent.Parser.ParseFrom(payload);
                    if (active.VRequest is not null)
                    {
                        UpdateProfession(active.VRequest.ProfessionId, clearTalent: false);
                        foreach (int nodeId in active.VRequest.TalentNodeIds)
                        {
                            if (KnownBranchNodes.TryGetValue(nodeId, out int talentId))
                            {
                                UpdateTalent(talentId);
                            }
                        }
                    }
                    break;

                case ResetProfessionTalentBySingleNodeMethodId:
                    World.Types.ResetProfessionTalentBySingleNode reset =
                        World.Types.ResetProfessionTalentBySingleNode.Parser.ParseFrom(payload);
                    if (reset.VRequest is not null &&
                        KnownBranchNodes.ContainsKey(reset.VRequest.TalentNodeId))
                    {
                        UpdateTypeUnselected(reset.VRequest.ProfessionId);
                    }
                    break;

                case SwitchProjectMethodId:
                    ClearTalent();
                    break;

                case EnableCultivateLineMethodId:
                    World.Types.EnableCultivateLine enable =
                        World.Types.EnableCultivateLine.Parser.ParseFrom(payload);
                    if (enable.VRequest?.ZoneId is >= 1 and <= 8)
                    {
                        UpdateCultivate(enable.VRequest.ZoneId);
                    }
                    break;

                case DisableCultivateLineMethodId:
                    World.Types.DisableCultivateLine disable =
                        World.Types.DisableCultivateLine.Parser.ParseFrom(payload);
                    if (disable.VRequest?.ZoneId is >= 1 and <= 8)
                    {
                        UpdateCultivate(null);
                    }
                    break;
            }
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Warning(
                ex,
                "Failed to parse a build status call. MethodId: {MethodId}",
                methodId);
        }
    }

    private void UpdateState(
        int? professionId,
        int? talentId,
        bool cultivateKnown,
        int? cultivateAreaId)
    {
        BuildStatusSnapshot snapshot;
        lock (_stateLock)
        {
            _professionId = professionId;
            _talentId = talentId is { } id && TypeNames.ContainsKey(id)
                ? id
                : null;
            _typeUnselected = talentId is { } stageId &&
                              KnownBaseTalentIds.Contains(stageId);
            _cultivateKnown = cultivateKnown;
            _cultivateAreaId = cultivateAreaId;
            snapshot = CreateSnapshot();
        }

        Publish(snapshot);
    }

    private void UpdateProfession(int professionId, bool clearTalent = true)
    {
        BuildStatusSnapshot snapshot;
        lock (_stateLock)
        {
            _professionId = professionId;
            if (clearTalent)
            {
                _talentId = null;
                _typeUnselected = false;
            }
            snapshot = CreateSnapshot();
        }

        Publish(snapshot);
    }

    private void UpdateTalent(int talentId)
    {
        if (!TypeNames.ContainsKey(talentId))
        {
            return;
        }

        BuildStatusSnapshot snapshot;
        lock (_stateLock)
        {
            _talentId = talentId;
            _typeUnselected = false;
            snapshot = CreateSnapshot();
        }

        Publish(snapshot);
    }

    private void ClearTalent()
    {
        BuildStatusSnapshot snapshot;
        lock (_stateLock)
        {
            _talentId = null;
            _typeUnselected = false;
            snapshot = CreateSnapshot();
        }

        Publish(snapshot);
    }

    private void UpdateTypeUnselected(int professionId)
    {
        BuildStatusSnapshot snapshot;
        lock (_stateLock)
        {
            _professionId = professionId;
            _talentId = null;
            _typeUnselected = true;
            snapshot = CreateSnapshot();
        }

        Publish(snapshot);
    }

    private void UpdateCultivate(int? areaId)
    {
        BuildStatusSnapshot snapshot;
        lock (_stateLock)
        {
            _cultivateKnown = true;
            _cultivateAreaId = areaId;
            snapshot = CreateSnapshot();
        }

        Publish(snapshot);
    }

    private BuildStatusSnapshot CreateSnapshot()
    {
        string? typeName = _talentId is { } talent &&
                           TypeNames.TryGetValue(talent, out string? name)
            ? name
            : null;
        string? cultivateName = _cultivateKnown &&
                                _cultivateAreaId is { } area &&
                                CultivateNames.TryGetValue(
                                    area,
                                    out string? knownCultivateName)
            ? knownCultivateName
            : null;

        return new BuildStatusSnapshot(
            _professionId,
            _talentId,
            typeName,
            _typeUnselected,
            _cultivateAreaId,
            cultivateName,
            _cultivateKnown && !_cultivateAreaId.HasValue);
    }

    private void Publish(BuildStatusSnapshot snapshot)
    {
        try
        {
            lock (_stateLock)
            {
                if (snapshot == _lastPublished)
                {
                    return;
                }

                _lastPublished = snapshot;
            }

            Log.Information(
                "Build status updated: ProfessionId={ProfessionId}, TalentId={TalentId}, TypeName={TypeName}, CultivateAreaId={CultivateAreaId}, CultivateDisabled={CultivateDisabled}",
                snapshot.ProfessionId,
                snapshot.TalentId,
                snapshot.TypeName,
                snapshot.CultivateAreaId,
                snapshot.IsCultivateDisabled);
            StatusChanged?.Invoke(snapshot);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Warning(ex, "Failed to publish a build status update");
        }
    }

    private static int? FindTypeTalentId(ReadOnlySpan<byte> buffer)
    {
        int? result = null;
        for (int index = 0; index <= buffer.Length - 8; index++)
        {
            if (buffer[index] != 0x04 ||
                buffer[index + 1] != 0 ||
                buffer[index + 2] != 0 ||
                buffer[index + 3] != 0)
            {
                continue;
            }

            int value = BitConverter.ToInt32(buffer.Slice(index + 4, 4));
            if (TypeNames.ContainsKey(value) &&
                HasProfessionContainerMarker(buffer, index))
            {
                result = value;
            }
        }

        return result;
    }

    private static bool HasProfessionContainerMarker(
        ReadOnlySpan<byte> buffer,
        int valueOffset)
    {
        int start = Math.Max(0, valueOffset - 64);
        for (int index = valueOffset - 1; index >= start; index--)
        {
            if (index + 4 <= buffer.Length &&
                buffer[index] == 0x3D &&
                buffer[index + 1] == 0 &&
                buffer[index + 2] == 0 &&
                buffer[index + 3] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRecoverableException(Exception exception)
    {
        return exception is not (
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException);
    }
}
