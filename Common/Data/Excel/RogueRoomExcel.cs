namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("RogueRoom.json")]
public class RogueRoomExcel : ExcelResource
{
    public int RogueRoomID { get; set; }
    public int RogueRoomType { get; set; }
    public int MapEntrance { get; set; }
    public int GroupID { get; set; }
    public Dictionary<int, int> GroupWithContent { get; set; } = [];
	public int Variation { get; set; } = 1; // 默认设为 1，确保老配置不用改也能跑

    public override int GetId()
    {
        return RogueRoomID;
    }

    public override void Loaded()
    {
        GameData.RogueRoomData.Add(RogueRoomID, this);
    }
}