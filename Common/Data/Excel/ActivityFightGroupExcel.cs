using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;

namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("ActivityFightGroup.json")]
public class ActivityFightGroupExcel : ExcelResource
{
    // 关卡图标路径
    public string ActivityFightGroupIconPath { get; set; } = string.Empty;

    // 战斗舞台限制描述 HASH
    public HashName FightStageLimitDesc { get; set; } = new();

    // 战斗区域 ID
    public int BattleAreaID { get; set; }

    // 位面 ID
    public int PlaneID { get; set; }

    // 特殊试用角色 ID
    public int SpecialAvatarID { get; set; }

    // 层级 ID
    public int FloorID { get; set; }

    // 战斗舞台详细描述 HASH
    public HashName FightStageDesc { get; set; } = new();

    // 战斗舞台标题 HASH
    public HashName FightStageTitle { get; set; } = new();

    // 战斗区域组 ID
    public int BattleAreaGroupID { get; set; }

    // 核心标识：活动战斗组 ID
    public int ActivityFightGroupID { get; set; }

    public override int GetId()
    {
        return ActivityFightGroupID;
    }

    public override void Loaded()
    {
        // 将数据加载至 GameData 对应的静态字典中
        GameData.ActivityFightGroupData.Add(ActivityFightGroupID, this);
    }
}
