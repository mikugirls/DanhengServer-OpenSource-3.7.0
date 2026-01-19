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

        // 1. 获取 MapId (统一长图骨架)
        int mapId = GetMapIdFromAreaId(areaExcel.RogueAreaID);
        
        // 2. 检查 MapId
        if (!GameData.RogueMapData.ContainsKey(mapId))
        {
            Console.WriteLine($"[Rogue Error] 找不到 MapId: {mapId}，紧急回退到 1");
            mapId = 1; 
        }

        // 3. 加载房间
        if (GameData.RogueMapData.TryGetValue(mapId, out var mapRooms))
        {
            foreach (var item in mapRooms.Values)
            {
                // 创建房间实例 (所有逻辑都在 RogueRoomInstance 内部处理了)
                var roomInstance = new RogueRoomInstance(item, areaExcel);
                roomInstance.MapId = mapId; 

                RogueRooms.Add(item.SiteID, roomInstance);
                if (item.IsStart) StartSiteId = item.SiteID;
            }
        }
        else
        {
            Console.WriteLine($"[Rogue Critical] 无法加载房间！MapId: {mapId}");
        }

        // 初始化 Bonus
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
 	public async ValueTask HandleBattleWinRewards(BattleInstance battle)
{
    // 1. 判定是否为最终 BOSS (Site 13 或 房间类型 7)
    bool isFinalBoss = CurRoom?.Excel.RogueRoomType == 7 || CurRoom?.SiteId == 13;
    
    if (isFinalBoss)
    {
        // --- 核心修改：战斗大屏领奖逻辑 ---
        Console.WriteLine($"[Rogue-Drop] 检测到最终BOSS胜利，正在将首通奖励注入结算大屏...");

        // A. 调用 RogueManager 发放首通奖励
        // 这一步会通过 InventoryManager.HandleReward 将物品存入背包
        // 并返回一个“增量物品列表”
        var rewards = await Player.RogueManager!.FinishRogue(AreaExcel.RogueAreaID, true);
        
        // B. 【关键】将奖励列表注入到当前战斗实例的 RaidRewardItems 中
        // PVEBattleResultScRsp 协议包会自动读取这个列表并展示在胜利大屏正中央
        if (rewards != null && rewards.Count > 0)
        {
            battle.RaidRewardItems.AddRange(rewards);
        }

        // C. 标记通关状态，同步给客户端
        IsWin = true;
        Status = RogueStatus.Finish;
        await Player.SendPacket(new PacketSyncRogueStatusScNotify(Status));

        // D. 拦截逻辑：不执行 GainMoney 和 RollBuff，防止弹出 Buff 选择框
        Console.WriteLine("[Rogue-Drop] 最终关卡：奖励已注入，跳过Buff选择。");
    }
    else
    {
        // 2. 普通房间逻辑 (保持不变)
        int waveCount = Math.Max(1, battle.Stages.Count);
        var money = Random.Shared.Next(20, 60) * waveCount;
        await GainMoney(money);
        await RollBuff(1); 
    }
}
   

    // --- 核心修改：统一使用长图 Map ID (200) ---
    private int GetMapIdFromAreaId(int areaId)
    {
        // 教学关 (7关)
        if (areaId == 100) return 1;
        if (areaId == 110) return 3;
        if (areaId == 120) return 3;

        // 正式世界 (World 3 - 9)
        // 全部统一返回 200 (因为 Map 200 确定是 13 关的长图)
        // 具体的怪物和场景风格，由 RogueRoomInstance 内部的 worldIndex 决定
        if (areaId >= 130 && areaId < 200) return 200; 

        // DLC
        if (areaId >= 10100) return 10001; 

        return 1;
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
    // 1. 获取本次得分
    AreaExcel.ScoreMap.TryGetValue(CurReachedRoom, out var score);

    // 2. 正式结算周积分 (调用 Manager 的方法)
    Player.RogueManager!.AddRogueScore(score);

    // 3. 设置状态并同步
    Status = RogueStatus.Finish;
    await Player.SendPacket(new PacketSyncRogueStatusScNotify(Status));
    
    // 4. 发送结算大屏协议
    await Player.SendPacket(new PacketSyncRogueFinishScNotify(ToFinishInfo()));

    // 5. 更新解锁进度并保存数据库 (这是你刚才提到的 Manager 里的新方法)
    if (IsWin)
    {
        await Player.RogueManager!.UpdateRogueProgress(AreaExcel.RogueAreaID);
    }

    // 6. 离开场景
    await LeaveRogue();
}
    #endregion

    #region Handlers

    private async ValueTask OnRogueMonsterKill(EntityMonster monster) 
    {
        var relatedProps = monster.Scene.Entities.Values.OfType<EntityProp>().Where(p => p.GroupId == monster.GroupId);
        foreach (var prop in relatedProps) 
        {
            if (prop.Excel.PropType == PropTypeEnum.PROP_ROGUE_CHEST || 
                prop.Excel.PropType == PropTypeEnum.PROP_ROGUE_REWARD_OBJECT ||
                prop.Excel.ID >= 60000) 
            {
                await prop.SetState(PropStateEnum.ChestClosed);
            }
        }
    }

 public override void OnBattleStart(BattleInstance battle)
    {
        base.OnBattleStart(battle);
        
        Console.WriteLine($"[RogueDebug] OnBattleStart 被调用! CurRoom 是否为空: {(CurRoom == null ? "是" : "否")}");
        if (CurRoom == null) return;

        // 1. 基础设置
        battle.OnMonsterKill += OnRogueMonsterKill;
        battle.MappingInfoId = 0;
        battle.StaminaCost = 0;

        // 2. 准备基础数据 (只在这里定义一次！)
        int worldIndex = (AreaExcel.RogueAreaID / 10) % 10;
        int difficulty = AreaExcel.Difficulty; 
        if (difficulty == 0) difficulty = 1;

        // 3. 【等级核心修复】
        // 直接读取 Excel 的 RecommendLevel
        int baseLevel = AreaExcel.RecommendLevel;
        
        // 兜底：如果表里是 0，就用备用公式
        if (baseLevel == 0) 
        {
             // 备用公式：世界3=35, 往后每世界+5; 难度每级+10
             baseLevel = 35 + ((worldIndex > 3 ? worldIndex - 3 : 0) * 5) + ((difficulty - 1) * 10);
        }

        // 加上层数微量成长 (每2层+1级)
        int targetLevel = baseLevel + (CurReachedRoom / 2);
        
        // 赋值
        battle.CustomLevel = CurRoom.MonsterLevel > 0 ? CurRoom.MonsterLevel : targetLevel;
        
        Console.WriteLine($"[RogueLevel] 战斗等级修正 -> 推荐:{baseLevel} 最终:{battle.CustomLevel} (世界:{worldIndex})");

        // 4. 【BOSS 注入逻辑】
        bool isBossStage = CurRoom.Excel.RogueRoomType == 7 || CurRoom.SiteId == 13;

        if (isBossStage) 
        {
            Console.WriteLine($"[RogueDebug] 判定为BOSS战 (SiteId: {CurRoom.SiteId}, Type: {CurRoom.Excel.RogueRoomType})");
            
            // 默认公式
            int targetStageId = 80300000 + (worldIndex * 10) + difficulty;

            // [双重保险] 明确指定 ID，防止算错
            if (worldIndex == 3) targetStageId = 80300031; // 杰帕德
            if (worldIndex == 4) targetStageId = 80300041; // 史瓦罗

            Console.WriteLine($"[RogueDebug] BOSS战注入 -> World: {worldIndex}, TargetStage: {targetStageId}");

            if (targetStageId > 0 && GameData.StageConfigData.TryGetValue(targetStageId, out var stageConfig))
            {
                battle.StageId = targetStageId;
                if (battle.Stages != null)
                {
                    battle.Stages.Clear(); 
                    battle.Stages.Add(stageConfig); 
                    Console.WriteLine($"[RogueDebug] 注入成功!");
                }
            }
            else
            {
                Console.WriteLine($"[RogueDebug] 错误: 数据库里没有 {targetStageId} 这个 Stage!");
            }
        }
        else
        {
            Console.WriteLine($"[RogueDebug] 普通战斗: SiteId={CurRoom.SiteId}, Type={CurRoom.Excel.RogueRoomType}");
        }
        
        Console.WriteLine($"[Rogue] 战斗开始. Level: {battle.CustomLevel}, Final StageID: {battle.StageId}");
    }

    public override async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
      foreach (var miracle in RogueMiracles.Values) miracle.OnEndBattle(battle);

    // 战斗失败处理
    if (req.EndStatus != BattleEndStatus.BattleEndWin)
    {
        await QuitRogue();
        return;
    }

    // 检查是否是 BOSS 房
    bool isFinalBoss = CurRoom!.NextSiteIds.Count == 0 || CurRoom.Excel.RogueRoomType == 7;

    if (isFinalBoss)
    {
        // 注意：这里【不要】调用 QuitRogue()，也不要在这里发奖（交给上面的 HandleBattleWinRewards）
        // 只需要确保玩家不被踢出场景即可。
        Console.WriteLine("[Rogue] 战斗结束，等待结算包下发，玩家留守场景。");
    }
    // 普通怪物的逻辑会自动在 DropManager 里触发 RollBuff
    }
    #endregion

    #region Serialization
    // (序列化代码保持不变，为节省篇幅省略，请保留你原文件中的 Serialization 区域)
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

    public RogueMapInfo ToMapInfo()
    {
        var proto = new RogueMapInfo
        {
            CurSiteId = (uint)(CurRoom?.SiteId ?? StartSiteId),
            CurRoomId = (uint)(CurRoom?.RoomId ?? 0),
            AreaId = (uint)AreaExcel.RogueAreaID,
            MapId = (uint)(CurRoom?.MapId ?? GetMapIdFromAreaId(AreaExcel.RogueAreaID))
        };
        foreach (var room in RogueRooms) proto.RoomList.Add(room.Value.ToProto());
        return proto;
    }
    // ... (保留你原文件后面的 ToAeonInfo 等方法)
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
    // 只获取分值，不在这里 AddRogueScore
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
