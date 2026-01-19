using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Enums.Avatar;
using EggLink.DanhengServer.GameServer.Game.Battle.Custom;
using EggLink.DanhengServer.GameServer.Game.Lineup;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Scene;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.BattleCollege;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.GameServer.Game.Battle;
public delegate ValueTask OnMonsterKillDelegate(EntityMonster monster);
public class BattleInstance(PlayerInstance player, LineupInfo lineup, List<StageConfigExcel> stages)
    : BasePlayerManager(player)
{
    public BattleInstance(PlayerInstance player, LineupInfo lineup, List<EntityMonster> monsters) : this(player, lineup,
        new List<StageConfigExcel>())
    {	if (player.SceneInstance != null)
		{
			var dungeonMonster = monsters.FirstOrDefault(m => m.Info.FarmElementID > 0);
    
		if (dungeonMonster != null)
			{
			// 如果打的是量子 BOSS，Info.FarmElementID 已经是 1101 了
			this.MappingInfoId = dungeonMonster.Info.FarmElementID;
			Console.WriteLine($"[Battle-Fix] 自动从怪物属性提取奖励 ID: {this.MappingInfoId}");
			}

            
            Console.WriteLine($"[Battle-Fix] 当前层: 匹配位面奖励 ID: {this.MappingInfoId}");
		}
        if (player.ActivityManager!.TrialActivityInstance != null &&
            player.ActivityManager!.TrialActivityInstance.Data.CurTrialStageId != 0)
        {
            var instance = player.ActivityManager!.TrialActivityInstance;
            GameData.StageConfigData.TryGetValue(instance.Data.CurTrialStageId, out var stage);
            if (stage != null) Stages.Add(stage);
            StageId = Stages[0].StageID;
        }
        else
        {
            foreach (var id in monsters.Select(monster => monster.GetStageId()))
            {
                GameData.PlaneEventData.TryGetValue(id * 10 + player.Data.WorldLevel, out var planeEvent);
                if (planeEvent == null) continue;
                GameData.StageConfigData.TryGetValue(planeEvent.StageID, out var stage);
                if (stage != null) Stages.Add(stage);
            }

            EntityMonsters = monsters;
            StageId = Stages[0].StageID;
        }
    }
	 // --- 必须添加这两行 ---
    public event OnMonsterKillDelegate? OnMonsterKill;

    public async ValueTask TriggerMonsterKill(EntityMonster monster) 
    {
        if (OnMonsterKill != null) 
        {
            await OnMonsterKill.Invoke(monster);
        }
    }
    public int BattleId { get; set; } = ++player.NextBattleId;
    public int StaminaCost { get; set; }
    public int WorldLevel { get; set; }
    public int CocoonWave { get; set; }
    public int MappingInfoId { get; set; }
    public int RoundLimit { get; set; }
    public int StageId { get; set; } = stages.Count > 0 ? stages[0].StageID : 0; // Set to 0 when hit monster
    public int EventId { get; set; }
    public int CustomLevel { get; set; }
    public BattleEndStatus BattleEndStatus { get; set; }

    public List<ItemData> MonsterDropItems { get; set; } = [];
	// 新增：RAID / 副本结算奖励清单（由 DropManager 填充）
	// BattleInstance.cs
	public List<ItemData> RogueFirstRewardItems { get; set; } = new();
    public List<StageConfigExcel> Stages { get; set; } = stages;
    public LineupInfo Lineup { get; set; } = lineup;
    public List<EntityMonster> EntityMonsters { get; set; } = [];
    public List<AvatarSceneInfo> AvatarInfo { get; set; } = [];
    public List<MazeBuff> Buffs { get; set; } = [];
    public BattleRogueMagicInfo? MagicInfo { get; set; }
    public Dictionary<int, BattleEventInstance> BattleEvents { get; set; } = [];
    public Dictionary<int, BattleTargetList> BattleTargets { get; set; } = [];
    public BattleCollegeConfigExcel? CollegeConfigExcel { get; set; }
    public PVEBattleResultCsReq? BattleResult { get; set; }
    public BattleGridFightOptions? GridFightOptions { get; set; }
    public bool IsTournRogue { get; set; }

    public delegate ValueTask OnBattleEndDelegate(BattleInstance battle, PVEBattleResultCsReq req);

    public event OnBattleEndDelegate? OnBattleEnd;

    public async ValueTask TriggerOnBattleEnd()
    {
        if (OnBattleEnd != null)
            await OnBattleEnd(this, BattleResult!);
    }

    public List<ItemData> RogueFirstRewardItems { get; set; } = new(); // 命名规范：模拟宇宙首通奖励项

	public ItemList GetDropItemList()
	{
    var list = new ItemList();
    // 如果战斗没赢，直接返回空
    if (BattleEndStatus != BattleEndStatus.BattleEndWin) return list;
	
    // 简单汇总：小怪掉落 + DropManager 之前算好的 Raid 奖励
    foreach (var item in MonsterDropItems) list.ItemList_.Add(item.ToProto());
    foreach (var item in RaidRewardItems) list.ItemList_.Add(item.ToProto());
	foreach (var item in RogueFirstRewardItems) list.ItemList_.Add(item.ToProto());
            
    return list;
	}

    public void AddBattleTarget(int key, int targetId, int progress, int totalProgress = 0)
    {
        if (!BattleTargets.TryGetValue(key, out var value))
        {
            value = new BattleTargetList();
            BattleTargets.Add(key, value);
        }

        var battleTarget = new BattleTarget
        {
            Id = (uint)targetId,
            Progress = (uint)progress,
            TotalProgress = (uint)totalProgress
        };
        value.BattleTargetList_.Add(battleTarget);
    }

    public List<AvatarLineupData> GetBattleAvatars()
    {
        var excel = GameData.StageConfigData[StageId];
        List<int> list = [.. excel.TrialAvatarList];

        // if college excel is not null
        if (CollegeConfigExcel is { TrialAvatarList.Count: > 0 }) list = [.. CollegeConfigExcel.TrialAvatarList];

        if (list.Count > 0)
        {
            List<int> tempList = [.. list];
            if (Player.Data.CurrentGender == Gender.Man)
                foreach (var avatar in tempList.Where(avatar =>
                             GameData.SpecialAvatarData.TryGetValue(avatar * 10 + 0, out var specialAvatarExcel) &&
                             specialAvatarExcel.AvatarID is 8002 or 8004 or 8006))
                    list.Remove(avatar);
            else
                foreach (var avatar in tempList.Where(avatar =>
                             GameData.SpecialAvatarData.TryGetValue(avatar * 10 + 0, out var specialAvatarExcel) &&
                             specialAvatarExcel.AvatarID is 8001 or 8003 or 8005))
                    list.Remove(avatar);
        }

        if (list.Count > 0) // if list is not empty
        {
            List<AvatarLineupData> avatars = [];
            foreach (var avatar in list)
            {
                var specialAvatar = Player.AvatarManager!.GetTrialAvatar(avatar);
                if (specialAvatar != null)
                {
                    specialAvatar.CheckLevel(Player.Data.WorldLevel);
                    avatars.Add(new AvatarLineupData(specialAvatar, AvatarType.AvatarTrialType));
                }
                else
                {
                    var avatarInfo = Player.AvatarManager!.GetFormalAvatar(avatar);
                    if (avatarInfo != null) avatars.Add(new AvatarLineupData(avatarInfo, AvatarType.AvatarFormalType));
                }
            }

            return avatars;
        }
        else
        {
            List<AvatarLineupData> avatars = [];
            foreach (var avatar in Lineup.BaseAvatars!) // if list is empty, use scene lineup
            {
                BaseAvatarInfo? avatarInstance = null;
                var avatarType = AvatarType.AvatarFormalType;

                if (avatar.AssistUid != 0)
                {
                    var player = DatabaseHelper.Instance!.GetInstance<AvatarData>(avatar.AssistUid);
                    if (player != null)
                    {
                        avatarInstance = player.FormalAvatars.Find(item => item.BaseAvatarId == avatar.BaseAvatarId);
                        avatarType = AvatarType.AvatarAssistType;
                    }
                }
                else if (avatar.SpecialAvatarId != 0)
                {
                    var specialAvatar = Player.AvatarManager!.GetTrialAvatar(avatar.SpecialAvatarId);
                    if (specialAvatar != null)
                    {
                        specialAvatar.CheckLevel(Player.Data.WorldLevel);
                        avatarInstance = specialAvatar;
                        avatarType = AvatarType.AvatarTrialType;
                    }
                }
                else
                {
                    avatarInstance = Player.AvatarManager!.GetFormalAvatar(avatar.BaseAvatarId);
                }

                if (avatarInstance == null) continue;

                avatars.Add(new AvatarLineupData(avatarInstance, avatarType));
            }

            return avatars;
        }
    }

    public SceneBattleInfo ToProto()
    {
        var proto = new SceneBattleInfo
        {
            BattleId = (uint)BattleId,
            WorldLevel = (uint)WorldLevel,
            RoundsLimit = (uint)RoundLimit,
            StageId = (uint)StageId,
            LogicRandomSeed = (uint)Random.Shared.Next()
        };

        if (GridFightOptions != null)
        {
            GridFightOptions.HandleProto(proto, this);  // grid fight will handle the proto itself
        }
        else
        {
            if (MagicInfo != null) proto.BattleRogueMagicInfo = MagicInfo;

            foreach (var protoWave in Stages.Select(wave => wave.ToProto()))
            {
                if (CustomLevel > 0)
                    foreach (var item in protoWave)
                        item.MonsterParam.Level = (uint)CustomLevel;

                proto.MonsterWaveList.AddRange(protoWave);
            }

            if (Player.BattleManager!.NextBattleMonsterIds.Count > 0)
            {
                var ids = Player.BattleManager!.NextBattleMonsterIds;
                // split every 5
                for (var i = 0; i < (ids.Count - 1) / 5 + 1; i++)
                {
                    var count = Math.Min(5, ids.Count - i * 5);
                    var waveIds = ids.GetRange(i * 5, count);

                    proto.MonsterWaveList.Add(new SceneMonsterWave
                    {
                        BattleStageId = (uint)(Stages.FirstOrDefault()?.StageID ?? 0),
                        BattleWaveId = (uint)(proto.MonsterWaveList.Count + 1),
                        MonsterParam = new SceneMonsterWaveParam(),
                        MonsterList =
                        {
                            waveIds.Select(x => new SceneMonster
                            {
                                MonsterId = (uint)x
                            })
                        }
                    });
                }
            }

            var avatars = GetBattleAvatars();
            foreach (var avatar in avatars)
                proto.BattleAvatarList.Add(avatar.AvatarInfo.ToBattleProto(
                    new PlayerDataCollection(Player.Data, Player.InventoryManager!.Data, Lineup), avatar.AvatarType));

            System.Threading.Tasks.Task.Run(async () =>
            {
                foreach (var monster in EntityMonsters) await monster.ApplyBuff(this);

                foreach (var avatar in AvatarInfo)
                    if (avatars.Select(x => x.AvatarInfo).FirstOrDefault(x =>
                            x.BaseAvatarId == avatar.AvatarInfo.BaseAvatarId) !=
                        null) // if avatar is in lineup
                        await avatar.ApplyBuff(this);
            }).Wait();

            foreach (var buff in Buffs.Clone())
                if (Enum.IsDefined(typeof(DamageTypeEnum), buff.BuffID))
                    Buffs.RemoveAll(x => x.BuffID == buff.BuffID && x.DynamicValues.Count == 0);
        }

        foreach (var eventInstance in BattleEvents.Values) proto.BattleEvent.Add(eventInstance.ToProto());

        for (var i = 1; i <= 5; i++)
        {
            var battleTargetEntry = new BattleTargetList();

            if (BattleTargets.TryGetValue(i, out var battleTargetList))
                battleTargetEntry.BattleTargetList_.AddRange(battleTargetList.BattleTargetList_);

            proto.BattleTargetInfo.Add((uint)i, battleTargetEntry);
        }

        // global buff
        foreach (var buff in GameData.AvatarGlobalBuffConfigData.Values)
            if (Player.AvatarManager!.GetFormalAvatar(buff.AvatarID) != null)
                // add buff
                Buffs.Add(new MazeBuff(buff.MazeBuffID, 1, -1)
                {
                    WaveFlag = -1
                });

        foreach (var buff in Buffs)
        {
            if (buff.WaveFlag != null) continue;
            var buffs = Buffs.FindAll(x => x.BuffID == buff.BuffID);
            if (buffs.Count < 2) continue;
            var count = 0;
            foreach (var mazeBuff in buffs)
            {
                mazeBuff.WaveFlag = (int)Math.Pow(2, count);
                count++;
            }
        }

        if (IsTournRogue)
            proto.AJGPJGLPMIO = new();

        proto.BuffList.AddRange(Buffs.Select(buff => buff.ToProto(this)));
        return proto;
    }
}
