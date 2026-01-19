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
            foreach (var item in mapRooms.Values)
            {
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
        if (areaId == 100) return 1;
        if (areaId == 110) return 3;
        if (areaId == 120) return 3;
        if (areaId >= 130 && areaId < 200) return 200; 
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
        AreaExcel.ScoreMap.TryGetValue(CurReachedRoom, out var score);
        Player.RogueManager!.AddRogueScore(score);

        Status = RogueStatus.Finish;
        await Player.SendPacket(new PacketSyncRogueStatusScNotify(Status));
        await Player.SendPacket(new PacketSyncRogueFinishScNotify(ToFinishInfo()));
        
        if (IsWin) 
        {
            await Player.RogueManager!.UpdateRogueProgress(AreaExcel.RogueAreaID);
        }

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
        if (CurRoom == null) return;

        battle.OnMonsterKill += OnRogueMonsterKill;
        battle.MappingInfoId = 0;
        battle.StaminaCost = 0;

        int worldIndex = (AreaExcel.RogueAreaID / 10) % 10;
        int difficulty = AreaExcel.Difficulty; 
        if (difficulty == 0) difficulty = 1;

        int baseLevel = AreaExcel.RecommendLevel;
        if (baseLevel == 0) 
        {
             baseLevel = 35 + ((worldIndex > 3 ? worldIndex - 3 : 0) * 5) + ((difficulty - 1) * 10);
        }
        int targetLevel = baseLevel + (CurReachedRoom / 2);
        battle.CustomLevel = CurRoom.MonsterLevel > 0 ? CurRoom.MonsterLevel : targetLevel;
        
        bool isBossStage = CurRoom.Excel.RogueRoomType == 7 || CurRoom.SiteId == 13;

        if (isBossStage) 
        {
            int targetStageId = 80300000 + (worldIndex * 10) + difficulty;
            if (worldIndex == 3) targetStageId = 80300031; 
            if (worldIndex == 4) targetStageId = 80300041; 

            if (targetStageId > 0 && GameData.StageConfigData.TryGetValue(targetStageId, out var stageConfig))
            {
                battle.StageId = targetStageId;
                if (battle.Stages != null)
                {
                    battle.Stages.Clear(); 
                    battle.Stages.Add(stageConfig); 
                }
            }
        }
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