using EggLink.DanhengServer.Util;
using Newtonsoft.Json;

namespace EggLink.DanhengServer.Data.Excel;

// 指定对应的资源文件名
[ResourceEntity("CityShopConfig.json")]
public class CityShopConfigExcel : ExcelResource
{
    // 对应 JSON 中的 WorldID
    public int WorldID { get; set; }

    // 对应 JSON 中的 Name 对象 (Hash/Hash64)
    public HashName Name { get; set; } = new();

    // 对应 JSON 中的 WorldImgPath
    public string WorldImgPath { get; set; } = "";

    // 对应 JSON 中的 ShopID，作为唯一标识符
    public int ShopID { get; set; }

    // 该城市商店的最大等级
    public int MaxLevel { get; set; }

    // 升级所需的消耗货币 ItemID
    public int ItemID { get; set; }

    // 对应的奖励组 ID
    public int RewardListGroupID { get; set; }

    // 溢出提醒数值
    public int HintOverNum { get; set; }

    // 返回 ShopID 作为索引 ID
    public override int GetId()
    {
        return ShopID;
    }

    // 资源加载完成后的回调，将数据存入全局字典
    public override void Loaded()
    {
        if (!GameData.CityShopConfigData.ContainsKey(ShopID))
            GameData.CityShopConfigData.Add(ShopID, this);
    }
}
