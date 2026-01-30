namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("ExpeditionTeam.json")]
public class ExpeditionTeamExcel : ExcelResource
{
    public int TeamID { get; set; }
    public int UnlockMission { get; set; }

    public override int GetId() => TeamID;

    public override void Loaded()
    {
        GameData.ExpeditionTeamData.TryAdd(TeamID, this);
    }
}
