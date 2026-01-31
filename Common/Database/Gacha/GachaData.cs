using SqlSugar;

namespace EggLink.DanhengServer.Database.Gacha;

[SugarTable("Gacha")]
public class GachaData : BaseDatabaseDataHelper
{
    [SugarColumn(IsJson = true)] 
    public List<GachaInfo> GachaHistory { get; set; } = [];

    // --- 1. 常驻池 (1001) ---
    // 旧字段无需改动，但为了安全建议也加上默认初始化
    public int StandardCumulativeCount { get; set; } = 0; 
    
    /// <summary> 常驻池5星水位保底计数 (出金立刻重置) </summary>
    [SugarColumn(DefaultValue = "0")]
    public int? StandardPityCount { get; set; } = 0; // 改为 int?

    public bool? IsStandardSelected { get; set; } = false;

    [SugarColumn(DefaultValue = "0")]
    public uint? PlayerGachaSeed { get; set; } = 0; // 改为 uint?

    // --- 2. 新手池 (4001) ---
    [SugarColumn(DefaultValue = "0")]
    public int? NewbieGachaCount { get; set; } = 0; // 改为 int?

    [SugarColumn(DefaultValue = "0")]
    public int? NewbiePityCount { get; set; } = 0;  // 改为 int?

    // --- 3. 限定角色池 (AvatarUp) ---
    [SugarColumn(DefaultValue = "0")]
    public int? LastAvatarGachaPity { get; set; } = 0; // 改为 int?

    [SugarColumn(DefaultValue = "0")]
    public bool? LastAvatarGachaFailed { get; set; } = false; // 改为 bool?

    // --- 4. 限定武器池 (WeaponUp) ---
    [SugarColumn(DefaultValue = "0")]
    public int? LastWeaponGachaPity { get; set; } = 0; // 改为 int?

    [SugarColumn(DefaultValue = "0")]
    public bool? LastWeaponGachaFailed { get; set; } = false; // 改为 bool?

    // --- 5. 通用/四星 ---
    public int LastGachaFailedCount { get; set; } = 0;

    [SugarColumn(DefaultValue = "0")]
    public int? LastGachaPurpleFailedCount { get; set; } = 0; // 改为 int?

    /// <summary> 
    /// 记录上次获得的4星对象是否为非UP。
    /// </summary>
    [SugarColumn(DefaultValue = "0")]
    public bool? LastPurpleGachaFailed { get; set; } = false; // 改为 bool?
    
    [SugarColumn(IsJson = true)] 
    public List<int> GachaDecideOrder { get; set; } = [];
}

public class GachaInfo
{
    public int GachaId { get; set; }
    public long Time { get; set; }
    public int ItemId { get; set; }
}