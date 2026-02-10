using EggLink.DanhengServer.Proto;
using SqlSugar;

namespace EggLink.DanhengServer.Database.FightActivity;

[SugarTable("PlayerFightActivity")]
public class FightActivityData : BaseDatabaseDataHelper
{
    // 这里只存玩家打出来的“变量”
    [SugarColumn(IsJson = true)]
    public Dictionary<uint, FightStageResultData> Stages { get; set; } = new();
}

public class FightStageResultData
{
    public uint MaxWave { get; set; } = 0;               // 玩家打到的最高波次
    public uint UnlockLevel { get; set; } = 1;            // 玩家解锁到的最高难度
    public List<uint> TakenRewards { get; set; } = new(); // 玩家已经点过的领奖记录
}
