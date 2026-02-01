using Newtonsoft.Json;

namespace EggLink.DanhengServer.Data.Excel;

// ResourceEntity 特性指定对应的 JSON 文件名
[ResourceEntity("FightFestChallenge.json")]
public class FightFestChallengeExcel : ExcelResource
{
    public int ChallengeID { get; set; }
    public uint EventID { get; set; }
    public int GroupID { get; set; }
    public int QuestGroupID { get; set; }
    public int AvatarInfoID { get; set; }
    public int TutorialID { get; set; }
    public uint EnvironmentBuffID { get; set; }
    
    // 任务 ID 列表（一关包含的 4 个小关）
    public List<uint> QuestIDList { get; set; } = [];
    
    // 试用角色 ID 列表
    public List<uint> SpecialAvatarList { get; set; } = [];
    
    // 战斗目标列表
    public List<uint> BattleTargetList { get; set; } = [];

    // 文本 Hash 处理
    public HashName TabName { get; set; } = new();
    public HashName ChallengeName { get; set; } = new();
    public HashName OriginalStageName { get; set; } = new();
    public HashName UnlockTips { get; set; } = new();

    // 资源路径
    public string FigurePath { get; set; } = "";
    public string TabIconPath { get; set; } = "";

    // 获取唯一 ID 的方法，这里返回 ChallengeID
    public override int GetId()
    {
        return ChallengeID;
    }

    // 加载后的回调逻辑
    public override void Loaded()
    {
        // 将数据存入 GameData 的字典中以便后续 FightFestManager 调用
        if (!GameData.FightFestChallengeConfig.ContainsKey((uint)ChallengeID))
        {
            GameData.FightFestChallengeConfig.Add((uint)ChallengeID, this);
        }
    }
}