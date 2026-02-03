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
			// 如果打是量子 BOSS，Info.FarmElementID 已经是 1101 了
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
	// 1. 保留这个！DropManager 需要它来存放常规副本奖励（遗器、材料等）
	public List<ItemData> RaidRewardItems { get; set; } = new();	
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
	public BattleBoxingClubOptions? BoxingClubOptions { get; set; }	
    public bool IsTournRogue { get; set; }

    public delegate ValueTask OnBattleEndDelegate(BattleInstance battle, PVEBattleResultCsReq req);

    public event OnBattleEndDelegate? OnBattleEnd;

    public async ValueTask TriggerOnBattleEnd()
    {
        if (OnBattleEnd != null)
            await OnBattleEnd(this, BattleResult!);
    }

    

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
  // 修复报错：The name 'FillMonsterWaves' does not exist in the current context
	private void FillMonsterWaves(SceneBattleInfo proto)
{
    // 如果是肉鸽魔法，填充该字段
    if (MagicInfo != null) proto.BattleRogueMagicInfo = MagicInfo;

    // 填充怪物波次
    foreach (var protoWave in Stages.Select(wave => wave.ToProto()))
    {
        if (CustomLevel > 0)
        {
            foreach (var item in protoWave)
                item.MonsterParam.Level = (uint)CustomLevel;
        }
        proto.MonsterWaveList.AddRange(protoWave);
    }

    // 处理遇袭触发的额外怪物 (NextBattleMonsterIds)
    if (Player.BattleManager!.NextBattleMonsterIds.Count > 0)
    {
        var ids = Player.BattleManager!.NextBattleMonsterIds;
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
                    waveIds.Select(x => new SceneMonster { MonsterId = (uint)x })
                }
            });
        }
    }
	}
  public List<AvatarLineupData> GetBattleAvatars()
    {
        // 1. 优先处理试用关卡、战斗学院等特殊 Stage 的固定阵容
        var excel = GameData.StageConfigData[StageId];
        List<int> list = [.. excel.TrialAvatarList];

        // 如果战斗学院配置不为空，覆盖阵容
        if (CollegeConfigExcel is { TrialAvatarList.Count: > 0 }) 
            list = [.. CollegeConfigExcel.TrialAvatarList];

        if (list.Count > 0)
        {
            List<int> tempList = [.. list];
            // 根据主角性别移除不匹配的开拓者 ID
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

        // 2. 如果是试用/剧情固定阵容模式
        if (list.Count > 0) 
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
                    if (avatarInfo != null) 
                        avatars.Add(new AvatarLineupData(avatarInfo, AvatarType.AvatarFormalType));
                }
            }
            return avatars;
        }
        else
        {
            // 3. 【核心修复】常规副本/拟造花萼编队（处理助战关键点）
            List<AvatarLineupData> avatars = [];
            foreach (var avatar in Lineup.BaseAvatars!) 
            {
                BaseAvatarInfo? avatarInstance = null;
                var avatarType = AvatarType.AvatarFormalType;

                // --- 助战逻辑开始 ---
                if (avatar.AssistUid != 0)
                {
                    // 修正：使用 GetInstanceOrCreateNew 确保离线大佬的数据也能被跨库读取
                    var assistData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<AvatarData>((int)avatar.AssistUid);
                    if (assistData != null)
                    {
                        // 在对方的正式角色库里寻找该 ID
                        avatarInstance = assistData.FormalAvatars.Find(item => item.BaseAvatarId == avatar.BaseAvatarId);
                        avatarType = AvatarType.AvatarAssistType;
                    }
                }
                // --- 试用逻辑 ---
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
                // --- 自身角色逻辑 ---
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
		else if (BoxingClubOptions != null) {
    	BoxingClubOptions.HandleProto(proto, this); // 运行你的新插件
    	FillMonsterWaves(proto); // 确保怪物波次也被填充
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
