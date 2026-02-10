using EggLink.DanhengServer.GameServer.Game.Player;
using System.Threading.Tasks; // 必须添加这个
namespace EggLink.DanhengServer.GameServer.Game.FightActivity;

public class FightActivityInstance
{
    // 基础信息
    public PlayerInstance Player { get; }
    public uint GroupId { get; }       // 当前关卡 ID (如 10004)
    public uint Difficulty { get; }    // 当前难度 (1-3)
    public int FightEventId { get; }   // 对应的怪物组事件 ID

    // 实时战斗状态
    public uint CurrentWave { get; set; }        // 实时波次记录
    public List<uint> SelectedAvatars { get; }   // 本场出战阵容（含试用）

    /// <summary>
    /// 构造函数
    /// 作用：初始化本次战斗的上下文，准备进入地图
    /// </summary>
    public FightActivityInstance(PlayerInstance player, uint groupId, uint difficulty, int fightEventId, List<uint> avatars) { }

    /// <summary>
    /// 进入活动战斗地图
    /// 作用：调用 SceneManager 切换至星芒战幕专属 Plane (20111) 和 Floor
    /// </summary>
    public async Task EnterMap() { }

    /// <summary>
    /// 注入战斗 Slot 数据
    /// 作用：在进入战斗前，将阵容、Buff 以及星芒战幕特有的机制数据写入 Slot 19
    /// </summary>
    public void PrepareBattleSlot() { }

    /// <summary>
    /// 实时同步战斗波次
    /// 作用：接收来自战斗引擎的同步包，更新 CurrentWave
    /// </summary>
    public void UpdateProgress(uint wave) { }

    /// <summary>
    /// 战斗结束处理
    /// 作用：根据最终波次判断是否达成奖励阈值，并通知 Manager 进行数据库持久化
    /// </summary>
    public void OnComplete() { }
}
