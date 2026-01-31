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
using EggLink.DanhengServer.Util;
namespace EggLink.DanhengServer.GameServer.Game.Gacha;

public class GachaManager : BasePlayerManager
{
    public GachaManager(PlayerInstance player) : base(player)
    {
        GachaData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<GachaData>(player.Uid);
		

        // --- 新增：初始化玩家专属种子 ---
        if (GachaData.PlayerGachaSeed == 0)
        {
            // 使用 System.Security.Cryptography 生成物理级随机种子
            GachaData.PlayerGachaSeed = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        }

      
        if (GachaData.GachaHistory.Count >= 50)
            GachaData.GachaHistory.RemoveRange(0, GachaData.GachaHistory.Count - 50);

        foreach (var order in GameData.DecideAvatarOrderData.Values.ToList().OrderBy(x => -x.Order))
        {
            if (GachaData.GachaDecideOrder.Contains(order.ItemID)) continue;
            GachaData.GachaDecideOrder.Add(order.ItemID);
        }
    }

    public GachaData GachaData { get; }

    // --- 基础池获取方法 (保持不变) ---
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
    // 1. 日志与卡池验证
    Console.WriteLine($"\n[GACHA_DEBUG] 收到抽卡请求 -> UID: {Player.Uid} | Banner: {bannerId} | Times: {times}");

    var banner = GameData.BannersConfig.Banners.Find(x => x.GachaId == bannerId);
    if (banner == null) return null;

    if (bannerId == 4001 && (times != 10 || GachaData.NewbieGachaCount > 50))
    {
        Console.WriteLine("[GACHA_WARNING] 新手池请求非法");
        return null;
    }

    // 2. 补票与扣费逻辑 (静默扣除 sync: false)
    int actualCost = (bannerId == 4001 && times == 10) ? 8 : times;
    int ticketId = (int)banner.GachaType.GetCostItemId();

    // 核心改进：使用 Dictionary 收集所有需要同步的物品，Key 为 string(防止ID/UniqueId冲突)
    var syncMap = new Dictionary<string, ItemData>();

    if (Player.InventoryManager!.GetItemCount(ticketId) < actualCost)
    {
        int deficit = actualCost - Player.InventoryManager.GetItemCount(ticketId);
        var costHcoin = await Player.InventoryManager.RemoveItem(1, deficit * 160, sync: false);
        if (costHcoin != null) syncMap["1"] = costHcoin; // 记录扣除后的星琼状态
        
        await Player.InventoryManager.AddItem(ticketId, deficit, sync: false);
    }
    var ticket = await Player.InventoryManager.RemoveItem(ticketId, actualCost, sync: false);
    if (ticket != null) syncMap[ticketId.ToString()] = ticket; // 记录券的状态

    // 3. 执行抽卡内核
    var decideItem = GachaData.GachaDecideOrder.Count >= 7 ? GachaData.GachaDecideOrder.GetRange(0, 7) : GachaData.GachaDecideOrder;
    var resultIds = new List<int>();
	
	// 【关键修改】：使用玩家持久化种子创建随机实例
    var playerRand = new Random((int)GachaData.PlayerGachaSeed);
	
	for (var i = 0; i < times; i++)
    {
        // 传入 playerRand，让 BannerConfig 内部使用它进行判定
        var item = banner.DoGacha(
            decideItem, 
            GetPurpleAvatars(), 
            GetPurpleWeapons(), 
            GetGoldWeapons(), 
            GetBlueWeapons(), 
            GachaData, 
            playerRand // 这里传入实例
        );
        
        if (item == 0) break;
        resultIds.Add(item);
    }
	
	// 【重要】：演化种子并存回数据库。这保证了下一次抽卡的起点不可预测。
    GachaData.PlayerGachaSeed = (uint)playerRand.Next(1, int.MaxValue);

    // 4. 处理物品发放与副产物
    var gachaItems = new List<GachaItem>();

    foreach (var item in resultIds)
    {
        var rarity = GetRarity(item);
        var gItem = new GachaItem { GachaItem_ = new Item { ItemId = (uint)item, Num = 1, Level = 1, Rank = 1 } };
        gItem.TransferItemList = new ItemList();

        int star = 0, dirt = 0;
        if (rarity == 5) star = 20; 
        else if (rarity == 4) star = 8;
        else dirt = 20;

        GachaData.GachaHistory.Add(new GachaInfo { GachaId = bannerId, ItemId = item, Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

        // A. 角色入库逻辑
        if (GameData.ItemConfigData[item].ItemMainType == ItemMainTypeEnum.AvatarCard)
        {
            var avatar = Player.AvatarManager?.GetFormalAvatar(item);
            if (avatar == null)
            {
                await Player.AvatarManager!.AddAvatar(item, isGacha: true);
                var headIcon = await Player.InventoryManager.AddItem(200000 + item, 1, false, sync: false, returnRaw: true);
                if (headIcon != null) syncMap[(200000 + item).ToString()] = headIcon;
            }
            else
            {
                var rankUpItemId = item + 10000;
                var rankUpItem = await Player.InventoryManager.AddItem(rankUpItemId, 1, false, sync: false, returnRaw: true);
                if (rankUpItem != null) syncMap[rankUpItemId.ToString()] = rankUpItem;
                gItem.TransferItemList.ItemList_.Add(new Item { ItemId = (uint)rankUpItemId, Num = 1 });

                if (avatar.PathInfos[item].Rank + (rankUpItem?.Count ?? 0) >= 6)
                {
                    star += (rarity == 5) ? 60 : 12;
                    if (rarity == 5)
                    {
                        var rareItem = await Player.InventoryManager.AddItem(281, 1, false, sync: false, returnRaw: true);
                        if (rareItem != null) syncMap["281"] = rareItem;
                        gItem.TransferItemList.ItemList_.Add(new Item { ItemId = 281, Num = 1 });
                    }
                }
            }
        }
        else
        {
            // B. 光锥入库：【修复关键】必须获取 returnRaw 拿到 UniqueId
            var weapon = await Player.InventoryManager.AddItem(item, 1, false, sync: false, returnRaw: true);
            if (weapon != null)
            {
                // 武器是唯一物品，必须用 UniqueId 作为标识符放入同步列表
                syncMap[$"weapon_{weapon.UniqueId}"] = weapon; 
            }
        }

        // C. 发放副产物
        if (star > 0)
        {
            var sItem = await Player.InventoryManager.AddItem(252, star, false, sync: false, returnRaw: true);
            if (sItem != null) syncMap["252"] = sItem;
            gItem.TokenItem ??= new ItemList { ItemList_ = { new Item { ItemId = 252, Num = (uint)star } } };
        }
        if (dirt > 0)
        {
            var dItem = await Player.InventoryManager.AddItem(251, dirt, false, sync: false, returnRaw: true);
            if (dItem != null) syncMap["251"] = dItem;
            gItem.TokenItem ??= new ItemList { ItemList_ = { new Item { ItemId = 251, Num = (uint)dirt } } };
        }

        gachaItems.Add(gItem);
    }

    // 5. 【终极同步】一次性发送所有更新过的数据包
    // 包含：扣费、所有抽到的武器(带UniqueId)、增加后的材料总量
    await Player.SendPacket(new PacketPlayerSyncScNotify(syncMap.Values.ToList()));

    // 6. 构造回包
    var proto = new DoGachaScRsp {
        GachaId = (uint)bannerId,
        GachaNum = (uint)times,
        Retcode = 0,
        GDIFAAHIFBH = (uint)GachaData.NewbieGachaCount,
        CeilingNum = (uint)GachaData.StandardCumulativeCount,
        PENILHGLHHM = (uint)GachaData.StandardCumulativeCount
    };
    proto.GachaItemList.AddRange(gachaItems);

    return proto;
}

  
    

    public GetGachaInfoScRsp ToProto()
    {
       // 将当前数据库中的 PlayerGachaSeed 发送给客户端
        // 客户端将根据此种子预渲染抽卡表现
        var proto = new GetGachaInfoScRsp { GachaRandom = GachaData.PlayerGachaSeed };
        foreach (var banner in GameData.BannersConfig.Banners)
        {
            if (banner.GachaId == 4001 && GachaData.NewbieGachaCount > 50) continue;
            proto.GachaInfoList.Add(banner.ToInfo(GetGoldAvatars(), Player.Uid, GachaData));
        }
        return proto;
    }
}