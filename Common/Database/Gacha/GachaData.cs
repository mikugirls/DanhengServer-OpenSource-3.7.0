using SqlSugar;

namespace EggLink.DanhengServer.Database.Gacha;


[SugarTable("Gacha")]
public class GachaData : BaseDatabaseDataHelper
{
    [SugarColumn(IsJson = true)] public List<GachaInfo> GachaHistory { get; set; } = [];

    // --- 1. 常驻池 (1001) ---
    public int StandardCumulativeCount { get; set; } = 0; // 既是自选进度，也是常驻5星水位
	/// <summary> 常驻池5星水位保底计数 (出金立刻重置) </summary>
	[SugarColumn(DefaultValue = "0")]
    public int StandardPityCount { get; set; } = 0; // <--- 新增这个标记
    public bool IsStandardSelected { get; set; } = false;
	public uint PlayerGachaSeed { get; set; } = 0;
    // --- 2. 新手池 (4001) ---
	[SugarColumn(DefaultValue = "0")]
    public int NewbieGachaCount { get; set; } = 0; // 新手池独立水位
	[SugarColumn(DefaultValue = "0")]
	public int NewbiePityCount { get; set; } = 0;  // 保底水位（出金后重置为 0）
    // --- 3. 限定角色池 (AvatarUp) ---
	[SugarColumn(DefaultValue = "0")]
    public int LastAvatarGachaPity { get; set; } = 0; // 建议新增：专门记录角色池垫了多少抽
    public bool LastAvatarGachaFailed { get; set; } = false; // 记录是否歪了（大保底状态）

    // --- 4. 限定武器池 (WeaponUp) ---
	[SugarColumn(DefaultValue = "0")]
    public int LastWeaponGachaPity { get; set; } = 0; // 建议新增：专门记录武器池垫了多少抽
    public bool LastWeaponGachaFailed { get; set; } = false; // 记录武器是否歪了

    // --- 5. 通用/四星 ---
    public int LastGachaFailedCount { get; set; } = 0; // 兜底用水位
    public int LastGachaPurpleFailedCount { get; set; } = 0; // 四星水位
    
    [SugarColumn(IsJson = true)] public List<int> GachaDecideOrder { get; set; } = [];
}

public class GachaInfo
{
    public int GachaId { get; set; }
    public long Time { get; set; }
    public int ItemId { get; set; }
}
