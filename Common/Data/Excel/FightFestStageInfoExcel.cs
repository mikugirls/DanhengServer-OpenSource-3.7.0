using EggLink.DanhengServer.Proto;
using Newtonsoft.Json;

namespace EggLink.DanhengServer.Data.Excel;

[ResourceEntity("FightFestStageInfo.json")]
public class FightFestStageInfoExcel : ExcelResource
{
    public uint EventID { get; set; }
    
    // 界面显示的预览怪物列表
    public List<uint> PreviewMonsterList { get; set; } = [];
    
    // 推荐属性 (如 Thunder, Physical)
    public List<string> RecommadNature { get; set; } = [];
    
    // 这一关的核心环境 Buff
    public uint EnvironmentBuffID { get; set; }
    
    // 这一关提供的试用角色列表
    public List<uint> SpecialAvatarList { get; set; } = [];
    
    public uint UIEnterBattleAreaID { get; set; }
    
    public int TutorialID { get; set; }

    // 文本 Hash
    public HashName ChallengeName { get; set; } = new();

    public override int GetId()
    {
        return (int)EventID;
    }

    public override void Loaded()
    {
        // 将数据存入 GameData 以便 FightFestManager 随时调取
        if (!GameData.FightFestStageConfig.ContainsKey(EventID))
        {
            GameData.FightFestStageConfig.Add(EventID, this);
        }
    }
}