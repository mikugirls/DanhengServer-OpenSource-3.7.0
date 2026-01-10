using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using SqlSugar;

namespace EggLink.DanhengServer.Database.Friend;

[SugarTable("friend_record_data")]
public class FriendRecordData : BaseDatabaseDataHelper
{
    [SugarColumn(IsJson = true)]
    public List<FriendDevelopmentInfoPb> DevelopmentInfos { get; set; } = []; // max 20 entries

    [SugarColumn(IsJson = true)]
    public Dictionary<uint, ChallengeGroupStatisticsPb> ChallengeGroupStatistics { get; set; } =
        []; // cur group statistics

    public uint NextRecordId { get; set; }

    public void AddAndRemoveOld(FriendDevelopmentInfoPb info)
    {
        // get same type
        var same = DevelopmentInfos.Where(x => x.DevelopmentType == info.DevelopmentType);

        // if param equal remove
        foreach (var infoPb in same.ToArray())
            // ReSharper disable once UsageOfDefaultStructEquality
            if (infoPb.Params.SequenceEqual(info.Params))
                // remove
                DevelopmentInfos.Remove(infoPb);

        DevelopmentInfos.Add(info);
    }
}

public class FriendDevelopmentInfoPb
{
    public DevelopmentType DevelopmentType { get; set; }
    public long Time { get; set; } = Extensions.GetUnixSec();
    public Dictionary<string, uint> Params { get; set; } = [];

    public FriendDevelopmentInfo ToProto()
    {
        var proto = new FriendDevelopmentInfo
        {
            Time = Time,
            DevelopmentType = DevelopmentType
        };

        switch (DevelopmentType)
        {
            case DevelopmentType.DevelopmentNone:
            case DevelopmentType.DevelopmentActivityStart:
            case DevelopmentType.DevelopmentActivityEnd:
            case DevelopmentType.DevelopmentRogueMagic:
                break;
            case DevelopmentType.DevelopmentRogueCosmos:
            case DevelopmentType.DevelopmentRogueChessNous:
            case DevelopmentType.DevelopmentRogueChess:
                proto.RogueDevelopmentInfo = new FriendRogueDevelopmentInfo
                {
                    AreaId = Params.GetValueOrDefault("AreaId", 0u)
                };
                break;
            case DevelopmentType.DevelopmentMemoryChallenge:
            case DevelopmentType.DevelopmentStoryChallenge:
            case DevelopmentType.DevelopmentBossChallenge:
                proto.ChallengeDevelopmentInfo = new FriendChallengeDevelopmentInfo
                {
                    ChallengeId = Params.GetValueOrDefault("ChallengeId", 0u)
                };
                break;
            case DevelopmentType.DevelopmentUnlockAvatar:
                proto.AvatarId = Params.GetValueOrDefault("AvatarId", 0u);
                break;
            case DevelopmentType.DevelopmentUnlockEquipment:
                proto.EquipmentTid = Params.GetValueOrDefault("EquipmentTid", 0u);
                break;
            case DevelopmentType.DevelopmentRogueTourn:
            case DevelopmentType.DevelopmentRogueTournWeek:
            case DevelopmentType.DevelopmentRogueTournDivision:
                proto.RogueTournDevelopmentInfo = new FriendRogueTournDevelopmentInfo
                {
                    AreaId = Params.GetValueOrDefault("AreaId", 0u),
                    FinishTournDifficulty = Params.GetValueOrDefault("FinishTournDifficulty", 0u)
                };
                break;
            case DevelopmentType.DevelopmentChallengePeak:
                proto.ChallengePeakDevelopmentInfo = new FriendChallengePeakDevelopmentInfo
                {
                    PeakLevelId = Params.GetValueOrDefault("PeakLevelId", 0u)
                };
                break;
        }

        return proto;
    }
}

public class ChallengeGroupStatisticsPb
{
    public uint GroupId { get; set; }
    public Dictionary<uint, MemoryGroupStatisticsPb>? MemoryGroupStatistics { get; set; }
    public Dictionary<uint, StoryGroupStatisticsPb>? StoryGroupStatistics { get; set; }
    public Dictionary<uint, BossGroupStatisticsPb>? BossGroupStatistics { get; set; }

    public ChallengeGroupStatistics ToProto()
    {
        var proto = new ChallengeGroupStatistics
        {
            GroupId = GroupId
        };

        if (MemoryGroupStatistics != null)
        {
            foreach (var memoryGroupStatistic in MemoryGroupStatistics.Values)
                proto.GroupTotalStars += memoryGroupStatistic.Stars;

            var maxFloor = MemoryGroupStatistics.Values.MaxBy(x => x.Level);
            if (maxFloor != null) proto.MemoryGroup = maxFloor.ToProto();
        }

        if (StoryGroupStatistics != null)
        {
            foreach (var storyGroupStatistic in StoryGroupStatistics.Values)
                proto.GroupTotalStars += storyGroupStatistic.Stars;

            var maxFloor = StoryGroupStatistics.Values.MaxBy(x => x.Level);
            if (maxFloor != null) proto.StoryGroup = maxFloor.ToProto();
        }

        if (BossGroupStatistics != null)
        {
            foreach (var bossGroupStatistic in BossGroupStatistics.Values)
                proto.GroupTotalStars += bossGroupStatistic.Stars;

            var maxFloor = BossGroupStatistics.Values.MaxBy(x => x.Level);
            if (maxFloor != null) proto.BossGroup = maxFloor.ToProto();
        }

        return proto;
    }
}

public class MemoryGroupStatisticsPb
{
    public uint RecordId { get; set; }
    public uint Level { get; set; }
    public uint RoundCount { get; set; }
    public uint Stars { get; set; }
    public List<List<ChallengeAvatarInfoPb>> Lineups { get; set; } = [];

    public MemoryGroupStatistics ToProto()
    {
        return new MemoryGroupStatistics
        {
            RecordId = RecordId,
            SttInfo = new MemoryStatisticsInfo
            {
                CurLevelStars = Stars,
                Level = Level,
                RoundCount = RoundCount,
                LineupList =
                {
                    Lineups.Select(x => new ChallengeLineupList
                    {
                        AvatarList = { x.Select(avatar => avatar.ToProto()) }
                    })
                }
            }
        };
    }
}

public class StoryGroupStatisticsPb
{
    public uint RecordId { get; set; }
    public uint Level { get; set; }
    public uint Score { get; set; }
    public uint BuffOne { get; set; }
    public uint BuffTwo { get; set; }
    public uint Stars { get; set; }
    public List<List<ChallengeAvatarInfoPb>> Lineups { get; set; } = [];

    public StoryGroupStatistics ToProto()
    {
        var stt = new StoryStatisticsInfo
        {
            CurLevelStars = Stars,
            Level = Level,
            BuffOne = BuffOne,
            BuffTwo = BuffTwo,
            ScoreId = Score
        };

        // --- 核心修正：只创建一个容器 ---
        var singleContainer = new ChallengeLineupList();

        // 遍历所有队伍（第一队和第二队）
        foreach (var team in Lineups)
        {
            foreach (var av in team)
            {
                // 将所有 8 个人全部加进这唯一一个容器的 AvatarList 中
                // 这里会调用 av.ToProto()，它会带上正确的 Index (0-7)
                singleContainer.AvatarList.Add(av.ToProto());
            }
        }

        // 把这包含 8 个人的唯一容器加进去
        stt.LineupList.Add(singleContainer);

        return new StoryGroupStatistics
        {
            RecordId = RecordId,
            SttInfo = stt
        };
    }
}

public class BossGroupStatisticsPb
{
    public uint RecordId { get; set; }
    public uint Level { get; set; }
    public uint Score { get; set; }
    public uint BuffOne { get; set; }
    public uint BuffTwo { get; set; }
    public uint Stars { get; set; }
    public List<List<ChallengeAvatarInfoPb>> Lineups { get; set; } = [];

    public BossGroupStatistics ToProto()
    {
        return new BossGroupStatistics
        {
            RecordId = RecordId,
            SttInfo = new BossStatisticsInfo
            {
                CurLevelStars = Stars,
                Level = Level,
                LineupList =
                {
                    Lineups.Select(x => new ChallengeLineupList
                    {
                        AvatarList = { x.Select(avatar => avatar.ToProto()) }
                    })
                },
                BuffOne = BuffOne,
                BuffTwo = BuffTwo,
                ScoreId = Score
            }
        };
    }
}

public class ChallengeAvatarInfoPb
{
    public uint Level { get; set; }
    public uint Index { get; set; }
    public uint Id { get; set; }
    public AvatarType AvatarType { get; set; } = AvatarType.AvatarFormalType;
    public uint Rank { get; set;} // <--- 添加这一行
    public ChallengeAvatarInfo ToProto()
    {
        return new ChallengeAvatarInfo
        {
            Level = Level,
            AvatarType = AvatarType,
            Id = Id,
            Index = Index,
			GGDIIBCDOBB = Rank // 对应星魂
        };
    }
}