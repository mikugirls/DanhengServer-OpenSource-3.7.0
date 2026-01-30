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

    public ExpeditionManager(PlayerInstance player) : base(player)
    {
    }

    #region Main Actions

    /// <summary>
    /// 处理开始派遣请求 (CmdId: 2521)
    /// </summary>
    public async ValueTask AcceptExpedition(AcceptExpeditionCsReq req)
    {
        // 基础验证 
        var info = req.AcceptExpedition;
        if (info == null) return;

        // 1. 获取派遣静态配置 
        if (!GameData.ExpeditionDataData.TryGetValue((int)info.Id, out var config))
            return;

        // 2. 校验队伍上限 (参考 ExpeditionTeam.json)
        int unlockedSlots = GetUnlockedExpeditionSlots();
        if (Data.ExpeditionList.Count >= unlockedSlots)
            return;

        // 3. 校验解锁任务条件 
        if (config.UnlockMission > 0 && 
            Player.MissionManager!.GetMainMissionStatus(config.UnlockMission) != Enums.Mission.MissionPhaseEnum.Finish)
            return;

        // 4. 校验人数限制 (参考 ExpeditionData.json)
        if (info.AvatarIdList.Count < config.AvatarNumMin || info.AvatarIdList.Count > config.AvatarNumMax)
            return;

        // 5. 校验派遣时长与奖励配置 (参考 ExpeditionReward.json)
        var rewardConfig = GameData.ExpeditionIdToRewards[(int)info.Id]
            .FirstOrDefault(x => x.Duration == info.TotalDuration);
        if (rewardConfig == null) return;

        // 6. 保存至数据库 
        // 修正：使用当前服务器时间并存入列表
        var newExpedition = new ExpeditionInfoInstance
        {
            Id = info.Id,
            TotalDuration = info.TotalDuration,
            StartExpeditionTime = Extensions.GetUnixSec(), // 使用服务器授时
            AvatarIdList = info.AvatarIdList.ToList()
        };
        
        Data.ExpeditionList.Add(newExpedition);
        
        // 标记数据库保存
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

        // 注意：根据你的要求，ScRsp 和任务触发逻辑暂不编写，直到你提供对应的协议或指示
        await Player.SendPacket(new PacketAcceptExpeditionScRsp(newExpedition));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 获取当前已解锁的派遣队伍槽位
    /// </summary>
    public int GetUnlockedExpeditionSlots()
    {
        int count = 0;
        foreach (var team in GameData.ExpeditionTeamData.Values)
        {
            // 如果没有解锁任务或任务已完成，则计入槽位 
            if (team.UnlockMission == 0 || 
                Player.MissionManager!.GetMainMissionStatus(team.UnlockMission) == Enums.Mission.MissionPhaseEnum.Finish)
            {
                count++;
            }
        }
        // 兜底逻辑：至少拥有 2 个初始槽位
        return count == 0 ? 2 : count;
    }
    /// <summary>
/// 处理领取派遣奖励请求 (CmdId: 2552)
/// </summary>
/// <summary>
    /// 领取派遣奖励 (CmdId: 2552) - 含官服角色加成逻辑
    /// </summary>
    /// <summary>
/// 领取派遣奖励 (CmdId: 2552) - 完整双重加成版
/// </summary>
public async ValueTask TakeExpeditionReward(uint expeditionId)
{
    // 1. 查找派遣实例
    var instance = Data.ExpeditionList.FirstOrDefault(x => x.Id == expeditionId);
    if (instance == null) return;

    // 2. 时间校验 (当前时间 >= 开始时间 + 总时长)
    if (Extensions.GetUnixSec() < (instance.StartExpeditionTime + instance.TotalDuration))
    {
        return;
    }

    // 3. 获取点位配置(用于判定加成列表)与奖励配置
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
                // A. 判定命途加成 (如 "Destruction", "Knight")
                if (config.BonusBaseTypeList.Count > 0 && 
                    config.BonusBaseTypeList.Contains(avatarExcel.AvatarBaseType.ToString()))
                {
                    hasBonus = true;
                    break; 
                }

                // B. 判定属性加成 (如 "Fire", "Ice")
                // 注意：这里假设你的 AvatarConfigExcel 中属性字段名为 DamageType
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

        // 5. 发放额外奖励 (满足任意加成条件时)
        var extraRewardProto = new ItemList();
        if (hasBonus && rewardConfig.ExtraRewardID > 0)
        {
            var extraItems = await Player.InventoryManager!.HandleReward(rewardConfig.ExtraRewardID, notify: true, sync: true);
            extraRewardProto.ItemList_.AddRange(extraItems.Select(x => x.ToProto()));
        }

        // 6. 发送回执包 (确保 Packet 类构造函数接收 3 个参数)
        await Player.SendPacket(new PacketTakeExpeditionRewardScRsp(expeditionId, rewardProto, extraRewardProto));
    }

    // 7. 清理数据并持久化
    Data.ExpeditionList.Remove(instance);
    DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
}

	/// <summary>
    /// 中途取消
    /// </summary>
    public async ValueTask CancelExpedition(uint expeditionId)
    {
        var instance = Data.ExpeditionList.FirstOrDefault(x => x.Id == expeditionId);
        if (instance == null) return;

        Data.ExpeditionList.Remove(instance);
        DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

        await Player.SendPacket(new PacketCancelExpeditionScRsp(expeditionId));
    }
	public async ValueTask SyncExpeditionData()
{
    // 构造通知包
    var notify = new ExpeditionDataChangeScNotify
    {
        // 1. 获取当前所有正在进行的派遣
        ExpeditionList = { Data.ExpeditionList.Select(x => x.ToProto()) },
        
        // 2. 计算当前解锁的槽位总数 (2, 3, 4...)
        TeamCount = (uint)GetUnlockedExpeditionSlots(),
        
        // 3. 计算已解锁的所有点位 ID (遍历配置表，检查任务是否完成)
        UnlockedExpeditionIdList = { GetUnlockedExpeditionIds() }
    };

    await Player.SendPacket(new PacketExpeditionDataChangeScNotify(notify));
}

// 辅助方法：获取当前玩家解锁的所有派遣点 ID
private List<uint> GetUnlockedExpeditionIds()
{
    return GameData.ExpeditionDataData.Values
        .Where(config => config.UnlockMission == 0 || 
               Player.MissionManager!.GetMainMissionStatus(config.UnlockMission) == Enums.Mission.MissionPhaseEnum.Finish)
        .Select(config => (uint)config.ExpeditionID)
        .ToList();
}
    #endregion
}
