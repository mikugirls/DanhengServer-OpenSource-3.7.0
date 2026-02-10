using EggLink.DanhengServer.GameServer.Game.Player;

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
    /// 作用：初始化本次战斗的上下文
    /// </summary>
    public FightActivityInstance(PlayerInstance player, uint groupId, uint difficulty, int fightEventId, List<uint> avatars) 
    {
        this.Player = player;
        this.GroupId = groupId;
        this.Difficulty = difficulty;
        this.FightEventId = fightEventId;
        this.SelectedAvatars = avatars;
    }

    /// <summary>
    /// 进入活动战斗地图 (改为同步返回)
    /// 作用：调用 SceneManager 切换至星芒战幕专属 Plane (20111)
    /// </summary>
    public bool EnterMap() 
    { 
        // 内部调用 Player.SceneManager.EntryPoint...
        return true; 
    }

    /// <summary>
    /// 准备战斗 Slot
    /// 作用：下发必要的 Buff 和 阵容数据
    /// </summary>
    public void PrepareBattleSlot() { }

    /// <summary>
    /// 战斗结束处理
    /// </summary>
    public void OnComplete() { }
}
