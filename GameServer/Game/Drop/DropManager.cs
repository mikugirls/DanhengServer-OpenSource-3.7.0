using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Enums.Scene;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Rogue;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.BattleCollege;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Game.Drop;

public class DropManager(PlayerInstance player) : BasePlayerManager(player)
{
    /// <summary>
    /// 战斗奖励统一结算入口
    /// </summary>
    public async ValueTask ProcessBattleRewards(BattleInstance battle, PVEBattleResultCsReq req)
    {
        Console.WriteLine($"\n[Drop-Debug] >>>>>>>>>> 战斗结算链路启动 <<<<<<<<<<");
        
        if (req.EndStatus != BattleEndStatus.BattleEndWin) 
        {
            Console.WriteLine($"[Drop-Debug] 战斗未获胜，跳过奖励发放。");
            return;
        }

        // 防止同一组怪重复触发
        var processedGroups = new HashSet<int>();

        foreach (var monster in battle.EntityMonsters) 
        {
            await monster.Kill(false); 

            // 1. 拦截副本模式 (既不给奖励，也不解锁)
            if (battle.MappingInfoId > 0) continue; 

            // 2. 大世界掉落 (仅 Unknown/Maze)
            if (Player.SceneInstance?.GameModeType == GameModeTypeEnum.Unknown || Player.SceneInstance?.GameModeType == GameModeTypeEnum.Maze)
            {
                var dropId = monster.MonsterData.ID * 10 + Player.Data.WorldLevel;
                if (GameData.MonsterDropData.TryGetValue(dropId, out var dropData))
                {
                    var items = dropData.CalculateDrop();
                    await Player.InventoryManager!.AddItems(items, false);
                    battle.MonsterDropItems.AddRange(items);
                }
            }

            // 3. 同组宝箱解封 (仅宝箱，不存库)
            if (processedGroups.Add(monster.GroupId))
            {
                await HandleGroupPropUnlock(monster);
            }
        }

        // --- 分流结算 ---
        if (battle.MappingInfoId > 0) await HandleRaidSettlement(battle);

        if (Player.SceneInstance?.GameModeType is GameModeTypeEnum.RogueExplore or GameModeTypeEnum.ChessRogue or GameModeTypeEnum.TournRogue or GameModeTypeEnum.MagicRogue)
        {
            await HandleRogueSettlement(battle);
        }

        if (battle.CollegeConfigExcel != null) await HandleCollegeSettlement(battle);

        Console.WriteLine($"[Drop-Debug] <<<<<<<<<< 战斗结算链路结束 >>>>>>>>>>\n");
    }

    /// <summary>
    /// 【极简版】只解除同组宝箱的封印，不存库，不碰门
    /// </summary>
    private async ValueTask HandleGroupPropUnlock(EntityMonster monster)
    {
        var scene = Player.SceneInstance;
        if (scene == null) return;

        // 黑名单：模拟宇宙跳过
        if (scene.GameModeType == GameModeTypeEnum.RogueExplore ||
            scene.GameModeType == GameModeTypeEnum.RogueChallenge ||
            scene.GameModeType == GameModeTypeEnum.RogueAeonRoom ||
            scene.GameModeType == GameModeTypeEnum.ChessRogue ||
            scene.GameModeType == GameModeTypeEnum.TournRogue ||
            scene.GameModeType == GameModeTypeEnum.MagicRogue)
        {
            return;
        }

        var relatedProps = scene.Entities.Values
            .OfType<EntityProp>()
            .Where(p => p.GroupId == monster.GroupId)
            .ToList();

        if (relatedProps.Count == 0) return;

        foreach (var prop in relatedProps)
        {
            // 只处理：被封印的宝箱 (Locked -> Closed)
            if (prop.State == PropStateEnum.ChestLocked)
            {
                Console.WriteLine($"[DropManager] -> 解除宝箱封印(临时): 物件ID {prop.EntityId}");
                await prop.SetState(PropStateEnum.ChestClosed);
                // 不执行 Save
            }
        }
    }

    /// <summary>
    /// 处理大世界宝箱交互 (只改状态，不存库)
    /// </summary>
    public async ValueTask HandleChestInteractDrop(EntityProp prop)
    {
        if (prop.Excel.MappingInfoID > 0) return;
        if (prop.State == PropStateEnum.ChestUsed) return;

        // 给物品
        var items = DropService.CalculateDropsFromProp(prop.PropInfo.ChestID);
        await Player.InventoryManager!.AddItems(items);
        
        // 通知客户端播动画
        await Player.SendPacket(new PacketOpenChestScNotify(prop.PropInfo.ChestID));

        // 只改运行时状态：盖子打开
        await prop.SetState(PropStateEnum.ChestUsed);
        
        // 删除了所有 Player.SceneData.OpenedChestIdList.Add(...) 代码
    }

    // --- 以下保持原样 ---

    private async ValueTask HandleRaidSettlement(BattleInstance battle)
    {
        if (battle.MappingInfoId <= 0) return;
        var items = await Player.InventoryManager!.HandleMappingInfo(battle.MappingInfoId, battle.WorldLevel);
        battle.RaidRewardItems.AddRange(items);
    }

    private async ValueTask HandleRogueSettlement(BattleInstance battle)
    {
        var rogue = Player.RogueManager?.GetRogueInstance() as RogueInstance;
        if (rogue == null) return;
        await rogue.HandleBattleWinRewards(battle);
    }

    private async ValueTask HandleCollegeSettlement(BattleInstance battle)
    {
        var excel = battle.CollegeConfigExcel!;
        if (Player.BattleCollegeData?.FinishedCollegeIdList.Contains(excel.ID) == false)
        {
            Player.BattleCollegeData.FinishedCollegeIdList.Add(excel.ID);
            await Player.SendPacket(new PacketBattleCollegeDataChangeScNotify(Player));
            var items = await Player.InventoryManager!.HandleReward(excel.RewardID);
            battle.RaidRewardItems.AddRange(items);
        }
    }

    public async ValueTask GrantRogueImmersiveReward(RogueInstance rogue)
    {
        if (rogue == null || rogue.AreaExcel.ChestDisplayItemList == null)
        {
            await Player.InventoryManager!.AddItem(2, 2000);
            return;
        }

        int currentDifficulty = rogue.AreaExcel.Difficulty;
        int dropCount = 0;

        foreach (var item in rogue.AreaExcel.ChestDisplayItemList)
        {
            if (item.ItemID < 30000)
            {
                int count = item.ItemNum > 0 ? item.ItemNum : 1;
                await Player.InventoryManager!.AddItem(item.ItemID, count);
                continue;
            }

            if (item.ItemID % 10 == currentDifficulty)
            {
                int count = item.ItemNum > 0 ? item.ItemNum : 1;
                await Player.InventoryManager!.AddItem(item.ItemID, count);
                dropCount++;
            }
        }

        if (dropCount == 0)
        {
            var backupItem = rogue.AreaExcel.ChestDisplayItemList.FirstOrDefault(x => x.ItemID > 30000);
            if (backupItem != null) await Player.InventoryManager!.AddItem(backupItem.ItemID, 1);
        }
    }
}