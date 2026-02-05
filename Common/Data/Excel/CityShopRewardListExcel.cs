using EggLink.DanhengServer.Util;
using Newtonsoft.Json;

namespace EggLink.DanhengServer.Data.Excel;

// 指定对应的资源文件名
[ResourceEntity("CityShopRewardList.json")]
public class CityShopRewardListExcel : ExcelResource
{
    // 对应 JSON 中的 RewardID (领取的奖励 ID)
    public int RewardID { get; set; }

    // 对应 JSON 中的 ItemNeed (当前等级升级所需消耗的货币数量)
    public int ItemNeed { get; set; }

    // 对应 JSON 中的 Level (城市商店等级)
    public int Level { get; set; }

    // 对应 JSON 中的 TotalItem (达到该等级累计需要的货币总数)
    public int TotalItem { get; set; }

    // 对应 JSON 中的 GroupID (关联 CityShopConfigExcel 中的 RewardListGroupID)
    public int GroupID { get; set; }

    // 获取唯一标识，由于 JSON 结构没有单一主键，这里使用自增或组合逻辑
    // 但为了符合框架，通常建议在 GameData 中使用复合 Key
    public override int GetId()
    {
        // 简单处理可以使用 HashCode，或者在 Loaded 中处理
        return (GroupID << 16) | Level;
    }

    public override void Loaded()
    {
        // 1. 存入扁平化字典
        int id = GetId();
        if (!GameData.CityShopRewardListData.ContainsKey(id))
            GameData.CityShopRewardListData.Add(id, this);

        // 2. 存入分组字典 (GroupID -> List<RewardConfig>) 方便按商店查找所有等级
        if (!GameData.CityShopRewardGroupData.TryGetValue(GroupID, out var list))
        {
            list = [];
            GameData.CityShopRewardGroupData.Add(GroupID, list);
        }
        list.Add(this);
    }
}
