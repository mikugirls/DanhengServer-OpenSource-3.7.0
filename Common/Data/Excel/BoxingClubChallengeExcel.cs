using Newtonsoft.Json;
using EggLink.DanhengServer.Data.Excel;

namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("BoxingClubChallenge.json")]
public class BoxingClubChallengeExcel : ExcelResource
{
    [JsonProperty("ChallengeID")]
    public int ChallengeID { get; set; }

    // 修复：使用 = null! 或 = new() 消除 CS8618
    public TextBundle Name { get; set; } = new();

    public int PreChallengeID { get; set; }

    public int PerfectTurn { get; set; }

    public int ChallengeTurnLimit { get; set; }

    public int FirstPassRewardID { get; set; }

    public List<int> StageGroupList { get; set; } = new();

    public int SpecialAvatarActivityModule { get; set; }

    public List<uint> SpecialAvatarIDList { get; set; } = new();

    public List<string> DamageType { get; set; } = new();

    public Dictionary<string, int> StageBuffAndGroupMap { get; set; } = new();

    public int ActivityModuleID { get; set; }

    public int ChallengeBuff { get; set; }

    // 修复：初始化为 string.Empty 消除 CS8618
    public string Type { get; set; } = string.Empty;

    public bool IsSpecialChallenge { get; set; }

    public override int GetId()
    {
        return ChallengeID;
    }

    public override void Loaded()
    {
        GameData.BoxingClubChallengeData[ChallengeID] = this;
    }
}

public class TextBundle
{
    public int Hash { get; set; }
    public ulong Hash64 { get; set; }
}
