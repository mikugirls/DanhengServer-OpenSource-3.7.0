using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.FightActivity;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
namespace EggLink.DanhengServer.GameServer.Game.FightActivity;
/* --------------------------------------------------------------------------------
 * 【星芒战幕 (FightActivity) 协议字段业务全解析】
 * --------------------------------------------------------------------------------
 * * 1. GetFightActivityDataScRsp (主响应包 - 3623)
 * ---------------------------------------------------------
 * - Retcode (Tag 11)     : 错误码。0=正常，非0则主界面报错。
 * - KAIOMPFBGKL (Tag 13) : 活动开启总开关 (IsUnlocked)。
 * 必须为 true，否则 UI 界面无法加载。
 * - WorldLevel (Tag 14)  : 动态均衡等级。决定战斗内怪物等级基准。
 * - JKHIFDGHJDO (Tag 12) : 核心数组 [ICLFKKNFDME]。下发 8 个关卡的实时进度列表。
 * - DGNFCMDJOPA (Tag 2)  : 赛季或活动属性映射 Map。通常下发配置版本号或特殊倍率。
 * * * 2. ICLFKKNFDME (关卡进度快照类)
 * ---------------------------------------------------------
 * - GroupId (Tag 11)     : 关卡 ID。对应 ActivityFightGroupID (10001-10011)。
 * 控制界面显示哪个关卡（如“不止冰火两重天”）。
 * - OKJNNENKLCE (Tag 14) : 历史最高波次 (MaxWave)。
 * UI 显示“最高波次：X”，控制星星点亮数量。
 * - AKDLDFHCFBK (Tag 10) : 最高解锁难度 (MaxDifficulty)。
 * 1:矮星, 2:巨星, 3:超巨星。控制难度按钮的置灰/锁定。
 * - GGGHOOGILFH (Tag 3)  : 已领取奖励列表 [RewardID]。
 * 数组里的 ID 会使界面宝箱显示为“已领取”。
 * - ICLFKKNFDME (Tag 7)  : 挑战分数/时间 (Score)。
 * 波次活动通常填 0，由波次字段(Tag 14)主导。
 * * * 3. EnterFightActivityStageCsReq (进入战斗请求 - 3665)
 * ---------------------------------------------------------
 * - GroupId (Tag 3)      : 请求挑战的关卡 ID。
 * - NEDFIBONLKB (Tag 12) : 选定的难度等级 (1-3)。
 * 对应 Easy(矮星), Normal(巨星), Hard(超巨星)。
 * - AvatarList (Tag 14)  : 玩家选定的阵容 ID 列表。
 * Manager 需要在此注入 ActivityFightGroupExcel 里的试用角色。
 * * --------------------------------------------------------------------------------
 */
public class FightActivityManager(PlayerInstance player) : BasePlayerManager(player)
{
    public FightActivityInstance? ChallengeInstance { get; set; }

    /// <summary>
    /// 获取活动全局进度快照
    /// </summary>
   	public List<ICLFKKNFDME> GetFightActivityStageData() 
    {
        var list = new List<ICLFKKNFDME>();
        
        // 修正 1: 使用 DatabaseHelper.Instance 访问
        // 修正 2: 使用你改名后的 FightActivityData
        var dbData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<FightActivityData>(this.Player.Uid);

        // 修正 3: 确保引入 EggLink.DanhengServer.Data 命名空间
        foreach (var config in GameData.ActivityFightGroupData.Values)
        {
            uint groupId = (uint)config.ActivityFightGroupID;
            dbData.Stages.TryGetValue(groupId, out var progress);

            list.Add(new ICLFKKNFDME
            {
                GroupId = groupId,
                OKJNNENKLCE = progress?.MaxWave ?? 0,
                AKDLDFHCFBK = progress?.UnlockLevel ?? 1,
                GGGHOOGILFH = { progress?.FinishedEvents ?? new List<uint>() }
            });
        }

        return list;
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
