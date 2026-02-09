
using SqlSugar;

namespace EggLink.DanhengServer.Database.Shop;

[SugarTable("CityShop")]
public class CityShopData : BaseDatabaseDataHelper
{
    /// <summary> 
    /// 城市商店经验字典: Key 为 ShopID, Value 为该商店累计消耗的代币总数 (Exp) 
    /// </summary>
    [SugarColumn(IsJson = true)]
    public Dictionary<int, uint> CityShopExpMap { get; set; } = [];

    /// <summary> 
    /// 城市商店领奖记录字典: Key 为 ShopID, Value 为已领取等级奖励的位掩码 (Bitmask)
    /// 例如：第 1 位 (1 << 0) 代表等级 1，第 2 位 (1 << 1) 代表等级 2
    /// </summary>
    [SugarColumn(IsJson = true)]
    public Dictionary<int, ulong> CityShopRewardMaskMap { get; set; } = [];

    /// <summary>
    /// 获取指定商店的当前累计经验
    /// </summary>
    public uint GetExp(int shopId)
    {
        return CityShopExpMap.GetValueOrDefault(shopId, 0u);
    }

    /// <summary>
    /// 获取指定商店的奖励领取位掩码
    /// </summary>
    public ulong GetRewardMask(int shopId)
    {
        return CityShopRewardMaskMap.GetValueOrDefault(shopId, 0uL);
    }

    /// <summary>
    /// 设置/更新经验
    /// </summary>
    public void SetExp(int shopId, uint exp)
    {
        CityShopExpMap[shopId] = exp;
    }

    /// <summary>
    /// 标记某个等级已领奖
    /// </summary>
    public void MarkRewardTaken(int shopId, uint level)
    {
        if (level == 0 || level > 64) return;
        
        ulong mask = GetRewardMask(shopId);
        mask |= (1UL << (int)(level - 1));
        CityShopRewardMaskMap[shopId] = mask;
    }
    /// <summary>
    /// 检查指定等级的奖励是否已领 (配合 MarkRewardTaken 使用)
    /// </summary>
    public bool IsRewardTaken(int shopId, uint level)
    {
        if (level == 0 || level > 64) return true; // 越界处理
        
        ulong mask = GetRewardMask(shopId);
        // 检查掩码的第 (level-1) 位是否为 1
        return (mask & (1UL << (int)(level - 1))) != 0;
    }
}
