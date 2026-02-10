using SqlSugar;

namespace EggLink.DanhengServer.Database.FightActivity;

[SugarTable("PlayerFightActivity")]
public class FightActivityData : BaseDatabaseDataHelper
{
    /// <summary>
    /// 关卡进度映射表
    /// Key: ActivityFightGroupID
    /// Value: 关卡的详细存档信息
    /// </summary>
    [SugarColumn(IsJson = true)]
    public Dictionary<uint, FightActivityStageInfo> StageInfoMap { get; set; } = new();
}

/// <summary>
/// 描述玩家在特定关卡中的最高成就
/// </summary>
public class FightActivityStageInfo
{
    public uint MaxWave { get; set; } = 0;                  // 历史最高波次 (对应 OKJNNENKLCE)
    public uint MaxDifficulty { get; set; } = 1;            // 已解锁的最高难度 (对应 AKDLDFHCFBK)
    public List<uint> FinishedEventIds { get; set; } = [];  // 已达成的事件奖励 ID (对应 GGGHOOGILFH)
}
