namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("ExpeditionReward.json")]
public class ExpeditionRewardExcel : ExcelResource
{
    public int RewardID { get; set; }
    public int ExpeditionID { get; set; }
    public int Duration { get; set; } // 4, 8, 12, 20
    public int ExtraRewardID { get; set; }
    public int AvatarNum { get; set; }

    public override int GetId() => RewardID;

    public override void Loaded()
    {
        GameData.ExpeditionRewardData.TryAdd(RewardID, this);
        
        // 为了方便根据 ExpeditionID 查询所有可用的时长选项
        if (!GameData.ExpeditionIdToRewards.ContainsKey(ExpeditionID))
            GameData.ExpeditionIdToRewards[ExpeditionID] = new List<ExpeditionRewardExcel>();
            
        GameData.ExpeditionIdToRewards[ExpeditionID].Add(this);
    }
}
