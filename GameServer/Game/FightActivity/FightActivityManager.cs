using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Game.FightActivity;

public class FightActivityManager(PlayerInstance player) : BasePlayerManager(player)
{
    public FightActivityInstance? ChallengeInstance { get; set; }

    /// <summary>
    /// 获取活动全局进度快照
    /// </summary>
    public List<ICLFKKNFDME> GetFightActivityStageData() 
    {
        // 暂时返回空列表以通过编译
        return new List<ICLFKKNFDME>();
    }

    /// <summary>
    /// 处理进入关卡逻辑
    /// </summary>
    public ValueTask<ICLFKKNFDME?> ProcessEnterStage(EnterFightActivityStageCsReq req) 
    {
        // 暂时返回默认值以通过编译
        return ValueTask.FromResult<ICLFKKNFDME?>(null);
    }

    /// <summary>
    /// 领取关卡阶段奖励
    /// </summary>
    public ICLFKKNFDME? ProcessTakeReward(uint groupId, uint rewardId) 
    {
        return null;
    }

    /// <summary>
    /// 战斗结算后的数据持久化
    /// </summary>
    public void OnBattleSettlement(uint groupId, uint finishWave) 
    {
        // void 方法不需要 return
    }

    /// <summary>
    /// 构造混淆后的关卡状态快照
    /// </summary>
    private ICLFKKNFDME ConstructStageSnapshot(uint groupId) 
    {
        // 必须返回一个对象，否则报错
        return new ICLFKKNFDME();
    }

    /// <summary>
    /// 退出活动挑战
    /// </summary>
    public void ClearChallengeInstance() 
    {
    }
}
