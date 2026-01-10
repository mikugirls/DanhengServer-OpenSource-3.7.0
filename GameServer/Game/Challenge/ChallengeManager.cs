using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Challenge;
using EggLink.DanhengServer.Database.Friend;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.GameServer.Game.Challenge.Definitions;
using EggLink.DanhengServer.GameServer.Game.Challenge.Instances;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Challenge;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Proto.ServerSide;
using EggLink.DanhengServer.Util;
using Google.Protobuf;
using static EggLink.DanhengServer.GameServer.Plugin.Event.PluginEvent;

namespace EggLink.DanhengServer.GameServer.Game.Challenge;

public class ChallengeManager(PlayerInstance player) : BasePlayerManager(player)
{
    #region Properties

    public BaseChallengeInstance? ChallengeInstance { get; set; }

    public ChallengeData ChallengeData { get; } =
        DatabaseHelper.Instance!.GetInstanceOrCreateNew<ChallengeData>(player.Uid);

    #endregion

    #region Management

    public async ValueTask StartChallenge(int challengeId, ChallengeStoryBuffInfo? storyBuffs,
        ChallengeBossBuffInfo? bossBuffs)
    {
        if (!GameData.ChallengeConfigData.TryGetValue(challengeId, out var excel))
        {
            await Player.SendPacket(new PacketStartChallengeScRsp((uint)Retcode.RetChallengeNotExist));
            return;
        }

        if (excel.StageNum > 0)
        {
            var lineup = Player.LineupManager!.GetExtraLineup(ExtraLineupType.LineupChallenge)!;
            if (lineup.AvatarData!.FormalAvatars.Count == 0)
            {
                await Player.SendPacket(new PacketStartChallengeScRsp((uint)Retcode.RetChallengeLineupEmpty));
                return;
            }

            foreach (var avatar in lineup.AvatarData!.FormalAvatars)
            {
                avatar.SetCurHp(10000, true);
                avatar.SetCurSp(5000, true);
            }
            lineup.Mp = 8;
        }

        if (excel.StageNum >= 2)
        {
            var lineup = Player.LineupManager!.GetExtraLineup(ExtraLineupType.LineupChallenge2)!;
            if (lineup.AvatarData!.FormalAvatars.Count == 0)
            {
                await Player.SendPacket(new PacketStartChallengeScRsp((uint)Retcode.RetChallengeLineupEmpty));
                return;
            }

            foreach (var avatar in lineup.AvatarData!.FormalAvatars)
            {
                avatar.SetCurHp(10000, true);
                avatar.SetCurSp(5000, true);
            }
            lineup.Mp = 8;
        }

        var data = new ChallengeDataPb();
        BaseLegacyChallengeInstance instance;

        if (excel.IsBoss())
        {
            data.Boss = new ChallengeBossDataPb
            {
                ChallengeMazeId = (uint)excel.ID,
                CurStatus = 1,
                CurrentStage = 1,
                CurrentExtraLineup = ChallengeLineupTypePb.Challenge1
            };
            instance = new ChallengeBossInstance(Player, data);
        }
        else if (excel.IsStory())
        {
            data.Story = new ChallengeStoryDataPb
            {
                ChallengeMazeId = (uint)excel.ID,
                CurStatus = 1,
                CurrentStage = 1,
                CurrentExtraLineup = ChallengeLineupTypePb.Challenge1
            };
            instance = new ChallengeStoryInstance(Player, data);
        }
        else
        {
            data.Memory = new ChallengeMemoryDataPb
            {
                ChallengeMazeId = (uint)excel.ID,
                CurStatus = 1,
                CurrentStage = 1,
                CurrentExtraLineup = ChallengeLineupTypePb.Challenge1,
                RoundsLeft = (uint)excel.ChallengeCountDown
            };
            instance = new ChallengeMemoryInstance(Player, data);
        }

        ChallengeInstance = instance;
        await Player.LineupManager!.SetExtraLineup((ExtraLineupType)instance.GetCurrentExtraLineupType());

        try
        {
            await Player.EnterScene(excel.MapEntranceID, 0, false);
        }
        catch
        {
            ChallengeInstance = null;
            await Player.SendPacket(new PacketStartChallengeScRsp((uint)Retcode.RetChallengeNotExist));
            return;
        }

        instance.SetStartPos(Player.Data.Pos!);
        instance.SetStartRot(Player.Data.Rot!);
        instance.SetSavedMp(Player.LineupManager.GetCurLineup()!.Mp);

        if (excel.IsStory() && storyBuffs != null)
        {
            instance.Data.Story.Buffs.Add(storyBuffs.BuffOne);
            instance.Data.Story.Buffs.Add(storyBuffs.BuffTwo);
        }

        if (bossBuffs != null)
        {
            instance.Data.Boss.Buffs.Add(bossBuffs.BuffOne);
            instance.Data.Boss.Buffs.Add(bossBuffs.BuffTwo);
        }

        InvokeOnPlayerEnterChallenge(Player, instance);
        await Player.SendPacket(new PacketStartChallengeScRsp(Player));
        SaveInstance(instance);
    }

    public void AddHistory(int challengeId, int stars, int score)
    {
        if (stars <= 0) return;
        if (!ChallengeData.History.ContainsKey(challengeId))
            ChallengeData.History[challengeId] = new ChallengeHistoryData(Player.Uid, challengeId);
        
        var info = ChallengeData.History[challengeId];
        info.SetStars(stars);
        info.Score = score;
    }

    public async ValueTask<List<TakenChallengeRewardInfo>?> TakeRewards(int groupId)
    {
        if (!GameData.ChallengeGroupData.TryGetValue(groupId, out var challengeGroup)) return null;
        if (!GameData.ChallengeRewardData.TryGetValue(challengeGroup.RewardLineGroupID, out var challengeRewardLine)) return null;

        var totalStars = 0;
        foreach (var ch in ChallengeData.History.Values)
        {
            if (ch.GroupId == 0)
            {
                if (!GameData.ChallengeConfigData.TryGetValue(ch.ChallengeId, out var challengeExcel)) continue;
                ch.GroupId = challengeExcel.GroupID;
            }
            if (ch.GroupId == groupId) totalStars += ch.GetTotalStars();
        }

        var rewardInfos = new List<TakenChallengeRewardInfo>();
        var data = new List<ItemData>();

        foreach (var challengeReward in challengeRewardLine)
        {
            if (totalStars < challengeReward.StarCount) continue;
            if (!ChallengeData.TakenRewards.ContainsKey(groupId))
                ChallengeData.TakenRewards[groupId] = new ChallengeGroupReward(Player.Uid, groupId);
            
            var reward = ChallengeData.TakenRewards[groupId];
            if (reward.HasTakenReward(challengeReward.StarCount)) continue;

            reward.SetTakenReward(challengeReward.StarCount);
            if (!GameData.RewardDataData.TryGetValue(challengeReward.RewardID, out var rewardExcel)) continue;

            var proto = new TakenChallengeRewardInfo
            {
                StarCount = (uint)challengeReward.StarCount,
                Reward = new ItemList()
            };

            foreach (var item in rewardExcel.GetItems())
            {
                var itemData = new ItemData { ItemId = item.Item1, Count = item.Item2 };
                proto.Reward.ItemList_.Add(itemData.ToProto());
                data.Add(itemData);
            }
            rewardInfos.Add(proto);
        }

        await Player.InventoryManager!.AddItems(data);
        return rewardInfos;
    }

    public void SaveInstance(BaseChallengeInstance instance)
    {
        ChallengeData.ChallengeInstance = Convert.ToBase64String(instance.Data.ToByteArray());
    }

    public void ClearInstance()
    {
        ChallengeData.ChallengeInstance = null;
        ChallengeInstance = null;
    }

    public void ResurrectInstance()
    {
        if (ChallengeData.ChallengeInstance == null) return;
        var protoByte = Convert.FromBase64String(ChallengeData.ChallengeInstance);
        var proto = ChallengeDataPb.Parser.ParseFrom(protoByte);

        if (proto != null)
            ChallengeInstance = proto.ChallengeTypeCase switch
            {
                ChallengeDataPb.ChallengeTypeOneofCase.Memory => new ChallengeMemoryInstance(Player, proto),
                ChallengeDataPb.ChallengeTypeOneofCase.Peak => new ChallengePeakInstance(Player, proto),
                ChallengeDataPb.ChallengeTypeOneofCase.Story => new ChallengeStoryInstance(Player, proto),
                ChallengeDataPb.ChallengeTypeOneofCase.Boss => new ChallengeBossInstance(Player, proto),
                _ => null
            };
    }

    public void SaveBattleRecord(BaseLegacyChallengeInstance inst)
    {
        // 先尝试通过常规 Switch 处理已知类型
        switch (inst)
        {
          case ChallengeMemoryInstance memory:
{
    Player.FriendRecordData!.ChallengeGroupStatistics.TryAdd((uint)memory.Config.GroupID, new ChallengeGroupStatisticsPb { GroupId = (uint)memory.Config.GroupID });
    var stats = Player.FriendRecordData.ChallengeGroupStatistics[(uint)memory.Config.GroupID];
    stats.MemoryGroupStatistics ??= [];

    // 计算本次战斗星数
    var starCount = 0u;
    for (var i = 0; i < 3; i++) starCount += (memory.Data.Memory.Stars & (1 << i)) != 0 ? 1u : 0u;

    // 计算本次消耗轮数 (总上限 - 剩余)
    var newRoundCount = (uint)(memory.Config.ChallengeCountDown - memory.Data.Memory.RoundsLeft);

    // 获取旧记录进行对比
    if (stats.MemoryGroupStatistics.TryGetValue((uint)memory.Config.ID, out var existing))
    {
        // 逻辑判断：
        // 1. 如果旧记录星数更高，绝对不更新
        if (existing.Stars > starCount) return;

        // 2. 如果星数相等
        if (existing.Stars == starCount)
        {
            // 如果不是3星，且轮数没有进步（新轮数 >= 旧轮数），则不更新
            // 但如果是3星，我们跳过这个判断，强制进入后面的更新流程（刷新数据/阵容）
            if (starCount < 3 && newRoundCount >= existing.RoundCount) return;
        }
    }

    // 构造新战报
    var pb = new MemoryGroupStatisticsPb
    {
        RoundCount = newRoundCount,
        Stars = starCount,
        RecordId = Player.FriendRecordData!.NextRecordId++,
        Level = memory.Config.Floor
    };

    // 抓取阵容逻辑...
    foreach (var type in new[] { ExtraLineupType.LineupChallenge, ExtraLineupType.LineupChallenge2 })
    {
        if (type == ExtraLineupType.LineupChallenge2 && memory.Config.StageNum < 2) continue;
        var lineup = Player.LineupManager!.GetExtraLineup(type);
        if (lineup?.BaseAvatars == null) continue;

        var lineupPb = new List<ChallengeAvatarInfoPb>();
        uint idx = 0;
        foreach (var avatar in lineup.BaseAvatars)
        {
            var formal = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
            if (formal == null) continue;
            lineupPb.Add(new ChallengeAvatarInfoPb { Index = idx++, Id = (uint)formal.BaseAvatarId, AvatarType = AvatarType.AvatarFormalType, Level = (uint)formal.Level });
        }
        pb.Lineups.Add(lineupPb);
    }

    // 更新到内存并持久化到数据库
    stats.MemoryGroupStatistics[(uint)memory.Config.ID] = pb;
    
    return;
}

            case ChallengeStoryInstance story:
            {
                Player.FriendRecordData!.ChallengeGroupStatistics.TryAdd((uint)story.Config.GroupID, new ChallengeGroupStatisticsPb { GroupId = (uint)story.Config.GroupID });
                var stats = Player.FriendRecordData.ChallengeGroupStatistics[(uint)story.Config.GroupID];
                stats.StoryGroupStatistics ??= [];

                var starCount = 0u;
                for (var i = 0; i < 3; i++) starCount += (story.Data.Story.Stars & (1 << i)) != 0 ? 1u : 0u;

                if (stats.StoryGroupStatistics.GetValueOrDefault((uint)story.Config.ID)?.Stars > starCount) return;

                var pb = new StoryGroupStatisticsPb
                {
                    Stars = starCount,
                    RecordId = Player.FriendRecordData!.NextRecordId++,
                    Level = story.Config.Floor,
                    BuffOne = story.Data.Story.Buffs.Count > 0 ? story.Data.Story.Buffs[0] : 0,
                    BuffTwo = story.Data.Story.Buffs.Count > 1 ? story.Data.Story.Buffs[1] : 0,
                    Score = (uint)story.GetTotalScore()
                };

                foreach (var type in new[] { ExtraLineupType.LineupChallenge, ExtraLineupType.LineupChallenge2 })
                {
                    if (type == ExtraLineupType.LineupChallenge2 && story.Config.StageNum < 2) continue;
                    var lineup = Player.LineupManager!.GetExtraLineup(type);
                    if (lineup?.BaseAvatars == null) continue;

                    var lineupPb = new List<ChallengeAvatarInfoPb>();
                    uint idx = 0;
                    foreach (var avatar in lineup.BaseAvatars)
                    {
                        var formal = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
                        if (formal == null) continue;
                        lineupPb.Add(new ChallengeAvatarInfoPb { Index = idx++, Id = (uint)formal.BaseAvatarId, AvatarType = AvatarType.AvatarFormalType, Level = (uint)formal.Level });
                    }
                    pb.Lineups.Add(lineupPb);
                }
                stats.StoryGroupStatistics[(uint)story.Config.ID] = pb;
				
                return;
            }

            case ChallengeBossInstance boss:
            {
                Player.FriendRecordData!.ChallengeGroupStatistics.TryAdd((uint)boss.Config.GroupID, new ChallengeGroupStatisticsPb { GroupId = (uint)boss.Config.GroupID });
                var stats = Player.FriendRecordData.ChallengeGroupStatistics[(uint)boss.Config.GroupID];
                stats.BossGroupStatistics ??= [];

                var starCount = 0u;
                for (var i = 0; i < 3; i++) starCount += (boss.Data.Boss.Stars & (1 << i)) != 0 ? 1u : 0u;

                if (stats.BossGroupStatistics.GetValueOrDefault((uint)boss.Config.ID)?.Stars > starCount) return;

                var pb = new BossGroupStatisticsPb
                {
                    Stars = starCount,
                    RecordId = Player.FriendRecordData!.NextRecordId++,
                    Level = boss.Config.Floor,
                    BuffOne = boss.Data.Boss.Buffs.Count > 0 ? boss.Data.Boss.Buffs[0] : 0,
                    BuffTwo = boss.Data.Boss.Buffs.Count > 1 ? boss.Data.Boss.Buffs[1] : 0,
                    Score = (uint)boss.GetTotalScore()
                };

                foreach (var type in new[] { ExtraLineupType.LineupChallenge, ExtraLineupType.LineupChallenge2 })
                {
                    if (type == ExtraLineupType.LineupChallenge2 && boss.Config.StageNum < 2) continue;
                    var lineup = Player.LineupManager!.GetExtraLineup(type);
                    if (lineup?.BaseAvatars == null) continue;

                    var lineupPb = new List<ChallengeAvatarInfoPb>();
                    uint idx = 0;
                    foreach (var avatar in lineup.BaseAvatars)
                    {
                        var formal = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
                        if (formal == null) continue;
                        lineupPb.Add(new ChallengeAvatarInfoPb { Index = idx++, Id = (uint)formal.BaseAvatarId, AvatarType = AvatarType.AvatarFormalType, Level = (uint)formal.Level });
                    }
                    pb.Lineups.Add(lineupPb);
                }
                stats.BossGroupStatistics[(uint)boss.Config.ID] = pb;
				
                return;
            }
        }

        // 处理特殊 PeakInstance 类型
        object obj = inst;
        if (obj.GetType().Name == "ChallengePeakInstance")
        {
            dynamic peak = obj;
            uint groupId = (uint)peak.Data.Peak.CurrentPeakGroupId;
            Player.FriendRecordData!.ChallengeGroupStatistics.TryAdd(groupId, new ChallengeGroupStatisticsPb { GroupId = groupId });
            var stats = Player.FriendRecordData.ChallengeGroupStatistics[groupId];
            stats.StoryGroupStatistics ??= [];

            uint levelId = (uint)peak.Data.Peak.CurrentPeakLevelId;
            uint starCount = (uint)peak.Data.Peak.Stars;

            if (stats.StoryGroupStatistics.GetValueOrDefault(levelId)?.Stars > starCount) return;

            var pb = new StoryGroupStatisticsPb
            {
                Stars = starCount,
                RecordId = Player.FriendRecordData!.NextRecordId++,
                Level = (uint)peak.Config.ID,
                BuffOne = peak.Data.Peak.Buffs.Count > 0 ? (uint)peak.Data.Peak.Buffs[0] : 0,
                Score = 0
            };

            foreach (var type in new[] { ExtraLineupType.LineupChallenge, ExtraLineupType.LineupChallenge2 })
            {
                var lineup = Player.LineupManager!.GetExtraLineup(type);
                if (lineup?.BaseAvatars == null) continue;

                var lineupPb = new List<ChallengeAvatarInfoPb>();
                uint index = 0;
                foreach (var avatar in lineup.BaseAvatars)
                {
                    var formal = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
                    if (formal == null) continue;
                    lineupPb.Add(new ChallengeAvatarInfoPb { Index = index++, Id = (uint)formal.BaseAvatarId, AvatarType = AvatarType.AvatarFormalType, Level = (uint)formal.Level });
                }
                if (lineupPb.Count > 0) pb.Lineups.Add(lineupPb);
            }
            stats.StoryGroupStatistics[levelId] = pb;
			
        }
    }

    #endregion
}