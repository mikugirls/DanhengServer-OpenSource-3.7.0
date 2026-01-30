using EggLink.DanhengServer.Database.Inventory;

namespace EggLink.DanhengServer.Database.Expedition;

/// <summary>
/// 玩家派遣数据持久化模型
/// </summary>
public class ExpeditionData : BaseDatabase
{
    /// <summary>
    /// 当前正在进行的派遣列表
    /// </summary>
    public List<ExpeditionInfoInstance> ExpeditionList { get; set; } = [];

    /// <summary>
    /// 已领取的派遣奖励次数（可选，用于某些成就或任务统计）
    /// </summary>
    public int TotalFinishedCount { get; set; }
}

/// <summary>
/// 单个派遣实例的详细信息
/// </summary>
public class ExpeditionInfoInstance
{
    /// <summary>
    /// 派遣 ID (对应 ExpeditionID)
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// 派遣总时长（秒）
    /// </summary>
    public uint TotalDuration { get; set; }

    /// <summary>
    /// 开始派遣的时间戳（秒）
    /// </summary>
    public long StartExpeditionTime { get; set; }

    /// <summary>
    /// 参与派遣的角色 ID 列表
    /// </summary>
    public List<uint> AvatarIdList { get; set; } = [];
}
