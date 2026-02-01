using Newtonsoft.Json;
using EggLink.DanhengServer.Data.Excel;

namespace EggLink.DanhengServer.Data.Excel;

// 指定对应的 JSON 文件名，这里的路径需与你的资源文件夹结构一致
[ResourceEntity("BoxingClubChallenge.json")]
public class BoxingClubChallengeExcel : ExcelResource
{
    // 挑战等级 ID (1, 2, 3, 4, 5, 10, 11)
    [JsonProperty("ChallengeID")]
    public int ChallengeID { get; set; }

    // 名字哈希值，用于匹配 TextMapCN.json
    public TextBundle Name { get; set; }

    // 前置挑战 ID，用于判定解锁逻辑
    public int PreChallengeID { get; set; }

    // 完美通关所需轮数（影响星级/奖励评价）
    public int PerfectTurn { get; set; }

    // 挑战总轮数限制
    public int ChallengeTurnLimit { get; set; }

    // 首次通关奖励 ID
    public int FirstPassRewardID { get; set; }

    // 每一关对应的怪物组列表（Match 时从中随机抽取）
    public List<int> StageGroupList { get; set; } = new();

    // 试用角色模块 ID
    public int SpecialAvatarActivityModule { get; set; }

    // 试用角色 ID 列表
    public List<uint> SpecialAvatarIDList { get; set; } = new();

    // 推荐的伤害类型（UI 显示用）
    public List<string> DamageType { get; set; } = new();

    // 关卡对应的 Buff 组映射关系
    public Dictionary<string, int> StageBuffAndGroupMap { get; set; } = new();

    // 活动模块 ID
    public int ActivityModuleID { get; set; }

    // 初始自带的挑战 Buff ID
    public int ChallengeBuff { get; set; }

    // 对应 JSON 里的 Type 字段 (First, Second 等)
    public string Type { get; set; }

    public bool IsSpecialChallenge { get; set; }

    public override int GetId()
    {
        return ChallengeID;
    }

    public override void Loaded()
    {
        // 将加载后的数据存入 GameData 的全局字典中，方便后续 Handler 调用
        // 需要在 GameData 类中预先定义：
        // public static Dictionary<int, BoxingClubChallengeExcel> BoxingClubChallengeData = new();
        GameData.BoxingClubChallengeData[ChallengeID] = this;
    }
}

// 辅助类：处理 JSON 中的嵌套 Name 对象
public class TextBundle
{
    public int Hash { get; set; }
    public ulong Hash64 { get; set; }
}