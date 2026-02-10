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
    /// <summary>
    /// 获取活动全局进度快照
    /// 核心逻辑：合并数据库进度与配置表中的试用角色 (SpecialAvatarID)
    /// </summary>
    public List<ICLFKKNFDME> GetFightActivityStageData() 
    {
        var list = new List<ICLFKKNFDME>();
        
        // 1. 获取数据库中的持久化数据 (使用你定义的 FightActivityData)
        var dbData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<FightActivityData>(this.Player.Uid);

        // 2. 遍历 ActivityFightGroupExcel 配置
        foreach (var config in GameData.ActivityFightGroupData.Values)
        {
            uint groupId = (uint)config.ActivityFightGroupID;
            
            // 尝试从数据库 StageInfoMap 获取该玩家的动态进度
            dbData.StageInfoMap.TryGetValue(groupId, out var info);

            // 3. 构造协议关卡快照
            var stageProto = new ICLFKKNFDME
            {
                GroupId = groupId,                         // Tag 6
                OKJNNENKLCE = info?.MaxWave ?? 0,          // Tag 7: 最高波次
                AKDLDFHCFBK = info?.MaxDifficulty ?? 1,    // Tag 5: 解锁难度
                GGGHOOGILFH = { info?.FinishedEventIds ?? new List<uint>() } // Tag 13: 事件奖励列表
            };

            // 4. 【注入试用角色】根据你的 Excel 定义使用 SpecialAvatarID
            // 如果该字段不为 0，则加入 AvatarList 告知客户端该关卡的试用角色
            if (config.SpecialAvatarID > 0)
            {
                // 将配置表里的特殊试用角色 ID 加入协议数组
                // 这决定了客户端关卡界面“试用角色”栏的显示
                stageProto.AvatarList.Add((uint)config.SpecialAvatarID);
            }

            list.Add(stageProto);
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
