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
    /// 战斗奖励统一结算入口（异步处理，彻底杜绝死锁）
    /// </summary>
public async ValueTask ProcessBattleRewards(BattleInstance battle, PVEBattleResultCsReq req)
{
    // --- 【全链路追踪日志：开始】 ---
    Console.WriteLine($"\n[Drop-Debug] >>>>>>>>>> 战斗结算链路启动 <<<<<<<<<<");
    Console.WriteLine($"[Drop-Debug] 战斗ID: {battle.BattleId} | 状态: {req.EndStatus}");
    Console.WriteLine($"[Drop-Debug] 副本关联ID (MappingInfoId): {battle.MappingInfoId}");
    Console.WriteLine($"[Drop-Debug] 场景模式: {Player.SceneInstance?.GameModeType} | 均衡等级: {Player.Data.WorldLevel}");
    
    if (req.EndStatus != BattleEndStatus.BattleEndWin) 
    {
        Console.WriteLine($"[Drop-Debug] 战斗未获胜，跳过奖励发放。");
        return;
    }

    // 1. 处理每一个怪物的死亡信号
    foreach (var monster in battle.EntityMonsters) 
    {
        await monster.Kill(false); 

        // 【核心修复】：如果是副本(MappingID > 0)，绝对禁止触发大世界怪物掉落
        // 从而彻底杜绝 MonsterDrop.json 里错误配置的“相位灵火”
        if (battle.MappingInfoId > 0) 
        {
            Console.WriteLine($"[Drop-Debug] 检测到副本模式: 拦截怪物 {monster.MonsterData.ID} 的大世界掉落，避免ID污染。");
            continue; 
        }

        // 仅在真正的“非副本”大世界模式下运行
        if (Player.SceneInstance?.GameModeType == GameModeTypeEnum.Unknown || Player.SceneInstance?.GameModeType == GameModeTypeEnum.Maze)
        {
            var dropId = monster.MonsterData.ID * 10 + Player.Data.WorldLevel;
            if (GameData.MonsterDropData.TryGetValue(dropId, out var dropData))
            {
                var items = dropData.CalculateDrop();
                await Player.InventoryManager!.AddItems(items, false);
                battle.MonsterDropItems.AddRange(items);
                Console.WriteLine($"[Drop-Debug] 触发大世界掉落: 怪物 {monster.MonsterData.ID} 产出 {items.Count} 堆物品。");
            }
        }
    }

    // --- 【分流结算逻辑】 ---

    // 2. 处理副本结算 (凝滞虚影、花藏等)
    if (battle.MappingInfoId > 0)
    {
        Console.WriteLine($"[Drop-Debug] >>> 执行副本结算路径: MappingID = {battle.MappingInfoId}");
        
        // 记录调用前的时间，排查异步阻塞
        await HandleRaidSettlement(battle);
        
        if (battle.RaidRewardItems.Count > 0)
        {
            foreach(var res in battle.RaidRewardItems)
                Console.WriteLine($"[Drop-Debug] 成功产出副本核心奖励: 物品ID {res.ItemId} 数量 {res.Count}");
        }
        else
        {
            Console.WriteLine($"[Drop-Debug] !!! 警告: 副本结算完成，但未产出任何物品。请检查 MappingInfoExcel.cs 里的 Loaded 逻辑。");
        }
    }

    // 3. 处理模拟宇宙结算
    if (Player.SceneInstance?.GameModeType is GameModeTypeEnum.RogueExplore or GameModeTypeEnum.ChessRogue or GameModeTypeEnum.TournRogue or GameModeTypeEnum.MagicRogue)
    {
        Console.WriteLine($"[Drop-Debug] >>> 执行模拟宇宙(Rogue)结算路径。");
        await HandleRogueSettlement(battle);
    }

    // 4. 处理战斗考核结算
    if (battle.CollegeConfigExcel != null)
    {
        Console.WriteLine($"[Drop-Debug] >>> 执行战斗考核结算路径。");
        await HandleCollegeSettlement(battle);
    }

    Console.WriteLine($"[Drop-Debug] <<<<<<<<<< 战斗结算链路结束 >>>>>>>>>>\n");
}
    /// <summary>
    /// 处理普通副本 (Raid) 掉落逻辑
    /// </summary>
    private async ValueTask HandleRaidSettlement(BattleInstance battle)
    {
        if (battle.MappingInfoId <= 0) return;

        // 调用原本 InventoryManager 里的副本掉落算法
        // 这里使用 await 确保数据库写入完成后再继续，且不会阻塞主线程
        var items = await Player.InventoryManager!.HandleMappingInfo(battle.MappingInfoId, battle.WorldLevel);
        
        // 将产出的物品存入战斗实例的结算清单
        battle.RaidRewardItems.AddRange(items);
    }

    /// <summary>
    /// 处理模拟宇宙 (Rogue) 结算逻辑（修复 1004 BOSS 卡死）
    /// </summary>
    private async ValueTask HandleRogueSettlement(BattleInstance battle)
    {
        var rogue = Player.RogueManager?.GetRogueInstance() as RogueInstance;
        if (rogue == null) return;

        // 1. 处理肉鸽专属奖励（碎片、积分、祝福抽选）
        await rogue.HandleBattleWinRewards(battle);

        // 2. 模拟宇宙 BOSS 沉浸奖励 (如果有关底 Mapping 奖励)
        if (battle.MappingInfoId > 0)
        {
            var items = await Player.InventoryManager!.HandleMappingInfo(battle.MappingInfoId, battle.WorldLevel);
            battle.RaidRewardItems.AddRange(items);
        }
    }

    /// <summary>
    /// 处理大世界宝箱交互掉落
    /// </summary>
    public async ValueTask HandleChestInteractDrop(EntityProp prop)
	{
		// 增加一层防护：如果是副本入口，不作为宝箱处理
		if (prop.Excel.MappingInfoID > 0) return;
        // 调用 DropService 算出宝箱内容
        var items = DropService.CalculateDropsFromProp(prop.PropInfo.ChestID);
        
        // 统一入库
        await Player.InventoryManager!.AddItems(items);
        
        // 发送开箱协议通知客户端
        await Player.SendPacket(new PacketOpenChestScNotify(prop.PropInfo.ChestID));
    }

    /// <summary>
    /// 处理战斗考核结算
    /// </summary>
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
}