namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("ActivityLoginConfig.json")]
public class ActivityLoginConfigExcel : ExcelResource
{
    public int ID { get; set; }
    public int ActivityModuleID { get; set; }
    public List<int> RewardList { get; set; } = [];

    public override int GetId()
    {
        return ID;
    }

    public override void Loaded()
    {
        // 将数据注册到 GameData 中，解决之前的 CS0117 报错
        GameData.ActivityLoginConfigData[ID] = this;
    }
}