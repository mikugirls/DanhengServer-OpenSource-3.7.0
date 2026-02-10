using EggLink.DanhengServer.Proto;
using SqlSugar;

namespace EggLink.DanhengServer.Database.FightActivity;

/// <summary>
/// 星芒战幕数据库存档模型 (独立表)
/// </summary>
[SugarTable("PlayerFightActivity")]
public class PlayerFightActivity : BaseDatabaseDataHelper
{
    /// <summary>
    /// 存储每个关卡的进度数据
    /// Key: ActivityFightGroupID (例如 10001, 10004)
    /// </summary>
    [SugarColumn(IsJson = true)]
    public Dictionary<uint, FightStageResultData> Stages { get; set; } = new();

    /// <summary>
    /// 转换为客户端协议混淆列表 (ICLFKKNFDME)
    /// 包含：波次进度、难度锁、领奖状态
    /// </summary>
    public List<ICLFKKNFDME> ToProto()
    {
        var protoList = new List<ICLFKKNFDME>();

        foreach (var kv in Stages)
        {
            var stageInfo = kv.Value;
            protoList.Add(new ICLFKKNFDME
            {
                GroupId = kv.Key,                  // Tag 11: 关卡 ID
                OKJNNENKLCE = stageInfo.MaxWave,    // Tag 14: 历史最高波次 (混淆名)
                AKDLDFHCFBK = stageInfo.UnlockLevel, // Tag 10: 解锁最高难度 (混淆名)
                GGGHOOGILFH = { stageInfo.TakenRewards } // Tag 3: 已领奖列表 (混淆名)
            });
        }

        return protoList;
    }
}

/// <summary>
/// 单个关卡的存档详情详情
/// </summary>
public class FightStageResultData
{
    /// <summary>
    /// 历史最高波次 (对应混淆字段 OKJNNENKLCE)
    /// </summary>
    public uint MaxWave { get; set; } = 0;

    /// <summary>
    /// 已解锁的最高难度 (对应混淆字段 AKDLDFHCFBK)
    /// 1: 矮星, 2: 巨星, 3: 超巨星
    /// </summary>
    public uint UnlockLevel { get; set; } = 1;

    /// <summary>
    /// 已领取的奖励 ID 列表 (对应混淆字段 GGGHOOGILFH)
    /// </summary>
    public List<uint> TakenRewards { get; set; } = new();
}
