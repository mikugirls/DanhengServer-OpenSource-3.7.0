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
    // 1. 获取基础配置
    GameData.ShopConfigData.TryGetValue(shopId, out var shopConfig);
    if (shopConfig == null) return [];
    var goods = shopConfig.Goods.Find(g => g.GoodsID == goodsId);
    if (goods == null) return [];
    GameData.ItemConfigData.TryGetValue(goods.ItemID, out var itemConfig);
    if (itemConfig == null) return [];

    // 2. 扣除货币 (注意：RemoveItem 内部已经发了 Player.ToProto 同步顶栏)
    foreach (var cost in goods.CostList) 
    {
        await Player.InventoryManager!.RemoveItem(cost.Key, cost.Value * count);
    }

    var items = new List<ItemData>();

    // 3. 处理装备/遗器（此类物品具有唯一性，需要循环添加）
    if (itemConfig.ItemMainType is ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic)
    {
        for (var i = 0; i < count; i++)
        {
            // 添加时不触发同步，最后统一处理
            var item = await Player.InventoryManager!.AddItem(itemConfig.ID, 1, notify: false, sync: false, returnRaw: true);
            if (item != null) items.Add(item.Clone()); 
        }
    }
    // 4. 处理普通物品/自动使用物品
    else
    {
        // 先尝试添加物品到背包，拿到数据库引用
        var item = await Player.InventoryManager!.AddItem(itemConfig.ID, count, notify: false, sync: false, returnRaw: true);
        
        if (item != null)
        {
            // --- 关键逻辑：处理自动使用（例如礼包、箱子、燃料等） ---
            if (GameData.ItemUseDataData.TryGetValue(item.ItemId, out var useData) && 
                useData.IsAutoUse && 
                itemConfig.ItemSubType != ItemSubTypeEnum.Formula) 
            {
                // 如果是自动使用物品，调用 UseItem（传入总数 count）
                // 注意：UseItem 内部会执行 HandleReward 并扣除该物品
                var res = await Player.InventoryManager!.UseItem(item.ItemId, count);
                
                // 将礼包开出的奖励加入返回列表
                if (res.returnItems != null) items.AddRange(res.returnItems);
            }
            else
            {
                // 如果是非自动使用物品（如普通材料、配方），将其克隆后存入返回列表
                var displayItem = item.Clone();
                displayItem.Count = count; // 保证 UI 显示购买的数量
                items.Add(displayItem);
            }
        }
    }

    // 5. 统一数据同步与通知
    if (items.Count > 0)
    {
        // 同步背包最新状态
        await Player.SendPacket(new EggLink.DanhengServer.GameServer.Server.Packet.Send.PlayerSync.PacketPlayerSyncScNotify(items));
        
        // 弹出右侧获得物品提示（合并所有购买/礼包开出的东西）
        await Player.SendPacket(new EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene.PacketScenePlaneEventScNotify(items));
    }

    // 6. 触发任务进度
    await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.BuyShopGoods, goods);

    return items;
}
}