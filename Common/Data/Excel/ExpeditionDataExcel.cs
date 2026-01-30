namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("ExpeditionData.json")]
public class ExpeditionDataExcel : ExcelResource
{
    public int ExpeditionID { get; set; }
    public int GroupID { get; set; }
    public int UnlockMission { get; set; }
    public int AvatarNumMin { get; set; }
    public int AvatarNumMax { get; set; }
    public List<int> AssignerIDList { get; set; } = [];
    public List<string> BonusDamageTypeList { get; set; } = [];
    public List<string> BonusBaseTypeList { get; set; } = [];

    public override int GetId()
    {
        return ExpeditionID;
    }

    public override void Loaded()
    {
        // 假设 GameData 中已定义对应的字典
        GameData.ExpeditionDataData.TryAdd(ExpeditionID, this);
    }
}
