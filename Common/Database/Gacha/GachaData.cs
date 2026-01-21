using SqlSugar;

namespace EggLink.DanhengServer.Database.Gacha;

[SugarTable("Gacha")]
public class GachaData : BaseDatabaseDataHelper
{
    [SugarColumn(IsJson = true)] public List<GachaInfo> GachaHistory { get; set; } = [];
	// --- 新增：常驻池相关 ---
    /// <summary> 常驻池累积抽卡数 (用于300抽自选进度) </summary>
    public int StandardCumulativeCount { get; set; } = 0;
    
    /// <summary> 是否已经领取过常驻300抽自选奖励 </summary>
    public bool IsStandardSelected { get; set; } = false;

    // --- 新增：新手池相关 ---
    /// <summary> 新手池已抽取总次数 (上限50) </summary>
    public int NewbieGachaCount { get; set; } = 0;
    public bool LastAvatarGachaFailed { get; set; } = false;
    public bool LastWeaponGachaFailed { get; set; } = false;
    public int LastGachaFailedCount { get; set; } = 0;
    public int LastGachaPurpleFailedCount { get; set; } = 0;
    [SugarColumn(IsJson = true)] public List<int> GachaDecideOrder { get; set; } = [];
}

public class GachaInfo
{
    public int GachaId { get; set; }
    public long Time { get; set; }
    public int ItemId { get; set; }
}
