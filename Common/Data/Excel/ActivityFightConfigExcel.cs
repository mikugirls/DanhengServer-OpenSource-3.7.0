using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;

namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("ActivityFightConfig.json")]
public class ActivityFightConfigExcel : ExcelResource
{
    // 战斗轮数限制
    public int RoundsLimit { get; set; }

    // 难度等级 (Easy, Normal, Hard)
    public string DifficultyLevel { get; set; } = string.Empty;

    // 战斗事件 ID，用于进入战斗时索引怪物配置
    public int FightEventID { get; set; }

    // 关联的奖励任务 ID (通常仅 Hard 难度有值)
    public int RewardQuest { get; set; }

    // 第二阶段奖励波次要求 (如超巨星 6 波)
    public int RewardWave2 { get; set; }

    // 奖励 ID
    public int RewardID { get; set; }

    // 等级偏移量
    public int OffsetLevel { get; set; }

    // 所属的活动战斗组 ID (如 10006)
    public int ActivityFightGroupID { get; set; }

    // 第一阶段奖励波次要求
    public int RewardWave { get; set; }

    // 总波次数限制
    public int TotalWave { get; set; }

    public override int GetId()
    {
        // 注意：由于此表 ActivityFightGroupID 不唯一（一关多难度），
        // 建议使用 FightEventID 作为唯一主键
        return FightEventID;
    }

    public override void Loaded()
    {
        // 将数据加载至 GameData 对应的静态字典中
        GameData.ActivityFightConfigData.Add(FightEventID, this);
    }
}
