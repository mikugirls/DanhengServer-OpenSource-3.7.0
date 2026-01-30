using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Expedition;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Expedition;

namespace EggLink.DanhengServer.GameServer.Game.Expedition;

public class ExpeditionManager : BasePlayerManager
{
    // 直接引用 PlayerInstance 中初始化好的 Data
    public ExpeditionData Data => Player.ExpeditionData!;

    public ExpeditionManager(PlayerInstance player) : base(player) { }

    public async ValueTask Initialize()
    {
        // 玩家上线时同步一次全量数据
        await SyncExpeditionData();
    }

    #region Main Actions

    /// <summary>
    /// 处理开始派遣请求 (CmdId: 2521)
    /// </summary>
    public async ValueTask AcceptExpedition(AcceptExpeditionCsReq req)
    {
        var info = req.AcceptExpedition;
        if (info == null) return;

        // 1. 获取派遣静态配置 
        if (!GameData.ExpeditionDataData.TryGetValue((int)info.Id, out var config))
            return;

        // 2. 校验队伍上限
        int unlockedSlots = GetUnlockedExpeditionSlots();
        if (Data.ExpeditionList.Count >= unlockedSlots)
            return;

        // 3. 校验解锁任务条件 
        if (config.UnlockMission > 0 && 
            Player.MissionManager!.GetMainMissionStatus(config.UnlockMission) != Enums.Mission.MissionPhaseEnum.Finish)
            return;

        // 4. 校验人数限制
        if (info.AvatarIdList.Count < config.AvatarNumMin || info.AvatarIdList.Count > config.AvatarNumMax)
            return;

        // 5. 校验派遣时长与奖励配置
        var rewardConfig = GameData.ExpeditionIdToRewards[(int)info.Id]
            .FirstOrDefault(x => (uint)(x.Duration * 3600) == info.TotalDuration);
        if (rewardConfig == null) return;

        // 6. 保存至数据库 
        var newExpedition = new ExpeditionInfoInstance
        {
            Id = info.Id,
            TotalDuration = info.TotalDuration,
            StartExpeditionTime = Extensions.GetUnixSec(), 
            AvatarIdList = info.AvatarIdList.ToList()
        };
        
        Data.ExpeditionList.Add(newExpedition);
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

        // 7. 发送回执
        await Player.SendPacket(new PacketAcceptExpeditionScRsp(newExpedition));

        // 8. 全量同步 (刷新 UI 占用状态)
        await SyncExpeditionData();
    }

    /// <summary>
    /// 领取派遣奖励 (CmdId: 2552) - 完整双重加成版
    /// </summary>
    public async ValueTask TakeExpeditionReward(uint expeditionId)
    {
        // 1. 查找派遣实例
        var instance = Data.ExpeditionList.FirstOrDefault(x => x.Id == expeditionId);
        if (instance == null) return;

        // 2. 时间校验
        if (Extensions.GetUnixSec() < (instance.StartExpeditionTime + instance.TotalDuration))
            return;

        // 3. 获取点位配置与奖励配置
        if (!GameData.ExpeditionDataData.TryGetValue((int)instance.Id, out var config))
            return;

        var rewardConfig = GameData.ExpeditionIdToRewards[(int)instance.Id]
            .FirstOrDefault(x => (uint)(x.Duration * 3600) == instance.TotalDuration);
        
        if (rewardConfig != null)
        {
            // --- 核心加成判定：命途 + 属性 ---
            bool hasBonus = false;
            foreach (var avatarId in instance.AvatarIdList)
            {
                if (GameData.AvatarConfigData.TryGetValue((int)avatarId, out var avatarExcel))
                {
                    // A. 判定命途加成
                    if (config.BonusBaseTypeList.Count > 0 && 
                        config.BonusBaseTypeList.Contains(avatarExcel.AvatarBaseType.ToString()))
                    {
                        hasBonus = true;
                        break; 
                    }

                    // B. 判定属性加成
                    if (config.BonusDamageTypeList.Count > 0 && 
                        config.BonusDamageTypeList.Contains(avatarExcel.DamageType.ToString()))
                    {
                        hasBonus = true;
                        break;
                    }
                }
            }

            // 4. 发放基础奖励
            var rewardItems = await Player.InventoryManager!.HandleReward(rewardConfig.RewardID, notify: true, sync: true);
            var rewardProto = new ItemList();
            rewardProto.ItemList_.AddRange(rewardItems.Select(x => x.ToProto()));

            // 5. 发放额外奖励
            var extraRewardProto = new ItemList();
            if (hasBonus && rewardConfig.ExtraRewardID > 0)
            {
                var extraItems = await Player.InventoryManager!.HandleReward(rewardConfig.ExtraRewardID, notify: true, sync: true);
                extraRewardProto.ItemList_.AddRange(extraItems.Select(x => x.ToProto()));
            }

            // 6. 发送领奖回执
            await Player.SendPacket(new PacketTakeExpeditionRewardScRsp(expeditionId, rewardProto, extraRewardProto));
        }

        // 7. 【关键】清理数据并持久化
        Data.ExpeditionList.Remove(instance);
        Data.TotalFinishedCount++; 
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

        // 8. 同步最新状态
        await SyncExpeditionData();
    }

    /// <summary>
    /// 中途取消派遣 (CmdId: 2584)
    /// </summary>
    public async ValueTask CancelExpedition(uint expeditionId)
    {
        var instance = Data.ExpeditionList.FirstOrDefault(x => x.Id == expeditionId);
        if (instance == null) return;

        Data.ExpeditionList.Remove(instance);
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

        await Player.SendPacket(new PacketCancelExpeditionScRsp(expeditionId));
        
        // 同步释放槽位
        await SyncExpeditionData();
    }

    #endregion

    #region Helpers

    public async ValueTask SyncExpeditionData()
    {
        // 1. 获取已解锁的点位 ID 列表
        var unlockedIds = GameData.ExpeditionDataData.Values
            .Where(config => config.UnlockMission == 0 || 
                   Player.MissionManager!.GetMainMissionStatus(config.UnlockMission) == Enums.Mission.MissionPhaseEnum.Finish)
            .Select(config => (uint)config.ExpeditionID)
            .ToList();

        // 2. 构造通知包 (对齐 3.7.0 Proto 字段)
        var proto = new ExpeditionDataChangeScNotify
        {
            TotalExpeditionCount = (uint)Data.TotalFinishedCount, 
            ExpeditionInfo = { Data.ExpeditionList.Select(x => x.ToProto()) },
            JFJPADLALMD = { unlockedIds } // 对应 UnlockedExpeditionIdList 的混淆名
        };

        await Player.SendPacket(new PacketExpeditionDataChangeScNotify(proto));
    }

    public int GetUnlockedExpeditionSlots()
    {
        int count = 0;
        foreach (var team in GameData.ExpeditionTeamData.Values)
        {
            if (team.UnlockMission == 0 || 
                Player.MissionManager!.GetMainMissionStatus(team.UnlockMission) == Enums.Mission.MissionPhaseEnum.Finish)
            {
                count++;
            }
        }
        return count == 0 ? 2 : count;
    }

    #endregion
}
