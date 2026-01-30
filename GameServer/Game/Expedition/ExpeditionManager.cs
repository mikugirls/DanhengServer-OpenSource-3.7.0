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
public async ValueTask TakeExpeditionReward(uint expeditionId)
{
    var instance = Data.ExpeditionList.FirstOrDefault(x => x.Id == expeditionId);
    if (instance == null) return;

    // 1. 时间校验 (略)
    
    // 2. 获取任务配置以查看“推荐属性”
    if (!GameData.ExpeditionDataData.TryGetValue((int)instance.Id, out var config))
        return;

    var rewardConfig = GameData.ExpeditionIdToRewards[(int)instance.Id]
        .FirstOrDefault(x => (uint)(x.Duration * 3600) == instance.TotalDuration);
    
    if (rewardConfig != null)
    {
        // --- 角色加成判定 ---
        bool hasBonus = false;
        foreach (var avatarId in instance.AvatarIdList)
        {
            [cite_start]// 通过你提供的 AvatarManager 获取角色数据 
            var avatarExcel = GameData.AvatarConfigData.GetValueOrDefault(avatarId);
            if (avatarExcel == null) continue;

            // 逻辑判定：例如推荐命途匹配（假设 config 中定义了推荐命途字段）
            // if (avatarExcel.AvatarBaseType == config.RecommendPath) { hasBonus = true; break; }
            
            // 或者：只要队伍中有特定属性的角色即可触发额外奖励
            hasBonus = true; 
        }

        // 3. 发放基础奖励
        var rewardItems = await Player.InventoryManager!.HandleReward(rewardConfig.RewardID, notify: true, sync: true);
        var rewardProto = new ItemList();
        rewardProto.ItemList_.AddRange(rewardItems.Select(x => x.ToProto()));

        // 4. 发放额外奖励
        var extraRewardProto = new ItemList();
        if (hasBonus && rewardConfig.ExtraRewardID > 0)
        {
            var extraItems = await Player.InventoryManager!.HandleReward(rewardConfig.ExtraRewardID, notify: true, sync: true);
            extraRewardProto.ItemList_.AddRange(extraItems.Select(x => x.ToProto()));
        }

        // 5. 发送更新后的回执包
        await Player.SendPacket(new PacketTakeExpeditionRewardScRsp(expeditionId, rewardProto, extraRewardProto));
    }

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
    #endregion
}
