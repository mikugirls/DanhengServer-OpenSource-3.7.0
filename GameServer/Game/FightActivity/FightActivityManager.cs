using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Game.FightActivity;

public class FightActivityManager(PlayerInstance player) : BasePlayerManager(player)
{
    // 当前挑战实例的引用，用于跨协议维护战斗状态
    public FightActivityInstance? ChallengeInstance { get; set; }

    /// <summary>
    /// 获取活动全局进度快照
    /// 对应协议：GetFightActivityDataScRsp (3623)
    /// 逻辑：遍历所有 ActivityFightGroupID，从数据库合并最高波次和领奖状态
    /// </summary>
    public List<ICLFKKNFDME> GetFightActivityStageData() { }

    /// <summary>
    /// 处理进入关卡逻辑
    /// 对应协议：EnterFightActivityStageCsReq (3665)
    /// 逻辑：根据 GroupId 和 混淆难度 ID 查找配置，注入试用角色，初始化 ChallengeInstance 并执行地图跳转
    /// </summary>
    public async ValueTask<ICLFKKNFDME?> ProcessEnterStage(EnterFightActivityStageCsReq req) { }

    /// <summary>
    /// 领取关卡阶段奖励
    /// 对应协议：TakeFightActivityRewardCsReq (3686)
    /// 逻辑：校验数据库中的波次记录是否达到配置要求，发放物品并更新领奖记录数组
    /// </summary>
    public ICLFKKNFDME? ProcessTakeReward(uint groupId, uint rewardId) { }

    /// <summary>
    /// 战斗结算后的数据持久化
    /// 对应：PVEBattleResultCsReq 后的逻辑回调
    /// 逻辑：根据最终清怪波次更新数据库中的 MaxWave，并根据关卡顺序解锁下一关或下一难度
    /// </summary>
    public void OnBattleSettlement(uint groupId, uint finishWave) { }

    /// <summary>
    /// 构造混淆后的关卡状态快照
    /// 作用：统一将数据库/内存数据转换为 ICLFKKNFDME 协议对象
    /// </summary>
    private ICLFKKNFDME ConstructStageSnapshot(uint groupId) { }

    /// <summary>
    /// 退出活动挑战
    /// 逻辑：清理内存中的 ChallengeInstance，重置战斗状态
    /// </summary>
    public void ClearChallengeInstance() { }
}
