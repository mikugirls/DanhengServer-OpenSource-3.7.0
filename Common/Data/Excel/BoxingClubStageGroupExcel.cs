namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("BoxingClubStageGroup.json")]
public class BoxingClubStageGroupExcel : ExcelResource
{
    // 怪物组 ID
    public int StageGroupID { get; set; }

    // 怪物 ID 列表
    public List<int> MonsterIDList { get; set; } = [];

    // UI 展示用的事件图标列表
    public List<int> DisplayEventIDList { get; set; } = [];

    // 战斗逻辑生效的事件/Buff 列表
    public List<int> EventIDList { get; set; } = [];

    // 显示索引列表
    public List<int> DisplayIndexList { get; set; } = [];

    // 随机权重
    public int Weight { get; set; }

    public override int GetId()
    {
        return StageGroupID;
    }

    public override void Loaded()
    {
        // 存入 GameData 对应的字典
        GameData.BoxingClubStageGroupData.TryAdd(StageGroupID, this);
    }
}