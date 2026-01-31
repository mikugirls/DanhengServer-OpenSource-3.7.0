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
    
    bool isDirty = false;

    // 1. 初始化玩家种子
    if ((GachaData.PlayerGachaSeed ?? 0) == 0)
    {
        GachaData.PlayerGachaSeed = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        isDirty = true;
    }

    // 2. 修复数值类保底字段 (int?)
    // 使用模式匹配或 ?? 运算符可以更优雅
    if (GachaData.StandardPityCount == null) { GachaData.StandardPityCount = 0; isDirty = true; }
    if (GachaData.NewbiePityCount == null) { GachaData.NewbiePityCount = 0; isDirty = true; }
    if (GachaData.NewbieGachaCount == null) { GachaData.NewbieGachaCount = 0; isDirty = true; }
    if (GachaData.LastAvatarGachaPity == null) { GachaData.LastAvatarGachaPity = 0; isDirty = true; }
    if (GachaData.LastWeaponGachaPity == null) { GachaData.LastWeaponGachaPity = 0; isDirty = true; }
    if (GachaData.LastGachaPurpleFailedCount == null) { GachaData.LastGachaPurpleFailedCount = 0; isDirty = true; }

    // 3. 修复布尔类字段 (bool?)
    // 警告触发点：如果字段定义为 bool 而非 bool?，则不需要检查 null
    if (GachaData.LastAvatarGachaFailed == null) { GachaData.LastAvatarGachaFailed = false; isDirty = true; }
    if (GachaData.LastWeaponGachaFailed == null) { GachaData.LastWeaponGachaFailed = false; isDirty = true; }
    if (GachaData.LastPurpleGachaFailed == null) { GachaData.LastPurpleGachaFailed = false; isDirty = true; }
    
    // 特别处理 IsStandardSelected
    // 如果它是 bool?，逻辑照旧；如果是 bool，删除此 null 检查
    // 假设你已将其改为 bool? 以对齐其他保底字段
    if (GachaData.IsStandardSelected == null) { GachaData.IsStandardSelected = false; isDirty = true; }

    // 执行更新
    if (isDirty) 
    {
        DatabaseHelper.UpdateInstance(GachaData);
        Console.WriteLine($"[GACHA_REPAIR] 已自动修复 UID:{player.Uid} 的数据库记录");
    }

    // 历史记录清理
    if (GachaData.GachaHistory.Count >= 50)
        GachaData.GachaHistory.RemoveRange(0, GachaData.GachaHistory.Count - 50);

    // 填充抽卡池权重顺序
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
        Console.WriteLine($"\n[GACHA_DEBUG] 收到抽卡请求 -> UID: {Player.Uid} | Banner: {bannerId} | Times: {times}");

        var banner = GameData.BannersConfig.Banners.Find(x => x.GachaId == bannerId);
        if (banner == null) return null;

       // --- 修正2：验证新手池限制 (处理 NULL) ---
        if (bannerId == 4001 && (times != 10 || (GachaData.NewbieGachaCount ?? 0) > 50))
            return null;

        DoGachaScRsp? finalResponse = null;
        var syncMap = new Dictionary<string, ItemData>();

        // --- 2. 开启 SqlSugar 事务保护 ---
        var tranResult = await DatabaseHelper.sqlSugarScope!.UseTranAsync(async () =>
        {
            // A. 扣费逻辑
            int actualCost = (bannerId == 4001 && times == 10) ? 8 : times;
            int ticketId = (int)banner.GachaType.GetCostItemId();

            if (Player.InventoryManager!.GetItemCount(ticketId) < actualCost)
            {
                int deficit = actualCost - Player.InventoryManager.GetItemCount(ticketId);
                var costHcoin = await Player.InventoryManager.RemoveItem(1, deficit * 160, sync: false);
                if (costHcoin != null) syncMap["1"] = costHcoin;
                await Player.InventoryManager.AddItem(ticketId, deficit, sync: false);
            }
            var ticket = await Player.InventoryManager.RemoveItem(ticketId, actualCost, sync: false);
            if (ticket != null) syncMap[ticketId.ToString()] = ticket;

            // B. 抽卡内核演化
            var decideItem = GachaData.GachaDecideOrder.Count >= 7 ? GachaData.GachaDecideOrder.GetRange(0, 7) : GachaData.GachaDecideOrder;
            var resultIds = new List<int>();
            
            // 使用当前数据库种子初始化序列
            var playerRand = new Random((int)(GachaData.PlayerGachaSeed ?? 0));

            for (var i = 0; i < times; i++)
            {
                var item = banner.DoGacha(decideItem, GetPurpleAvatars(), GetPurpleWeapons(), GetGoldWeapons(), GetBlueWeapons(), GachaData, playerRand);
                if (item == 0) break;
                resultIds.Add(item);
            }

            // 【关键】更新种子并即时更新数据库，防止SL回档
            GachaData.PlayerGachaSeed = (uint)playerRand.Next(1, int.MaxValue);
            DatabaseHelper.UpdateInstance(GachaData);

            // C. 物品发放与副产物处理
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
                    var weapon = await Player.InventoryManager.AddItem(item, 1, false, sync: false, returnRaw: true);
                    if (weapon != null) syncMap[$"weapon_{weapon.UniqueId}"] = weapon;
                }

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

           // D. 构造回包 - 增加卡池类型判定
			finalResponse = new DoGachaScRsp {
			GachaId = (uint)bannerId,
			GachaNum = (uint)times,
			Retcode = 0,
			GDIFAAHIFBH = (uint)(GachaData.NewbieGachaCount ?? 0)
			};

			// 只有常驻池才同步 300 抽天井进度
			if (bannerId == 1001) {
			finalResponse.CeilingNum = (uint)GachaData.StandardCumulativeCount;
			finalResponse.PENILHGLHHM = (uint)GachaData.StandardCumulativeCount;
			}

			finalResponse.GachaItemList.AddRange(gachaItems);
			});

        // --- 3. 事务提交结果判定 ---
        if (tranResult.IsSuccess)
        {
            await Player.SendPacket(new PacketPlayerSyncScNotify(syncMap.Values.ToList()));
            return finalResponse;
        }
        else
        {
            Console.WriteLine($"[GACHA_CRITICAL] 事务回滚! UID: {Player.Uid} Error: {tranResult.ErrorMessage}");
            return null;
        }
    }

  
    

    public GetGachaInfoScRsp ToProto()
    {
       // 将当前数据库中的 PlayerGachaSeed 发送给客户端
        // 客户端将根据此种子预渲染抽卡表现
        var proto = new GetGachaInfoScRsp { GachaRandom = GachaData.PlayerGachaSeed ?? 0 };
        foreach (var banner in GameData.BannersConfig.Banners)
        {
            if (banner.GachaId == 4001 && GachaData.NewbieGachaCount > 50) continue;
            proto.GachaInfoList.Add(banner.ToInfo(GetGoldAvatars(), Player.Uid, GachaData));
        }
        return proto;
    }
}