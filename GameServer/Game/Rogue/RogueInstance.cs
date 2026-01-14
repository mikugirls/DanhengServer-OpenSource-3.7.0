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

        // --- 核心修复：传入整个 areaExcel 以便同步等级和世界 ID ---
        foreach (var item in areaExcel.RogueMaps.Values)
        {
            // 注意：这里需要配合你修改过的 RogueRoomInstance 构造函数
            RogueRooms.Add(item.SiteID, new RogueRoomInstance(item, areaExcel));
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
            await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(prevRoom, AreaExcel.MapId));
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

    // 1. 准备基础数据
    int progress = AreaExcel.AreaProgress; 
    int difficulty = AreaExcel.Difficulty; 
    
    // 2. 动态计算目标等级 -> 给到 CustomLevel
    // 这样 ToProto 会把这个正常的数字 (如 35) 塞进 MonsterParam.Level
    int targetLevel = progress * 10 + (difficulty - 1) * 10 + 5 + (CurReachedRoom / 2);
    battle.CustomLevel = targetLevel; 

    // 3. 确定怪物组 ID (targetStageId)
    int targetStageId = 0;
    if (CurRoom.Excel.RogueRoomType == 7) 
    {
        // 这里的计算公式生成 300101 这种 StageID
        targetStageId = (int)((progress * 100000) + (difficulty * 100) + 1);
    }
    else 
    {
        int mapId = progress * 100 + difficulty;
        if (GameData.RogueMapData.TryGetValue(mapId, out var mapData))
        {
            if (mapData.TryGetValue(CurRoom.SiteId, out var mapInfo) && mapInfo.LevelList.Count > 0)
            {
                targetStageId = mapInfo.LevelList.RandomElement();
            }
        }
    }

    // 4. 重要：同步更新 Stage 数据 (确保波次里有正确的怪)
    if (targetStageId > 0)
    {
        battle.StageId = targetStageId; // 设置主 ID
        if (GameData.StageConfigData.TryGetValue(targetStageId, out var stageConfig))
        {
            // 必须重置 Stages 列表。
            // 这样 ToProto 在执行 Stages.Select 时，生成的每一波 SceneMonsterWave 
            // 都会带有正确的 BattleStageId (Tag 3)
            battle.Stages.Clear(); 
            battle.Stages.Add(stageConfig); 
        }
    }
}

    public override async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        foreach (var miracle in RogueMiracles.Values) miracle.OnEndBattle(battle);

        if (req.EndStatus != BattleEndStatus.BattleEndWin)
        {
            await QuitRogue();
            return;
        }

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
            MapId = (uint)AreaExcel.MapId
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
} // --- 修复点 2: 确保类定义闭合 ---
