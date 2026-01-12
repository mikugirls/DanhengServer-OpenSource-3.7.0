using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Enums.Item;
using EggLink.DanhengServer.Enums.Mission;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Game.Shop;

public class ShopService(PlayerInstance player) : BasePlayerManager(player)
{
    public async ValueTask<List<ItemData>> BuyItem(int shopId, int goodsId, int count)
    {
        GameData.ShopConfigData.TryGetValue(shopId, out var shopConfig);
        if (shopConfig == null) return [];
        var goods = shopConfig.Goods.Find(g => g.GoodsID == goodsId);
        if (goods == null) return [];
        GameData.ItemConfigData.TryGetValue(goods.ItemID, out var itemConfig);
        if (itemConfig == null) return [];

        foreach (var cost in goods.CostList) await Player.InventoryManager!.RemoveItem(cost.Key, cost.Value * count);
        var items = new List<ItemData>();
        if (itemConfig.ItemMainType is ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic)
        {
            for (var i = 0; i < count; i++)
            {
                var item = await Player.InventoryManager!.AddItem(itemConfig.ID, 1, false);
                if (item != null) items.Add(item);
            }
        }
       else
        {
            var item = await Player.InventoryManager!.AddItem(itemConfig.ID, count, false);
            if (item != null)
            {
                // 获取物品配置以判断类型
                GameData.ItemConfigData.TryGetValue(item.ItemId, out var subItemConfig);

                // --- 修改这里：如果是自动使用物品，且“不是”配方，才自动用掉 ---
                if (GameData.ItemUseDataData.TryGetValue(item.ItemId, out var useData) && 
                    useData.IsAutoUse && 
                    subItemConfig?.ItemSubType != ItemSubTypeEnum.Formula) 
                {
                    var res = await Player.InventoryManager!.UseItem(item.ItemId);
                    if (res.returnItems != null) items.AddRange(res.returnItems);
                }
                else
                {
                    // 配方或非自动使用物品，直接进入返回列表（即进入背包）
                    items.Add(item);
                }
            }
        }

        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.BuyShopGoods, goods);

        return items;
    }
}