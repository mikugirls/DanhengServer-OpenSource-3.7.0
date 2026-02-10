using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.FightActivity;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Game.FightActivity;

/* --------------------------------------------------------------------------------
 * 【星芒战幕 (FightActivity) 业务逻辑管理器】
 * --------------------------------------------------------------------------------
 * 职责说明：
 * 1. 负责从 Excel 配置 (GameData) 与 SQL 数据库 (FightActivityData) 中聚合数据。
 * 2. 处理关卡进度同步、奖励领取验证以及战斗结算后的存档更新。
 * * 协议字段对应关系 (ICLFKKNFDME):
 * - GroupId (Tag 6)      : 关卡组 ID (ActivityFightGroupID)。
 * - OKJNNENKLCE (Tag 7)  : 历史最高波次 (MaxWave)。
 * - AKDLDFHCFBK (Tag 5)  : 解锁的最高难度 (MaxDifficulty) -> 1:矮星, 2:巨星, 3:超巨星。
 * - GGGHOOGILFH (Tag 13) : 已达成/领取的事件 ID 列表 (FinishedEventIds)。
 * --------------------------------------------------------------------------------
 */
public class FightActivityManager(PlayerInstance player) : BasePlayerManager(player)
{
    /// <summary>
    /// 获取活动进度快照 (用于同步 GetFightActivityDataScRsp 或 DataChangeScNotify)
    /// </summary>
    /// <returns>返回经过协议包装的关卡列表</returns>
    public List<ICLFKKNFDME> GetFightActivityStageData() 
    {
        var list = new List<ICLFKKNFDME>();
        
        // 1. 获取该玩家在数据库中的持久化进度存档
        // 若玩家首次参加活动，DatabaseHelper 会自动创建新的字典实例
        var dbData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<FightActivityData>(this.Player.Uid);

        // 2. 遍历 Excel 配置表中的所有关卡组 (ActivityFightGroupExcel)
        // 以配置表为准进行遍历，确保服务器新增关卡时，客户端能实时显示
        foreach (var config in GameData.ActivityFightGroupData.Values)
        {
            uint groupId = (uint)config.ActivityFightGroupID;
            
            // 3. 尝试从数据库 Map 中提取该玩家在该关卡的进度记录
            // 采用你确定的字段名：StageInfoMap
            dbData.StageInfoMap.TryGetValue(groupId, out var info);

            // 4. 组装混淆协议对象 ICLFKKNFDME
            list.Add(new ICLFKKNFDME
            {
                GroupId = groupId,
                // 如果数据库无记录（新玩家），则波次默认为 0
                OKJNNENKLCE = info?.MaxWave ?? 0,
                // 如果数据库无记录，解锁难度默认为 1 (矮星级)
                AKDLDFHCFBK = info?.MaxDifficulty ?? 1,
                // 将已完成的事件 ID 列表注入 RepeatedField
                GGGHOOGILFH = { info?.FinishedEventIds ?? new List<uint>() }
            });
        }

        return list;
    }

    /// <summary>
    /// 处理进入关卡逻辑（预留钩子）
    /// </summary>
    public ValueTask<EnterFightActivityStageScRsp> ProcessEnterStage(EnterFightActivityStageCsReq req) 
    {
        // 未来在此实现：匹配 FightEventID、注入试用角色快照、启动战斗实例
        return ValueTask.FromResult(new EnterFightActivityStageScRsp { Retcode = 0 });
    }

    /// <summary>
    /// 领取阶段奖励逻辑（预留钩子）
    /// </summary>
    public TakeFightActivityRewardScRsp ProcessTakeReward(TakeFightActivityRewardCsReq req) 
    {
        // 未来在此实现：验证 MaxWave 是否达标、将 EventID 存入 FinishedEventIds、保存数据库并下发奖励
        return new TakeFightActivityRewardScRsp { Retcode = 0 };
    }

    /// <summary>
    /// 战斗结算回调逻辑（预留钩子）
    /// </summary>
    public void OnBattleSettlement(uint groupId, uint finishWave, uint difficultyIndex) 
    {
        // 未来在此实现：对比更新 MaxWave、根据配置判断是否增加 MaxDifficulty、发送 DataChangeScNotify
    }
}
