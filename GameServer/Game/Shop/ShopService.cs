using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Enums.Item;
using EggLink.DanhengServer.Enums.Mission;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Util; // 引用 GlobalDebug
// 在 ShopService.cs 顶部添加
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Shop;
using EggLink.DanhengServer.Database; // 添加这一行
using EggLink.DanhengServer.Proto;
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
        // 5.5. 城市商店经验逻辑 (新增)
        // 检查该商店是否属于城市商店配置
        if (GameData.CityShopConfigData.TryGetValue(shopId, out var cityConfig))
        {
            // 遍历刚才购买消耗的货币，寻找是否包含该城市指定的代币 (ItemID)
            foreach (var cost in goods.CostList)
            {
                if (cost.Key == cityConfig.ItemID)
                {
                    // 计算本次增加的经验：单价 * 购买数量
                    uint addExp = (uint)(cost.Value * count);
                    
                    // 从你刚才定义的独立数据库类 CityShopData 中获取并更新
                    uint oldExp = Player.CityShopData!.GetExp(shopId);
                    uint newExp = oldExp + addExp;
                    
                    Player.CityShopData.SetExp(shopId, newExp);
                    DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);

                    if (GlobalDebug.EnableVerboseLog)
                        Console.WriteLine($"[SHOP_DEBUG] 城市商店经验增加 | ShopID: {shopId} | 代币: {cost.Key} | +{addExp} | 当前总经验: {newExp}");

                    // 发送 1594 协议通知：让客户端左侧进度条和等级实时刷新
                    await Player.SendPacket(new PacketCityShopInfoScNotify(Player, shopId));
                    break; 
                }
            }
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
  // 1. 获取当前总经验能达到的【物理最高上限】
    // 比如：124经验，1级门槛10，2级门槛100，这里返回 2
    public uint GetPhysicalMaxLevel(int shopId, uint totalExp)
    {
        if (!GameData.CityShopConfigData.TryGetValue(shopId, out var config)) return 0;
        if (!GameData.CityShopRewardGroupData.TryGetValue(config.RewardListGroupID, out var rewards)) return 0;

        uint max = 0;
        foreach (var r in rewards.OrderBy(x => x.TotalItem))
        {
            if (totalExp >= r.TotalItem) max = (uint)r.Level;
            else break;
        }
        return max;
    }

    // 2. 领奖逻辑：现在配合位掩码偏移 (1 << level)
    public async Task<TakeCityShopRewardScRsp> TakeCityShopReward(uint shopId, uint level)
    {
        var rsp = new TakeCityShopRewardScRsp { ShopId = shopId, Level = level, Retcode = (uint)Retcode.RetSucc, Reward = new ItemList() };

        if (Player.CityShopData == null) return new TakeCityShopRewardScRsp { Retcode = (uint)Retcode.RetFail };

        // [关键修改] A. 经验校验：只要当前经验对应的物理等级 >= 请求等级即可
        uint totalExp = Player.CityShopData.GetExp((int)shopId);
        uint physicalMax = GetPhysicalMaxLevel((int)shopId, totalExp);
        if (level > physicalMax) 
        {
            rsp.Retcode = (uint)Retcode.RetCityLevelNotMeet;
            return rsp;
        }

        // [关键修改] B. 顺序校验：如果要领第 N 级，第 N-1 级必须已经领过
        // 注意：这里 IsRewardTaken 内部已经改成了 (1 << level) 逻辑
        if (level > 1 && !Player.CityShopData.IsRewardTaken((int)shopId, level - 1)) 
        {
            if (Util.GlobalDebug.EnableVerboseLog)
                Console.WriteLine($"[SHOP] 拦截跳级领取: 请先领取第 {level - 1} 级");
            rsp.Retcode = (uint)Retcode.RetCityLevelNotMeet; // 或者自定义错误码
            return rsp;
        }

        // C. 重复领取校验
        if (Player.CityShopData.IsRewardTaken((int)shopId, level)) 
        {
            rsp.Retcode = (uint)Retcode.RetCityLevelRewardTaken;
            return rsp;
        }

        // D. 执行发奖 (保持你原来的复合主键逻辑，这是对的)
        if (GameData.CityShopConfigData.TryGetValue((int)shopId, out var config))
        {
            int compositeKey = (config.RewardListGroupID << 16) | (int)level;
            if (GameData.CityShopRewardListData.TryGetValue(compositeKey, out var rewardEntry))
            {
                if (rewardEntry.RewardID > 0)
                {
                    var items = await Player.InventoryManager!.HandleReward(rewardEntry.RewardID, notify: false, sync: true);
                    if (items != null)
                    {
                        foreach (var item in items)
                            rsp.Reward.ItemList_.Add(new Item { ItemId = (uint)item.ItemId, Num = (uint)item.Count });
                    }
                }
            }
        }

        // [关键修改] E. 标记并保存 (内部使用 1 << level)
        Player.CityShopData.MarkRewardTaken((int)shopId, level);
        Database.DatabaseHelper.ToSaveUidList.SafeAdd(Player.Uid);
        
        return rsp;
    }
	    
}
