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

namespace EggLink.DanhengServer.GameServer.Game.Rogue;

public RogueInstance(RogueAreaConfigExcel areaExcel, RogueAeonExcel aeonExcel, PlayerInstance player) : base(player,
        RogueSubModeEnum.CosmosRogue, aeonExcel.RogueBuffType)
    {
        AreaExcel = areaExcel;
        AeonExcel = aeonExcel;
        AeonId = aeonExcel.AeonID;
        Player = player;
        CurLineup = player.LineupManager!.GetCurLineup()!;
        EventManager = new RogueEventManager(player, this);

        // --- 核心修复：将 areaId 传入每个房间实例以区分不同世界的 BOSS ---
        foreach (var item in areaExcel.RogueMaps.Values)
        {
            // 初始化房间，传入 Site 配置和当前世界 ID
            RogueRooms.Add(item.SiteID, new RogueRoomInstance(item, areaExcel.RogueAreaID));
            if (item.IsStart) StartSiteId = item.SiteID;
        }

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
        // 区分精英/BOSS房(6)与其他房间的 Buff 抽取概率
        if (CurRoom!.Excel.RogueRoomType == 6)
        {
            await RollBuff(amount, 100003, 2); // 奖励池
            await RollMiracle(1);
        }
        else
        {
            await RollBuff(amount, 100005); // 普通池
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
            // 校验路径合法性
            if (!prevRoom.NextSiteIds.Contains(siteId)) return null;
            
            // 标记上一个房间为完成状态
            prevRoom.Status = RogueRoomStatus.Finish;
            await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(prevRoom, AreaExcel.MapId));
        }

        // 进入新房间并标记为进行中
        CurReachedRoom++;
        CurRoom = RogueRooms[siteId];
        CurRoom.Status = RogueRoomStatus.Play;

        // 加载场景配置
        await Player.EnterScene(CurRoom.Excel.MapEntrance, 0, false);

        // 处理传送点偏移
        var anchor = Player.SceneInstance!.FloorInfo?.GetAnchorInfo(CurRoom.Excel.GroupID, 1);
        if (anchor != null)
        {
            Player.Data.Pos = anchor.ToPositionProto();
            Player.Data.Rot = anchor.ToRotationProto();
        }

        // 同步当前点位状态
        await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(CurRoom, AreaExcel.MapId));

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

 public override void OnBattleStart(BattleInstance battle)
{
    base.OnBattleStart(battle);

    if (CurRoom == null) return;

    // --- 1. 动态计算怪物等级 (核心：解决你说的等级过高问题) ---
    // 根据 AreaProgress (世界) 和 Difficulty (难度) 动态计算等级
    int progress = AreaExcel.AreaProgress;   // 世界几 (如 3)
    int difficulty = AreaExcel.Difficulty;     // 难度几 (如 1)
    
    // 公式建议：世界基准等级 + (难度-1) * 10 + 随房间进度微增
    // 示例：世界3 难度1 -> 30 + 0 + (13/2) ≈ 36级
    // 示例：世界3 难度2 -> 30 + 10 + (13/2) ≈ 46级
    uint targetLevel = (uint)(progress * 10 + (difficulty - 1) * 10 + 5 + (CurReachedRoom / 2));
    
    // 强制设置战斗等级，这会覆盖配置表里的默认等级
    battle.Level = targetLevel;

    // --- 2. 决定怪物组 (CustomLevel) ---
    if (CurRoom.Excel.RogueRoomType == 7) 
    {
        // BOSS 房：使用世界+难度的组合 ID
        battle.CustomLevel = (progress * 100000) + (difficulty * 100) + 1;
    }
    else 
    {
        // 普通房：根据 MapId 获取怪物列表
        // 确保 MapId 考虑了难度 (progress * 100 + difficulty)
        int mapId = progress * 100 + difficulty;
        
        if (GameData.RogueMapData.TryGetValue(mapId, out var mapData))
        {
            if (mapData.TryGetValue(CurRoom.SiteId, out var mapInfo) && mapInfo.LevelList.Count > 0)
            {
                // 随机抽取一个怪物组合
                battle.CustomLevel = mapInfo.LevelList.RandomElement();
            }
        }
    }
    
    // 调试打印：让你在服务器控制台看到当前的怪物强度
    // Console.WriteLine($"[Rogue] 战斗开始: 世界{progress} 难度{difficulty} 房间进度{CurReachedRoom} -> 最终等级: {battle.Level}, 怪物组ID: {battle.CustomLevel}");
}

    public override async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        foreach (var miracle in RogueMiracles.Values) miracle.OnEndBattle(battle);

        if (req.EndStatus != BattleEndStatus.BattleEndWin)
        {
            await QuitRogue();
            return;
        }

        // 如果没有后续路径，则判定为最终胜利
        if (CurRoom!.NextSiteIds.Count == 0)
        {
            IsWin = true;
            await Player.SendPacket(new PacketSyncRogueExploreWinScNotify());
        }
        else
        {
            await RollBuff(battle.Stages.Count);
            await GainMoney(Random.Shared.Next(20, 60) * battle.Stages.Count);
        }
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
            RogueMap = ToMapInfo(), // 核心地图同步协议
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
        // 关键：返回 AreaId 和 MapId 以便客户端渲染背景资源
        var proto = new RogueMapInfo
        {
            CurSiteId = (uint)(CurRoom?.SiteId ?? StartSiteId),
            CurRoomId = (uint)(CurRoom?.RoomId ?? 0),
            AreaId = (uint)AreaExcel.RogueAreaID,
            MapId = (uint)AreaExcel.MapId
        };

        // 将每个房间的随机 RoomId 和图标类型同步给前端
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
        return new RogueVirtualItem { RogueMoney = (uint)CurMoney };
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
}
