using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Enums.Item;
using EggLink.DanhengServer.Enums.Mission;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Util; // 引用 GlobalDebug

namespace EggLink.DanhengServer.GameServer.Game.Shop;

public class ShopService(PlayerInstance player) : BasePlayerManager(player)
{
    public async ValueTask<List<ItemData>> BuyItem(int shopId, int goodsId, int count)
    {
        if (GlobalDebug.EnableVerboseLog)
            Console.WriteLine($"\n[SHOP_DEBUG] >>> 购买请求 | UID: {Player.Uid} | ShopID: {shopId} | GoodsID: {goodsId} | Count: {count}");

        GameData.ShopConfigData.TryGetValue(shopId, out var shopConfig);
        if (shopConfig == null) return [];
        var goods = shopConfig.Goods.Find(g => g.GoodsID == goodsId);
        if (goods == null) return [];
        GameData.ItemConfigData.TryGetValue(goods.ItemID, out var itemConfig);
        if (itemConfig == null) return [];

        // 1. 扣除货币
        foreach (var cost in goods.CostList) 
        {
            int before = (int)Player.InventoryManager!.GetItemCount(cost.Key);
            await Player.InventoryManager!.RemoveItem(cost.Key, cost.Value * count, sync: false);
            int after = (int)Player.InventoryManager!.GetItemCount(cost.Key);

            if (GlobalDebug.EnableVerboseLog)
                Console.WriteLine($"[SHOP_DEBUG] 货币变动 | ID: {cost.Key} | 扣除前: {before} | 扣除后: {after} | 理论消耗: {cost.Value * count}");
        }

        var displayItems = new List<ItemData>(); // 专门用于右侧弹窗显示的列表 (增量)
        var databaseItems = new List<ItemData>(); // 专门用于记录数据库引用的列表 (总量)

        // 2. 装备类处理
        if (itemConfig.ItemMainType is ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic)
        {
            for (var i = 0; i < count; i++)
            {
                var item = await Player.InventoryManager!.AddItem(itemConfig.ID, 1, notify: false, sync: false, returnRaw: true);
                if (item != null) 
                {
                    databaseItems.Add(item);
                    displayItems.Add(item.Clone()); 
                }
            }
        }
        // 3. 普通物品处理
        else
        {
            var item = await Player.InventoryManager!.AddItem(itemConfig.ID, count, notify: false, sync: false, returnRaw: true);
            
            if (item != null)
            {
                if (GlobalDebug.EnableVerboseLog)
                    Console.WriteLine($"[SHOP_DEBUG] AddItem执行结果 | 目标ItemID: {item.ItemId} | 增加量: {count} | 数据库当前总量: {item.Count}");

                if (GameData.ItemUseDataData.TryGetValue(item.ItemId, out var useData) && 
                    useData.IsAutoUse && 
                    itemConfig.ItemSubType != ItemSubTypeEnum.Formula) 
                {
                    var res = await Player.InventoryManager!.UseItem(item.ItemId, count);
                    if (res.returnItems != null) displayItems.AddRange(res.returnItems);
                }
                else
                {
                    // 记录数据库引用（带总量）用于后续同步
                    databaseItems.Add(item);

                    // 创建克隆体用于弹窗（带增量）
                    var gachaDisplay = item.Clone();
                    gachaDisplay.Count = count; 
                    displayItems.Add(gachaDisplay);

                    if (GlobalDebug.EnableVerboseLog)
                        Console.WriteLine($"[SHOP_DEBUG] 分离数据 | 弹窗包Count:{gachaDisplay.Count} | 同步包准备Count:{item.Count}");
                }
            }
        }

        // 5. 统一数据同步与通知
        if (displayItems.Count > 0)
        {
            // --- 构造背包同步包 (PacketPlayerSyncScNotify) ---
            // 这里必须填最终【总量】，否则背包会缩水
            var syncList = new List<ItemData>();
            
            // 物品总量同步
            foreach (var dbItem in databaseItems)
            {
                syncList.Add(new ItemData 
                { 
                    ItemId = dbItem.ItemId, 
                    Count = (int)Player.InventoryManager!.GetItemCount(dbItem.ItemId) 
                });
            }

            // 货币余额同步
            foreach (var cost in goods.CostList)
            {
                syncList.Add(new ItemData 
                { 
                    ItemId = cost.Key, 
                    Count = (int)Player.InventoryManager!.GetItemCount(cost.Key) 
                });
            }

            // 发送同步包：刷新顶栏货币和背包总数
            await Player.SendPacket(new EggLink.DanhengServer.GameServer.Server.Packet.Send.PlayerSync.PacketPlayerSyncScNotify(syncList));
            
            // --- 发送事件通知包 (PacketScenePlaneEventScNotify) ---
            // 发送 displayItems：它里面存的是增量(count)，右侧会正确显示 x1 或 x10
            await Player.SendPacket(new EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene.PacketScenePlaneEventScNotify(displayItems));
            
            if (GlobalDebug.EnableVerboseLog)
                Console.WriteLine($"[SHOP_DEBUG] 同步完成 | 背包更新项数:{syncList.Count} | 弹窗显示项数:{displayItems.Count}");
        }

        // 6. 任务进度触发
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.BuyShopGoods, goods);

        return displayItems;
    }
    // --- 核心计算逻辑 ---
    public uint CalculateCityLevel(int shopId)
    {
        // 1. 从刚才定义的数据库类中拿总经验
        uint totalExp = Player.CityShopData?.GetExp(shopId) ?? 0;

        // 2. 获取配置
        if (!GameData.CityShopConfigData.TryGetValue(shopId, out var config)) return 1;
        if (!GameData.CityShopRewardGroupData.TryGetValue(config.RewardListGroupID, out var rewards)) return 1;

        // 3. 计算当前等级
        uint level = 1;
        foreach (var reward in rewards.OrderBy(x => x.Level))
        {
            if (totalExp >= reward.TotalItem) level = (uint)reward.Level;
            else break;
        }
        return level;
    }
    
}
