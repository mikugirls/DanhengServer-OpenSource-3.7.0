namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("ExpeditionGroup.json")]
public class ExpeditionGroupExcel : ExcelResource
{
    public int GroupID { get; set; }

    public override int GetId() => GroupID;

    public override void Loaded()
    {
        GameData.ExpeditionGroupData.TryAdd(GroupID, this);
    }
}
