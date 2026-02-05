using EggLink.DanhengServer.Database.Lineup;
using SqlSugar;

namespace EggLink.DanhengServer.Database.BoxingClub;

[SugarTable("BoxingClubData")]
public class BoxingClubData : BaseDatabaseDataHelper
{
    // 记录当前活跃的挑战 ID (对应 config.ChallengeID)
    public int CurChallengeId { get; set; }

    // 每一个挑战 ID 对应的持久化数据（包含关卡进度、已选阵容等）
    [SugarColumn(IsJson = true)]
    public Dictionary<int, BoxingClubInfo> RunningChallenges { get; set; } = [];
}

public class BoxingClubInfo
{
    public int ChallengeId { get; set; }
    
    // 学习 StoryLineInfo：存储该关卡专属的阵容（包含试用角色）
    // 这样即便切换关卡，每个 ChallengeID 对应的阵容也互不干扰
    public List<LineupAvatarInfo> Lineup { get; set; } = [];
}
