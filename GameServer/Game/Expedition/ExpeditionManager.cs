
using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Expedition;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.Expedition;

public class ExpeditionManager(PlayerInstance player) : BasePlayerManager(player)
{
    // 定义 Data 属性
    public ExpeditionData Data { get; private set; }

    // 构造函数
    public ExpeditionManager(PlayerInstance player) : base(player)
    {
        // 1. 初始化并持久化玩家派遣数据
        Data = DatabaseHelper.Instance!.GetInstanceOrCreateNew<ExpeditionData>(player.Uid);
        
        // 2. 如果有需要初始化的局部配置或实例，可以在此处添加
        // EnsureLocalConfigLoaded(); 
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
        var newExpedition = new ExpeditionInfoInstance
        {
            Id = info.Id,
            TotalDuration = info.TotalDuration,
            StartExpeditionTime = info.StartExpeditionTime,
            AvatarIdList = info.AvatarIdList.ToList()
        };
        Data.ExpeditionList.Add(newExpedition);

        // 7. 发送成功回执 (AcceptExpeditionScRsp: 2538)
        var rsp = new AcceptExpeditionScRsp
        {
            Retcode = 0,
            AcceptExpedition = new ExpeditionInfo
            {
                Id = info.Id,
                TotalDuration = info.TotalDuration,
                StartExpeditionTime = info.StartExpeditionTime
            }
        };
        rsp.AcceptExpedition.AvatarIdList.AddRange(info.AvatarIdList);
        await Player.SendPacket(rsp);

        // 8. 触发任务系统逻辑：处理特定的派遣任务目标 
        await Player.MissionManager!.HandleFinishType(Enums.Mission.MissionFinishTypeEnum.FinishMission, (int)info.Id);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 获取当前已解锁的派遣队伍槽位
    /// </summary>
    private int GetUnlockedExpeditionSlots()
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

    #endregion
}
