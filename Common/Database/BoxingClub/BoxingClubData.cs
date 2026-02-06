using EggLink.DanhengServer.Database.Lineup;
using SqlSugar;

namespace EggLink.DanhengServer.Database.BoxingClub;
[SugarTable("BoxingClubData")]
public class BoxingClubData : BaseDatabaseDataHelper
{
    // 每一个挑战 ID 对应的持久化记录
    [SugarColumn(IsJson = true)]
    public Dictionary<int, BoxingClubInfo> Challenges { get; set; } = [];
}

public class BoxingClubInfo
{
    public int ChallengeId { get; set; }
    
    public bool IsFinished { get; set; } // 是否已通关 (Tag 10)
    
    // [修正] 使用 int 类型，记录历史最快通关回合数 (Tag 13)
    public int MinRounds { get; set; } 

    // 阵容记忆 (Tag 3)
    public List<LineupAvatarInfo> Lineup { get; set; } = [];
}
