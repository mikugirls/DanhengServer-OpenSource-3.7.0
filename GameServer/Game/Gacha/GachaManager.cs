using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Gacha;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Enums;
using EggLink.DanhengServer.Enums.Item;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.PlayerSync;
using EggLink.DanhengServer.Proto;
using GachaInfo = EggLink.DanhengServer.Database.Gacha.GachaInfo;
using System.Linq;

namespace EggLink.DanhengServer.GameServer.Game.Gacha;

public class GachaManager : BasePlayerManager
{
    public GachaManager(PlayerInstance player) : base(player)
    {
        GachaData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<GachaData>(player.Uid);

        if (GachaData.GachaHistory.Count >= 50)
            GachaData.GachaHistory.RemoveRange(0, GachaData.GachaHistory.Count - 50);

        foreach (var order in GameData.DecideAvatarOrderData.Values.ToList().OrderBy(x => -x.Order))
        {
            if (GachaData.GachaDecideOrder.Contains(order.ItemID)) continue;
            GachaData.GachaDecideOrder.Add(order.ItemID);
        }
    }

    public GachaData GachaData { get; }

    public List<int> GetPurpleAvatars()
    {
        var purpleAvatars = new List<int>();
        foreach (var avatar in GameData.AvatarConfigData.Values)
            if (avatar.Rarity == RarityEnum.CombatPowerAvatarRarityType4 &&
                !(GameData.MultiplePathAvatarConfigData.ContainsKey(avatar.AvatarID) &&
                  GameData.MultiplePathAvatarConfigData[avatar.AvatarID].BaseAvatarID != avatar.AvatarID) &&
                avatar.MaxRank > 0)
                purpleAvatars.Add(avatar.AvatarID);
        return purpleAvatars;
    }

    public List<int> GetGoldAvatars() => [1003, 1004, 1101, 1107, 1104, 1209, 1211];

    public List<int> GetAllGoldAvatars()
    {
        var avatars = new List<int>();
        foreach (var avatar in GameData.AvatarConfigData.Values)
            if (avatar.Rarity == RarityEnum.CombatPowerAvatarRarityType5)
                avatars.Add(avatar.AvatarID);
        return avatars;
    }

    public List<int> GetBlueWeapons()
    {
        var blueWeapons = new List<int>();
        foreach (var weapon in GameData.EquipmentConfigData.Values)
            if (weapon.Rarity == RarityEnum.CombatPowerLightconeRarity3)
                blueWeapons.Add(weapon.EquipmentID);
        return blueWeapons;
    }

    public List<int> GetPurpleWeapons()
    {
        var purpleWeapons = new List<int>();
        foreach (var weapon in GameData.EquipmentConfigData.Values)
            if (weapon.Rarity == RarityEnum.CombatPowerLightconeRarity4)
                purpleWeapons.Add(weapon.EquipmentID);
        return purpleWeapons;
    }

    public List<int> GetGoldWeapons() => [23000, 23002, 23003, 23004, 23005, 23012, 23013];

    public List<int> GetAllGoldWeapons()
    {
        var weapons = new List<int>();
        foreach (var weapon in GameData.EquipmentConfigData.Values)
            if (weapon.Rarity == RarityEnum.CombatPowerLightconeRarity5)
                weapons.Add(weapon.EquipmentID);
        return weapons;
    }

    public int GetRarity(int itemId)
    {
        if (GetAllGoldAvatars().Contains(itemId) || GetAllGoldWeapons().Contains(itemId)) return 5;
        if (GetPurpleAvatars().Contains(itemId) || GetPurpleWeapons().Contains(itemId)) return 4;
        if (GetBlueWeapons().Contains(itemId)) return 3;
        return 0;
    }

    public int GetType(int itemId)
    {
        if (GetAllGoldAvatars().Contains(itemId) || GetPurpleAvatars().Contains(itemId)) return 1;
        if (GetAllGoldWeapons().Contains(itemId) || GetPurpleWeapons().Contains(itemId) ||
            GetBlueWeapons().Contains(itemId)) return 2;
        return 0;
    }

   public async ValueTask<DoGachaScRsp?> DoGacha(int bannerId, int times)
{
    // 1. 获取卡池配置
    var banner = GameData.BannersConfig.Banners.Find(x => x.GachaId == bannerId);
    if (banner == null) return null;

    // --- 【核心修复：前置计算实际消耗】 ---
    // 根据池子 ID 判断，新手池(4001)十连抽仅消耗 8 张票
    int actualCost = (bannerId == 4001 && times == 10) ? 8 : times;
    int ticketId = (int)banner.GachaType.GetCostItemId();

    // --- 【核心修复：自动补票与 UI 强制预同步】 ---
    // 解决“买了票 UI 不亮，必须退出重进”的 BUG
    if (Player.InventoryManager!.GetItemCount(ticketId) < actualCost)
    {
        int deficit = actualCost - Player.InventoryManager.GetItemCount(ticketId);
        // 扣星琼 (注意：此处使用 sync: false，后面统一发包)
        var costHcoin = await Player.InventoryManager.RemoveItem(1, deficit * 160, sync: false);
        if (costHcoin == null) return null; // 余额不足则拦截

        // 补齐缺少的车票
        await Player.InventoryManager.AddItem(ticketId, deficit, sync: false);
        
        // 【关键逻辑】在正式抽卡响应前，先发一个同步包刷新客户端 UI 余额，让抽卡按钮变亮
        var preSync = new List<ItemData> {
            new ItemData { ItemId = 1, Count = (int)Player.Data.Hcoin },
            new ItemData { ItemId = ticketId, Count = (int)Player.InventoryManager.GetItemCount(ticketId) }
        };
        await Player.SendPacket(new PacketPlayerSyncScNotify(preSync));
    }

    // --- 【核心修复：安全扣除实际车票】 ---
    var removedTicket = await Player.InventoryManager.RemoveItem(ticketId, actualCost, sync: false);
    if (removedTicket == null) return null; 

    var decideItem = GachaData.GachaDecideOrder.Count >= 7 ? GachaData.GachaDecideOrder.GetRange(0, 7) : GachaData.GachaDecideOrder;
    
    // 执行抽卡循环
    var items = new List<int>();
    for (var i = 0; i < times; i++)
    {
        // 调用 BannerConfig.DoGacha：内部已实现 4001 保底修改和 1001 计数
        var item = banner.DoGacha(decideItem, GetPurpleAvatars(), GetPurpleWeapons(), GetGoldWeapons(),
            GetBlueWeapons(), GachaData, Player.Uid);
        
        // 容错处理：若新手池抽满或异常返回 0
        if (item == 0) break;
        items.Add(item);
    }

    var gachaItems = new List<GachaItem>();
    // 将扣除的车票加入同步列表，确保最终余额刷新
    var syncItems = new List<ItemData> { removedTicket };

    // --- 物品分发与副产物处理逻辑 ---
    foreach (var item in items)
    {
        var dirt = 0;
        var star = 0;
        var rarity = GetRarity(item);

        // 记录抽卡历史记录
        GachaData.GachaHistory.Add(new GachaInfo {
            GachaId = bannerId,
            ItemId = item,
            Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var gachaItem = new GachaItem();

        // 处理 5 星掉落（包含满命转化 281）
        if (rarity == 5)
        {
            var type = GetType(item);
            if (type == 1) // 角色
            {
                var avatar = Player.AvatarManager?.GetFormalAvatar(item);
                if (avatar != null)
                {
                    star += 40;
                    var rankUpItemId = item + 10000;
                    var rankUpItem = Player.InventoryManager!.GetItem(rankUpItemId);
                    if (avatar.PathInfos[item].Rank + (rankUpItem?.Count ?? 0) >= 6)
                    {
                        star += 60;
                        var item281 = await Player.InventoryManager!.AddItem(281, 1, false, sync: false, returnRaw: true);
                        if (item281 != null)
                        {
                            var old = syncItems.Find(x => x.ItemId == 281);
                            if (old == null) syncItems.Add(item281);
                            else old.Count = item281.Count;
                        }
                        var extraTransfer = new ItemList();
                        extraTransfer.ItemList_.Add(new Item { ItemId = 281, Num = 1 });
                        gachaItem.TransferItemList = extraTransfer;
                    }
                    else
                    {
                        var dupeItem = new ItemList();
                        dupeItem.ItemList_.Add(new Item { ItemId = (uint)rankUpItemId, Num = 1 });
                        gachaItem.TransferItemList = dupeItem;
                    }
                }
            }
            else star += 20; // 武器直接给星芒
        }
        else if (rarity == 4) // 处理 4 星掉落
        {
            var type = GetType(item);
            if (type == 1)
            {
                var avatar = Player.AvatarManager?.GetFormalAvatar(item);
                if (avatar != null)
                {
                    star += 8;
                    var rankUpItemId = item + 10000;
                    var rankUpItem = Player.InventoryManager!.GetItem(rankUpItemId);
                    if (avatar.PathInfos[item].Rank + (rankUpItem?.Count ?? 0) >= 6) star += 12;
                    else
                    {
                        var dupeItem = new ItemList();
                        dupeItem.ItemList_.Add(new Item { ItemId = (uint)rankUpItemId, Num = 1 });
                        gachaItem.TransferItemList = dupeItem;
                    }
                }
            }
            else star += 8;
        }
        else dirt += 20; // 3 星掉落给离晶

        // 发放本体物品
        if (GameData.ItemConfigData[item].ItemMainType == ItemMainTypeEnum.AvatarCard &&
            Player.AvatarManager!.GetFormalAvatar(item) == null)
        {
            await Player.AvatarManager!.AddAvatar(item, isGacha: true);
            // 发放对应的头像
            int headIconId = 200000 + item;
            var headIconItem = await Player.InventoryManager!.AddItem(headIconId, 1, false, sync: false, returnRaw: true);
            if (headIconItem != null) syncItems.Add(headIconItem);
        }
        else
        {
            var i = await Player.InventoryManager!.AddItem(item, 1, false, sync: false, returnRaw: true);
            if (i != null) syncItems.Add(i);
        }

        // 处理副产物同步 (251 离晶)
        if (dirt > 0)
        {
            var it = await Player.InventoryManager!.AddItem(251, dirt, false, sync: false, returnRaw: true);
            if (it != null)
            {
                var old = syncItems.Find(x => x.ItemId == 251);
                if (old == null) syncItems.Add(it);
                else old.Count = it.Count;
            }
            gachaItem.TokenItem ??= new ItemList();
            gachaItem.TokenItem.ItemList_.Add(new Item { ItemId = 251, Num = (uint)dirt });
        }

        // 处理副产物同步 (252 星芒)
        if (star > 0)
        {
            var it = await Player.InventoryManager!.AddItem(252, star, false, sync: false, returnRaw: true);
            if (it != null)
            {
                var old = syncItems.Find(x => x.ItemId == 252);
                if (old == null) syncItems.Add(it);
                else old.Count = it.Count;
            }
            gachaItem.TokenItem ??= new ItemList();
            gachaItem.TokenItem.ItemList_.Add(new Item { ItemId = 252, Num = (uint)star });
        }

        gachaItem.GachaItem_ = new Item { ItemId = (uint)item, Num = 1, Level = 1, Rank = 1 };
        gachaItems.Add(gachaItem);
    }

    // --- 【核心修复：全量同步与响应填充】 ---
    await Player.SendPacket(new PacketPlayerSyncScNotify(syncItems));
    
    var proto = new DoGachaScRsp 
    { 
        GachaId = (uint)bannerId, 
        GachaNum = (uint)times,
        Retcode = 0,
        // 填充混淆字段，让 UI 进度条和保底数字实时变动
        CeilingNum = (uint)GachaData.LastGachaFailedCount, 
        GDIFAAHIFBH = (uint)GachaData.LastGachaPurpleFailedCount,
        PENILHGLHHM = (uint)GachaData.StandardCumulativeCount // 同步 300 抽进度到 Tag 9
    };
    proto.GachaItemList.AddRange(gachaItems);

    return proto;
}

     public GetGachaInfoScRsp ToProto()
{
    // 生成随机种子
    var proto = new GetGachaInfoScRsp { GachaRandom = (uint)Random.Shared.Next(1000, 1999) };
    
    foreach (var banner in GameData.BannersConfig.Banners)
    {
        // --- 【核心修复 1：隐藏已抽完的新手池】 ---
        // 如果新手池(4001)的累计抽数已达到 50，则 continue 跳过，不加入列表
        if (banner.GachaId == 4001 && GachaData.NewbieGachaCount >= 50) 
        {
            continue;
        }

        // --- 【核心修复 2：传递 GachaData 引用】 ---
        // 调用你修改后的 ToInfo 签名，内部会自动填充 GachaCeiling 和混淆字段
        var info = banner.ToInfo(GetGoldAvatars(), Player.Uid, GachaData);
        
        proto.GachaInfoList.Add(info);
    }
    
    return proto;
}
}
