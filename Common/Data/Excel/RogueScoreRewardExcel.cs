using EggLink.DanhengServer.Data.Excel;

namespace EggLink.DanhengServer.Data.Excel;

// 关联 JSON 文件名
[ResourceEntity("RogueScoreReward.json")]
public class RogueScoreRewardExcel : ExcelResource
{
    // 属性名必须严格对应 JSON 键名
    public int Score { get; set; }
    public int ScoreRow { get; set; }
    public int Reward { get; set; }
    public int RewardPoolID { get; set; }

    public override int GetId()
    {
        // 返回一个唯一 ID，这里用池子 ID 和行号组合
        return RewardPoolID * 100 + ScoreRow;
    }

    public override void Loaded()
    {
        // 自动注入到 GameData 中
        GameData.RogueScoreRewardData[GetId()] = this;
    }
}