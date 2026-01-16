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
namespace EggLink.DanhengServer.GameServer.Game.Rogue;
using EggLink.DanhengServer.GameServer.Game.Scene;

// --- 修复点 1: 补齐缺失的类定义声明 ---
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

       // --- 【核心修改开始】 ---
        
        // 1. 获取正确的 MapId (这是之前一直缺失的关键环节)
        int mapId = GetMapIdFromAreaId(areaExcel.RogueAreaID);
        
        // 2. 容错逻辑：如果找不到特定 MapId (比如 201)，回退到基础版 (比如 200)
        // 防止新出的难度还没有配地图数据导致崩服
        if (!GameData.RogueMapData.ContainsKey(mapId))
        {
            // 尝试回退到整百 ID (e.g., 131 -> 101, 141 -> 200 ??? 这里的逻辑视具体数据而定)
            // 根据你的数据，13x 都是 101，14x 是 20x。
            // 简单处理：如果找不到，打个日志，不用硬回退，方便发现问题。
            Console.WriteLine($"[Rogue Error] 找不到 MapId: {mapId} 的配置数据！");
        }

        // 3. 加载房间
        if (GameData.RogueMapData.TryGetValue(mapId, out var mapRooms))
        {
            foreach (var item in mapRooms.Values)
            {
                // 这里调用你刚刚修改好的 RogueRoomInstance 新构造函数
                // 它会自动计算出正确的 Boss ID 和 MonsterLevel
                var roomInstance = new RogueRoomInstance(item, areaExcel);
                
                RogueRooms.Add(item.SiteID, roomInstance);
                
                if (item.IsStart) StartSiteId = item.SiteID;
            }
        }
        // --- 【核心修改结束】 ---

        // 初始化 Bonus 动作
        var action = new RogueActionInstance
        {
            QueuePosition = CurActionQueuePosition
        };
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
        if (CurRoom!.Excel.RogueRoomType == 6)
        {
            await RollBuff(amount, 100003, 2); 
            await RollMiracle(1);
        }
        else
        {
            await RollBuff(amount, 100005); 
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
            0 => 3,
            1 => 6,
            2 => 10,
            3 => 14,
            _ => 100
        };

        if (curAeonBuffCount >= needAeonBuffCount)
        {
            RogueBuffSelectMenu menu = new(this)
            {
                QueueAppend = 2,
                HintId = hintId,
                RollMaxCount = 0,
                RollFreeCount = 0,
                IsAeonBuff = true
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
	// --- 把这个方法加到 RogueInstance 类里 ---
    private int GetMapIdFromAreaId(int areaId)
    {
        // 世界 1 & 2
        if (areaId == 100) return 1;
        if (areaId == 110) return 2;
        if (areaId == 120) return 3;

        // 世界 3 (雅利洛) - Area 13x -> Map 101
        if (areaId >= 130 && areaId < 140) return 101; 

        // 世界 4 (史瓦罗) - Area 14x -> Map 200 + Difficulty
        if (areaId >= 140 && areaId < 150)
        {
            int difficulty = areaId % 10; 
            return 200 + difficulty; 
        }

        // 世界 5 (卡芙卡) - Area 15x -> Map 300 + Difficulty
        if (areaId >= 150 && areaId < 160)
        {
            int difficulty = areaId % 10;
            return 300 + difficulty;
        }

        // 世界 6 (可可利亚) - Area 16x -> Map 401 + Difficulty
        if (areaId >= 160 && areaId < 170)
        {
            // 注意：Map 401 起步，不是 400
            int difficulty = areaId % 10; 
            return 401 + difficulty; // 如果难度1是160，那就是 401
        }

        // 世界 7 (玄鹿) - Area 17x -> Map 501 + Difficulty
        if (areaId >= 170 && areaId < 180)
        {
            int difficulty = areaId % 10;
            return 501 + difficulty;
        }

        // 世界 8 - Area 18x -> Map 601 + Difficulty
        if (areaId >= 180 && areaId < 190)
        {
            int difficulty = areaId % 10;
            return 601 + difficulty;
        }

        // 世界 9 - Area 19x -> Map 701 + Difficulty
        if (areaId >= 190 && areaId < 200)
        {
            int difficulty = areaId % 10;
            return 701 + difficulty;
        }
        
        // 无尽模式等其他 ID
        if (areaId >= 10100) return 10001; 

        return 1; // 默认
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
        // 检查连通性 (防止飞得太离谱)
        if (!prevRoom.NextSiteIds.Contains(siteId)) return null;

        // 【保留原逻辑】：强制把上一关设为完成
        // 这样你就可以“穿墙”，不打怪直接进下一关（方便测试）
        prevRoom.Status = RogueRoomStatus.Finish;

        // 【只改这里】：AreaExcel.MapId 是 0，必须改！
        // 如果你改了 RogueRoomInstance，这里直接用 prevRoom.MapId
        // 如果没改，就用 GetMapIdFromAreaId(AreaExcel.RogueAreaID)
        // 为了稳妥，这里假设你已经在 RogueRoomInstance 里加了 MapId 字段
        await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(prevRoom, prevRoom.MapId));
    }

    CurReachedRoom++;
    CurRoom = RogueRooms[siteId];
    CurRoom.Status = RogueRoomStatus.Play;

    await Player.EnterScene(CurRoom.Excel.MapEntrance, 0, false);

    var anchor = Player.SceneInstance!.FloorInfo?.GetAnchorInfo(CurRoom.Excel.GroupID, 1);
    if (anchor != null)
    {
        Player.Data.Pos = anchor.ToPositionProto();
        Player.Data.Rot = anchor.ToRotationProto();
    }

    // 【只改这里】：同上，修复 MapId 为 0 的问题
    await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(CurRoom, CurRoom.MapId));

    // 下面保持不变
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
        Status = RogueStatus.Finish;
        await Player.SendPacket(new PacketSyncRogueStatusScNotify(Status));
        await Player.SendPacket(new PacketSyncRogueFinishScNotify(ToFinishInfo()));
    }

    #endregion

    #region Handlers
 // --- 新增：肉鸽专属的杀怪回调逻辑 ---
private async ValueTask OnRogueMonsterKill(EntityMonster monster) 
{
    // 获取同组的物件（如沉浸器、肉鸽宝箱）
    var relatedProps = monster.Scene.Entities.Values
        .OfType<EntityProp>()
        .Where(p => p.GroupId == monster.GroupId);

    foreach (var prop in relatedProps) 
    {
        // 逻辑：激活肉鸽奖励台或宝箱
        if (prop.Excel.PropType == PropTypeEnum.PROP_ROGUE_CHEST || 
            prop.Excel.PropType == PropTypeEnum.PROP_ROGUE_REWARD_OBJECT ||
            prop.Excel.ID >= 60000) 
        {
            await prop.SetState(PropStateEnum.ChestClosed);
            Console.WriteLine($"[Rogue Logic] 激活肉鸽奖励: {prop.Excel.ID}");
        }
    }
}
/// <summary>
/// 【重构新增】处理肉鸽战斗胜利后的掉落奖励与祝福触发
/// </summary>
public async ValueTask HandleBattleWinRewards(BattleInstance battle)
{
    Console.WriteLine($"[Rogue] >>> 开始处理战斗奖励分发...");

    bool isFinalBoss = CurRoom?.Excel.RogueRoomType == 7;

    if (!isFinalBoss)
    {
        // 【修复点】直接乘以 Stages.Count，不要在那调 Add()
        int waveCount = Math.Max(1, battle.Stages.Count); 
        var money = Random.Shared.Next(20, 60) * waveCount;
        
        await GainMoney(money);
        await RollBuff(1); 
        Console.WriteLine($"[Rogue] 普通怪结算完成。获得碎片: {money}");
    }

    // 处理沉浸奖励 (Artifacts)
    if (battle.MappingInfoId > 0)
    {
        Console.WriteLine($"[Rogue] 检测到 Mapping奖励 ID: {battle.MappingInfoId}，开始异步入库...");
        var items = await Player.InventoryManager!.HandleMappingInfo(battle.MappingInfoId, Player.Data.WorldLevel);
        battle.RaidRewardItems.AddRange(items); 
        Console.WriteLine($"[Rogue] 沉浸奖励发放成功。");
    }
}
public override void OnBattleStart(BattleInstance battle)
{
    base.OnBattleStart(battle);
    if (CurRoom == null) return;

    // 1. 挂载击杀回调
    battle.OnMonsterKill += OnRogueMonsterKill;

    // 2. 清除干扰项 (防死锁、防扣体力)
    battle.MappingInfoId = 0;
    battle.StaminaCost = 0;

    // 3. 准备查找 StageId (怪物配置)
    int targetStageId = 0;

    // --- 【核心修复 1】使用正确的 MapId ---
    // 不要重新计算 MapId，直接用房间实例里存的！
    // (前提是你已经按上一条回答更新了 RogueRoomInstance，加了 MapId 属性)
    int currentMapId = CurRoom.MapId; 

    // 4. 优先尝试从 RogueMapData 获取怪物配置 (这是最正规的数据驱动方式)
    if (GameData.RogueMapData.TryGetValue(currentMapId, out var mapRooms))
    {
        if (mapRooms.TryGetValue(CurRoom.SiteId, out var mapRoomConfig))
        {
            // 如果配置表里配置了 LevelList (怪物组)，从中随机一个
            if (mapRoomConfig.LevelList != null && mapRoomConfig.LevelList.Count > 0)
            {
                targetStageId = mapRoomConfig.LevelList.RandomElement();
            }
        }
    }

    // 5. 【兜底逻辑】如果是 BOSS 房且没查到 Stage，再尝试硬编码公式
    // 注意：Boss 房类型通常是 5 (根据你的 RogueRoom.json)，不是 7
    // 或者判断没有下一关 (NextSiteIds.Count == 0)
    if (targetStageId == 0 && (CurRoom.Excel.RogueRoomType == 7 || CurRoom.NextSiteIds.Count == 0))
    {
        int progress = AreaExcel.AreaProgress;
        int difficulty = AreaExcel.Difficulty;
        
        // 这个公式主要针对 模拟宇宙·扩展 (DLC) 或特定活动
        // 标准模式下，上面的第4步通常能拿到 ID
        targetStageId = 80300000 + (progress * 10) + difficulty;
        Console.WriteLine($"[Rogue] 使用硬编码公式计算 BOSS StageId: {targetStageId}");
    }

    // 6. 应用 Stage 数据
    if (targetStageId > 0)
    {
        battle.StageId = targetStageId;
        if (GameData.StageConfigData.TryGetValue(targetStageId, out var stageConfig))
        {
            battle.Stages.Clear();
            battle.Stages.Add(stageConfig);
        }
        else
        {
            Console.WriteLine($"[Rogue Error] 找不到 StageID: {targetStageId} 的配置！");
            // 极度兜底：给个史莱姆防止卡死
            // battle.StageId = 201012311; 
        }
    }

    // --- 【核心修复 2】使用房间预设的等级 ---
    // 这样能保证战斗内等级和大地图显示的推荐等级一致
    battle.CustomLevel = CurRoom.MonsterLevel;
    
    // 如果想要动态一点，可以加个浮动
    // battle.CustomLevel = CurRoom.MonsterLevel + (CurReachedRoom / 2);
}

public override async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
{
    foreach (var miracle in RogueMiracles.Values) miracle.OnEndBattle(battle);

    // 失败处理
    if (req.EndStatus != BattleEndStatus.BattleEndWin)
    {
        await QuitRogue();
        return;
    }

    // --- 【关键点】这里只处理“赢了之后该去哪”，不再调用 RollBuff ---
    
    // 如果没有下一关了，说明是最终 BOSS 赢了
    if (CurRoom!.NextSiteIds.Count == 0)
    {
        IsWin = true;
        Console.WriteLine($"[Rogue] 检测到最终关卡胜利，准备发送通关通知...");
        await Player.SendPacket(new PacketSyncRogueExploreWinScNotify());
        
        // 标记状态为完成，防止流程挂起
        Status = RogueStatus.Finish;
    }
    // 普通关卡的奖励已经由 DropManager 提前发过了，这里什么都不用写
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
            ModuleInfo = new RogueModuleInfo
            {
                ModuleIdList = { 1, 2, 3, 4, 5 }
            },
            IsExploreWin = IsWin
        };

        if (RogueActions.Count > 0)
            proto.PendingAction = RogueActions.First().Value.ToProto();
        else
            proto.PendingAction = new RogueCommonPendingAction();

        return proto;
    }

    public RogueMapInfo ToMapInfo()
    {
        var proto = new RogueMapInfo
        {
            CurSiteId = (uint)(CurRoom?.SiteId ?? StartSiteId),
            CurRoomId = (uint)(CurRoom?.RoomId ?? 0),
            AreaId = (uint)AreaExcel.RogueAreaID,
            MapId = (uint)(CurRoom?.MapId ?? GetMapIdFromAreaId(AreaExcel.RogueAreaID))
        };

        foreach (var room in RogueRooms) 
            proto.RoomList.Add(room.Value.ToProto());

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

        proto.ReviveInfo = new RogueReviveInfo
        {
            RogueReviveCost = new ItemCostData
            {
                ItemList = { new ItemCost { PileItem = new PileItem { ItemId = 31, ItemNum = (uint)CurReviveCost } } }
            }
        };
        return proto;
    }

    public RogueVirtualItem ToVirtualItemInfo()
    {
        return new RogueVirtualItem { RogueMoney = (uint)CurMoney,
									 // 核心修正：对应 3.7.0 协议中的沉浸器总量字段
        DAFALAOAOOI = (uint)CurImmersionToken, 
        AMNKMBMHKDF = (uint)CurImmersionToken, // 兼容性冗余字段
        BPJOAPFAFKK = (uint)CurImmersionToken  // 兼容性冗余字段
									  };
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
        Player.RogueManager!.AddRogueScore(score);

        return new RogueFinishInfo
        {
            ScoreId = (uint)score,
            AreaId = (uint)AreaExcel.RogueAreaID,
            IsWin = IsWin,
            RecordInfo = new RogueRecordInfo
            {
                AvatarList =
                {
                    CurLineup!.BaseAvatars!.Select(avatar => new RogueRecordAvatar
                    {
                        Id = (uint)avatar.BaseAvatarId,
                        AvatarType = AvatarType.AvatarFormalType,
                        Level = (uint)(Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId)?.Level ?? 0),
                        Slot = (uint)CurLineup!.BaseAvatars!.IndexOf(avatar)
                    })
                },
                BuffList = { RogueBuffs.Select(buff => buff.ToProto()) },
                MiracleList = { RogueMiracles.Values.Select(miracle => (uint)miracle.MiracleId) }
            }
        };
    }

    #endregion
} // --- 修复点 2: 确保类定义闭合 ---
