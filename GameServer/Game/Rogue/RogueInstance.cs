using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Custom;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Enums.Rogue;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Rogue.Buff;
using EggLink.DanhengServer.GameServer.Game.Rogue.Event;
using EggLink.DanhengServer.GameServer.Game.Rogue.Scene;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Rogue;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;
using EggLink.DanhengServer.Enums.Scene;
using EggLink.DanhengServer.Database.Inventory;

namespace EggLink.DanhengServer.GameServer.Game.Rogue;

public class RogueInstance : BaseRogueInstance
{
    #region Initialization

  public RogueInstance(RogueAreaConfigExcel areaExcel, RogueAeonExcel aeonExcel, PlayerInstance player) : base(player,
        RogueSubModeEnum.CosmosRogue, aeonExcel.RogueBuffType)
    {
        AreaExcel = areaExcel;
        AeonExcel = aeonExcel;
        AeonId = aeonExcel.AeonID;
        Player = player;
        CurLineup = player.LineupManager!.GetCurLineup()!;
        EventManager = new RogueEventManager(player, this);

        int mapId = GetMapIdFromAreaId(areaExcel.RogueAreaID);
        
        if (!GameData.RogueMapData.ContainsKey(mapId))
        {
            Console.WriteLine($"[Rogue Error] 找不到 MapId: {mapId}，紧急回退到 1");
            mapId = 1; 
        }

        if (GameData.RogueMapData.TryGetValue(mapId, out var mapRooms))
        {
            // --- 1. 动态计算每个 SiteID 的逻辑深度 (Depth) ---
            var depthMap = new Dictionary<int, int>();
            var queue = new Queue<(int siteId, int d)>();

            // 寻找起始站点并初始化队列 
            foreach (var site in mapRooms.Values.Where(x => x.IsStart))
            {
                queue.Enqueue((site.SiteID, 1));
            }

            // 使用 BFS 遍历地图拓扑结构 
            while (queue.Count > 0)
            {
                var (sId, d) = queue.Dequeue();
                
                // 如果已记录深度且现有深度更短，则跳过
                if (depthMap.ContainsKey(sId) && depthMap[sId] <= d) continue;
                
                depthMap[sId] = d;

                if (mapRooms.TryGetValue(sId, out var siteData))
                {
                    // 沿着 NextSiteIDList 向下探索 
                    foreach (var nextId in siteData.NextSiteIDList)
                    {
                        queue.Enqueue((nextId, d + 1));
                    }
                }
            }

            // --- 2. 创建房间实例并传入深度与去重集合 ---

            // 【关键修复】：初始化去重集合，确保同一个关卡内精英房 ID 不重复
            HashSet<int> usedRoomIds = new();

            // 为了让去重更准确，建议按深度排序后再生成房间（可选，但推荐）
            var sortedSites = mapRooms.Values.OrderBy(x => depthMap.GetValueOrDefault(x.SiteID, 1)).ToList();

            foreach (var item in sortedSites)
            {
                // 获取逻辑深度，若在断开的路径上则默认为 1
                int siteDepth = depthMap.GetValueOrDefault(item.SiteID, 1);
                
                // 【修复点】：传入 siteDepth 和 usedRoomIds 两个参数
                var roomInstance = new RogueRoomInstance(item, areaExcel, siteDepth, usedRoomIds);

                RogueRooms.Add(item.SiteID, roomInstance);
                if (item.IsStart) StartSiteId = item.SiteID;
            }
        }
        else
        {
            Console.WriteLine($"[Rogue Critical] 无法加载房间！MapId: {mapId}");
        }

        var action = new RogueActionInstance { QueuePosition = CurActionQueuePosition };
        action.SetBonus();
        RogueActions.Add(CurActionQueuePosition, action);
    }
    #endregion

    #region Properties
    public new int CurImmersionToken => (int)Player.Data.ImmersiveArtifact;
    public RogueStatus Status { get; set; } = RogueStatus.Doing;
    public int CurReachedRoom { get; set; }
    public RogueAeonExcel AeonExcel { get; set; }
    public RogueAreaConfigExcel AreaExcel { get; set; }
    public Dictionary<int, RogueRoomInstance> RogueRooms { get; set; } = [];
    public RogueRoomInstance? CurRoom { get; set; }
    public int StartSiteId { get; set; }
    #endregion

    #region Buffs
 public override async ValueTask RollBuff(int amount)
{
    // 1. 修正属性名：使用 AeonExcel 而不是 Aeon
    if (AeonExcel == null)
    {
        // 兜底：如果命途配置为空，走父类的通用大池子 (100005)
        await base.RollBuff(amount); 
        return;
    }

    // 2. 获取该命途的前缀 (如存护 120, 巡猎 123)
    int pathPrefix = AeonExcel.RogueBuffType; 
    int roll = Random.Shared.Next(0, 100);
    int finalPoolId;

    // ==========================================
    // 情况 A：精英房 / BOSS 房 (RogueRoomType == 6)
    // ==========================================
    if (CurRoom != null && CurRoom.Excel.RogueRoomType == 6)
    {
        // 官服概率模拟：40% 概率出本命途 3 星金池，60% 概率出本命途 2 星蓝池
        if (roll < 40) 
        {
            finalPoolId = pathPrefix * 100 + 3; // 例如 12003
        }
        else 
        {
            finalPoolId = pathPrefix * 100 + 2; // 例如 12002
        }

        await RollBuff(amount, finalPoolId);

        // 精英房额外奇物掉落：25% 概率触发
        if (Random.Shared.Next(0, 100) < 25)
        {
            await RollMiracle(1);
            Console.WriteLine($"[Rogue] 玩家 {Player.Uid} 击败精英，额外掉落了奇物！");
        }
    }
    // ==========================================
    // 情况 B：普通战斗房 (或其他非精英房)
    // ==========================================
    else
    {
        // 官服概率模拟：15% 概率出现 2 星蓝池，85% 概率出现 1 星白池
        if (roll < 15)
        {
            finalPoolId = pathPrefix * 100 + 2; // 运气好给个蓝的
        }
        else
        {
            finalPoolId = pathPrefix * 100 + 1; // 常规给白的 (例如 12001)
        }

        await RollBuff(amount, finalPoolId);
    }
}

    public async ValueTask AddAeonBuff()
    {
        if (AeonBuffPending) return;
        if (CurAeonBuffCount + CurAeonEnhanceCount >= 4) return;

        var curAeonBuffCount = 0;
        var hintId = AeonId * 100 + 1;
        var enhanceData = GameData.RogueAeonEnhanceData[AeonId];
        var buffData = GameData.RogueAeonBuffData[AeonId];

        foreach (var buff in RogueBuffs)
        {
            if (buff.BuffExcel.RogueBuffType == AeonExcel.RogueBuffType)
            {
                if (!(buff.BuffExcel as RogueBuffExcel)!.IsAeonBuff)
                    curAeonBuffCount++;
                else
                    hintId++;
            }
        }

        var needAeonBuffCount = (CurAeonBuffCount + CurAeonEnhanceCount) switch
        {
            0 => 3, 1 => 6, 2 => 10, 3 => 14, _ => 100
        };

        if (curAeonBuffCount >= needAeonBuffCount)
        {
            RogueBuffSelectMenu menu = new(this)
            {
                QueueAppend = 2, HintId = hintId, RollMaxCount = 0, RollFreeCount = 0, IsAeonBuff = true
            };

            if (CurAeonBuffCount < 1)
            {
                CurAeonBuffCount++;
                menu.RollBuff([buffData], 1);
            }
            else
            {
                CurAeonEnhanceCount++;
                menu.RollBuff(enhanceData.Select(x => x as BaseRogueBuffExcel).ToList(), enhanceData.Count);
            }

            var action = menu.GetActionInstance();
            RogueActions.Add(action.QueuePosition, action);
            AeonBuffPending = true;
            await UpdateMenu();
        }
    }
    #endregion

    #region Methods
    
    // =========================================================================
    // 【唯一发奖入口】 HandleBattleWinRewards
    // 负责：BOSS战(发奖+弹窗) 和 普通战(发钱+RollBuff)
    // =========================================================================
    public async ValueTask HandleBattleWinRewards(BattleInstance battle)
    {
        // 关键判断：最后一关 (NextSiteIds为空) 或者 类型7/Site13
        bool isFinalBoss = CurRoom!.NextSiteIds.Count == 0 || CurRoom.Excel.RogueRoomType == 7 || CurRoom.SiteId == 13;
        
        if (isFinalBoss)
        {
            Console.WriteLine("[Rogue] 结算：BOSS 战胜利，调用 Manager.FinishRogue...");
            
            // 1. 调用 Manager 发奖 (notify=true 在 Manager 里设置了)
            var rewards = await Player.RogueManager!.FinishRogue(AreaExcel.RogueAreaID, true);
            
            // 2. 将获得的列表注入到战斗实例 (用于显示)
            if (rewards != null && rewards.Count > 0)
            {
                // Manager.FinishRogue 返回的是 List，所以这里用 AddRange 是对的！
                battle.RogueFirstRewardItems.AddRange(rewards);
            }
            
            // 3. 标记胜利
            IsWin = true;
            Status = RogueStatus.Finish;
            
            // 4. 发送大窗协议
            await Player.SendPacket(new PacketSyncRogueFinishScNotify(ToFinishInfo()));
            //await Player.SendPacket(new PacketSyncRogueStatusScNotify(Status));
            
            Console.WriteLine("[Rogue] 结算完成。");
        }
        else
        {
            // 普通怪逻辑
            int waveCount = Math.Max(1, battle.Stages.Count);
            var money = Random.Shared.Next(20, 60) * waveCount;
            await GainMoney(money);
            await RollBuff(1); 
        }
    }
    
  private int GetMapIdFromAreaId(int areaId)
{
    // 1. 特殊处理：新手教程或极短关卡
    if (areaId == 100) return 1; 
    
    // 2. 特殊处理：世界 1 和 世界 2 (使用 7 关长度的地图)
    if (areaId == 110) return 3; 
	if (areaId == 120) return 200; 

    // 3. 标准世界映射 (世界 3 - 世界 9)
    // 逻辑：(RogueAreaID 的十位) * 100 + (RogueAreaID 的个位)
    // 例如：131 (世界3难度2) -> 301
    // 例如：142 (世界4难度3) -> 402
    if (areaId >= 130 && areaId < 200)
    {
        int worldNum = (areaId / 10) % 10;
        int difficultyIndex = areaId % 10;
        return worldNum * 100 + difficultyIndex;
    }

    // 4. 活动/DLC 关卡映射 (10100 系列)
    // 例如：10100 -> 10001
    if (areaId >= 10100)
    {
        return 10000 + (areaId % 10) + 1;
    }

    // 兜底返回
    return 301; 
}

    public override async ValueTask UpdateMenu(int position = 0)
    {
        await base.UpdateMenu(position);
        await AddAeonBuff();
    }

    public async ValueTask<RogueRoomInstance?> EnterRoom(int siteId)
    {
        var prevRoom = CurRoom;
        if (prevRoom != null)
        {
            if (!prevRoom.NextSiteIds.Contains(siteId)) return null;
            prevRoom.Status = RogueRoomStatus.Finish;
            await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(prevRoom, prevRoom.MapId));
        }

        CurReachedRoom++;
        if (!RogueRooms.TryGetValue(siteId, out var nextRoom)) return null;

        CurRoom = nextRoom;
        CurRoom.Status = RogueRoomStatus.Play;
		System.Console.WriteLine($"[Rogue调试] 站点:{siteId} | 房间ID:{CurRoom.RoomId} | 类型:{CurRoom.Excel.RogueRoomType} | 物理Group:{CurRoom.Excel.GroupID}");
        await Player.EnterScene(CurRoom.Excel.MapEntrance, 0, false);

        var anchor = Player.SceneInstance!.FloorInfo?.GetAnchorInfo(CurRoom.Excel.GroupID, 1);
        if (anchor != null)
        {
            Player.Data.Pos = anchor.ToPositionProto();
            Player.Data.Rot = anchor.ToRotationProto();
        }

        await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(CurRoom, CurRoom.MapId));
        EventManager?.OnNextRoom();
        foreach (var miracle in RogueMiracles.Values) miracle.OnEnterNextRoom();
		
        return CurRoom;
    }

    public async ValueTask LeaveRogue()
    {
        Player.RogueManager!.RogueInstance = null;
        await Player.EnterScene(801120102, 0, false);
        Player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone, []);
    }

    public async ValueTask QuitRogue()
    {
        AreaExcel.ScoreMap.TryGetValue(CurReachedRoom, out var score);
        Player.RogueManager!.AddRogueScore(score);

        Status = RogueStatus.Finish;
        await Player.SendPacket(new PacketSyncRogueStatusScNotify(Status));
        await Player.SendPacket(new PacketSyncRogueFinishScNotify(ToFinishInfo()));
        
        //if (IsWin) 
        //{
        //    await Player.RogueManager!.UpdateRogueProgress(AreaExcel.RogueAreaID);
        //}

        await LeaveRogue();
    }
    #endregion

    #region Handlers

    

  public override void OnBattleStart(BattleInstance battle)
    {
        base.OnBattleStart(battle);
        if (CurRoom == null) return;

       
        battle.MappingInfoId = 0;
        battle.StaminaCost = 0;

        // =========================================================
        // 【等级逻辑】修复 85 级 BUG
        // =========================================================

        // 1. 获取基准等级
        int baseLevel = AreaExcel.RecommendLevel;

        // 2. 兜底
        if (baseLevel == 0) 
        {
            int difficulty = AreaExcel.Difficulty;
            if (difficulty == 0) difficulty = 1;
            baseLevel = difficulty * 10;
        }

        // 3. 【核心修复】层数成长
        // 错误写法: CurRoom.SiteId (BOSS房是 111，导致 +55级)
        // 正确写法: CurReachedRoom (记录实际走的步数，BOSS房是 13，+6级)
        int addLevel = CurReachedRoom > 1 ? (CurReachedRoom / 2) : 0;
        
        // 4. 强制赋值
        battle.CustomLevel = baseLevel + addLevel;

        Console.WriteLine($"[RogueLevel] 战斗等级修正 -> 推荐:{baseLevel} 层数:{CurReachedRoom}(原SiteId:{CurRoom.SiteId}) 加成:{addLevel} 最终:{battle.CustomLevel}");
		}
       

    // =========================================================================
    // 【修改后】OnBattleEnd 彻底“静音”
    // 删除了所有发奖逻辑，只处理失败退出和Miracle更新
    // 奖励逻辑全部由 DropManager -> HandleBattleWinRewards 触发，防止双倍
    // =========================================================================
    public override async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        foreach (var miracle in RogueMiracles.Values) miracle.OnEndBattle(battle);

        // 1. 输了 -> 退出
        if (req.EndStatus != BattleEndStatus.BattleEndWin)
        {
            await QuitRogue();
            return;
        }

        // 2. 赢了 -> 什么都不做！
        // 因为 DropManager 会自动调用 HandleBattleWinRewards 来发奖、发Buff、弹窗。
        // 如果这里再写代码，就会导致执行两次，玩家获得双倍奖励。
        
        // 仅留日志用于调试
        Console.WriteLine("[Rogue] OnBattleEnd: 战斗结束流程完成 (奖励已委托 DropManager 处理)");
    }
    #endregion

    #region Serialization
    public RogueCurrentInfo ToProto()
    {
        var proto = new RogueCurrentInfo
        {
            Status = Status,
            GameMiracleInfo = ToMiracleInfo(),
            RogueAeonInfo = ToAeonInfo(),
            RogueLineupInfo = ToLineupInfo(),
            RogueBuffInfo = ToBuffInfo(),
            VirtualItemInfo = ToVirtualItemInfo(),
            RogueMap = ToMapInfo(),
            ModuleInfo = new RogueModuleInfo { ModuleIdList = { 1, 2, 3, 4, 5 } },
            IsExploreWin = IsWin
        };
        if (RogueActions.Count > 0) proto.PendingAction = RogueActions.First().Value.ToProto();
        else proto.PendingAction = new RogueCommonPendingAction();
        return proto;
    }

	RogueMapInfo ToMapInfo()
	{
    // 逻辑：
    // 1. 如果有当前房间，直接拿房间里配置好的物理场景 ID (MapId)
    // 2. 如果没进房，拿第一个房源（起点房）里配好的物理场景 ID
    // 3. 实在没有，才用 GetMapIdFromAreaId 那个骨架 ID 兜底（但这基本不会发生）
    

    var proto = new RogueMapInfo
    {
        CurSiteId = (uint)(CurRoom?.SiteId ?? StartSiteId),
        CurRoomId = (uint)(CurRoom?.RoomId ?? 0),
        AreaId = (uint)AreaExcel.RogueAreaID,
        
        // 发给客户端这个 realMapId，它才会去加载杰帕德的场景
        MapId = (uint)GetMapIdFromAreaId(AreaExcel.RogueAreaID) 
    };
    
    foreach (var room in RogueRooms) proto.RoomList.Add(room.Value.ToProto());
    return proto;
}
    
    public GameAeonInfo ToAeonInfo()
    {
        return new GameAeonInfo
        {
            GameAeonId = (uint)AeonId,
            IsUnlocked = AeonId != 0,
            UnlockedAeonEnhanceNum = (uint)(AeonId != 0 ? 3 : 0)
        };
    }

    public RogueLineupInfo ToLineupInfo()
    {
        var proto = new RogueLineupInfo();
        foreach (var avatar in CurLineup!.BaseAvatars!) proto.BaseAvatarIdList.Add((uint)avatar.BaseAvatarId);
        proto.ReviveInfo = new RogueReviveInfo { RogueReviveCost = new ItemCostData { ItemList = { new ItemCost { PileItem = new PileItem { ItemId = 31, ItemNum = (uint)CurReviveCost } } } } };
        return proto;
    }

    public RogueVirtualItem ToVirtualItemInfo()
    {
        return new RogueVirtualItem { RogueMoney = (uint)CurMoney, DAFALAOAOOI = (uint)CurImmersionToken, AMNKMBMHKDF = (uint)CurImmersionToken, BPJOAPFAFKK = (uint)CurImmersionToken };
    }

    public GameMiracleInfo ToMiracleInfo()
    {
        var proto = new GameMiracleInfo { GameMiracleInfo_ = new RogueMiracleInfo() };
        foreach (var miracle in RogueMiracles.Values) proto.GameMiracleInfo_.MiracleList.Add(miracle.ToProto());
        return proto;
    }

    public RogueBuffInfo ToBuffInfo()
    {
        var proto = new RogueBuffInfo();
        foreach (var buff in RogueBuffs) proto.MazeBuffList.Add(buff.ToProto());
        return proto;
    }

    public RogueFinishInfo ToFinishInfo()
    {
        AreaExcel.ScoreMap.TryGetValue(CurReachedRoom, out var score);
        
        return new RogueFinishInfo
        {
            ScoreId = (uint)score,
            AreaId = (uint)AreaExcel.RogueAreaID,
            IsWin = IsWin,
            RecordInfo = new RogueRecordInfo
            {
                AvatarList = { CurLineup!.BaseAvatars!.Select(avatar => new RogueRecordAvatar { 
                    Id = (uint)avatar.BaseAvatarId, 
                    AvatarType = AvatarType.AvatarFormalType, 
                    Level = (uint)(Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId)?.Level ?? 0), 
                    Slot = (uint)CurLineup!.BaseAvatars!.IndexOf(avatar) 
                }) },
                BuffList = { RogueBuffs.Select(buff => buff.ToProto()) },
                MiracleList = { RogueMiracles.Values.Select(miracle => (uint)miracle.MiracleId) }
            }
        };
    }
    #endregion
}
