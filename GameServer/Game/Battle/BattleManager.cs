using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.GameServer.Game.Battle.Custom;
using EggLink.DanhengServer.GameServer.Game.GridFight;
using EggLink.DanhengServer.GameServer.Game.GridFight.Component;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.RogueMagic;
using EggLink.DanhengServer.GameServer.Game.Scene;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Battle;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Enums.Scene;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.BoxingClub; // 必须添加这一行引用刚才创建的类
using EggLink.DanhengServer.GameServer.Game.BoxingClub;
using static EggLink.DanhengServer.GameServer.Plugin.Event.PluginEvent;

namespace EggLink.DanhengServer.GameServer.Game.Battle;

public class BattleManager(PlayerInstance player) : BasePlayerManager(player)
{
    public StageConfigExcel? NextBattleStageConfig { get; set; }
    public List<int> NextBattleMonsterIds { get; set; } = [];

    public async ValueTask<BattleInstance?> StartBattle(BaseGameEntity attackEntity,
        List<BaseGameEntity> targetEntityList,
        bool isSkill)
    {
        if (Player.BattleInstance != null) return Player.BattleInstance;
        var targetList = new List<EntityMonster>();
        var avatarList = new List<AvatarSceneInfo>();
        var propList = new List<EntityProp>();
        Player.SceneInstance!.AvatarInfo.TryGetValue(attackEntity.EntityId, out var castAvatar);

        if (castAvatar != null)
        {
            foreach (var entity in targetEntityList)
                switch (entity)
                {
                    case EntityMonster monster:
                        targetList.Add(monster);
                        break;
                    case EntityProp prop:
                        propList.Add(prop);
                        break;
                }
        }
        else
        {
            var isAmbushed =
                targetEntityList.Any(entity => Player.SceneInstance!.AvatarInfo.ContainsKey(entity.EntityId));

            if (!isAmbushed) return null;

            var monsterEntity = Player.SceneInstance!.Entities[attackEntity.EntityId];
            if (monsterEntity is EntityMonster monster) targetList.Add(monster);
        }

        if (targetList.Count == 0 && propList.Count == 0) return null;

        foreach (var prop in propList)
        {
            await Player.SceneInstance!.RemoveEntity(prop);
            if (prop.Excel.IsMpRecover)
            {
                await Player.LineupManager!.GainMp(2, true, SyncLineupReason.SyncReasonMpAddPropHit);
            }
            else if (prop.Excel.IsHpRecover)
            {
                Player.LineupManager!.GetCurLineup()!.Heal(2000, false);
                await Player.SendPacket(new PacketSyncLineupNotify(Player.LineupManager!.GetCurLineup()!));
            }
            else if (prop.PropInfo.Name == "SpeedDestruct")
            {
                var avatar = Player.SceneInstance!.AvatarInfo.Values.ToList().RandomElement();
                await avatar.AddBuff(new SceneBuff(2041101, 1, -1, 15));
            }
            else
            {
                Player.InventoryManager!.HandlePlaneEvent(prop.PropInfo.EventID);
            }

            Player.RogueManager!.GetRogueInstance()?.OnPropDestruct(prop);
        }

        if (targetList.Count > 0)
        {
            var triggerBattle = targetList.Any(target => target.IsAlive);

            if (!triggerBattle) return null;

            var inst = Player.RogueManager!.GetRogueInstance();
            if (inst is RogueMagicInstance { CurLevel.CurRoom.AdventureInstance: not null } magic)
            {
                await magic.HitMonsterInAdventure(targetList);

                foreach (var entityMonster in targetList) await entityMonster.Kill();
                return null;
            }

            BattleInstance battleInstance =
                new(Player, Player.LineupManager!.GetCurLineup()!, targetList.Where(x => x.IsAlive).ToList())
                {
                    WorldLevel = Player.Data.WorldLevel
                };
			// --- [核心修复逻辑：在这里取值] ---
			// 逻辑：从你这次打中的怪物列表里，找第一个带有副本 ID 的怪
			var firstDungeonMonster = targetList.FirstOrDefault(m => m.Info.FarmElementID > 0);

			if (firstDungeonMonster != null)
			{
			// 将怪物 Info 里保存的 FarmElementID (1101) 传给战斗实例
			battleInstance.MappingInfoId = firstDungeonMonster.Info.FarmElementID;
    
			// 打印一行日志，重启后你可以看控制台有没有这行输出
			Console.WriteLine($"[BattleManager] 副本战斗识别成功：ID {battleInstance.MappingInfoId}");
			}	

            if (NextBattleStageConfig != null)
            {
                battleInstance =
                    new BattleInstance(Player, Player.LineupManager!.GetCurLineup()!, [NextBattleStageConfig])
                    {
                        WorldLevel = Player.Data.WorldLevel
                    };
                NextBattleStageConfig = null;
            }

            avatarList.AddRange(Player.LineupManager!.GetCurLineup()!.BaseAvatars!
                .Select(item =>
                    Player.SceneInstance!.AvatarInfo.Values.FirstOrDefault(x =>
                        x.AvatarInfo.AvatarId == item.BaseAvatarId))
                .OfType<AvatarSceneInfo>());

            MazeBuff? mazeBuff = null;
            if (castAvatar != null)
            {
                var index = battleInstance.Lineup.BaseAvatars!.FindIndex(x =>
                    x.BaseAvatarId == castAvatar.AvatarInfo.AvatarId);
                GameData.AvatarConfigData.TryGetValue(castAvatar.AvatarInfo.AvatarId, out var avatarExcel);
                if (avatarExcel != null)
                {
                    mazeBuff = new MazeBuff((int)avatarExcel.DamageType, 1, index);
                    mazeBuff.DynamicValues.Add("SkillIndex", isSkill ? 2 : 1);
                }
            }
            else
            {
                mazeBuff = new MazeBuff(GameConstants.AMBUSH_BUFF_ID, 1, -1)
                {
                    WaveFlag = 1
                };
            }

            if (mazeBuff != null && mazeBuff.BuffID != 0) // avoid adding a buff with ID 0
                battleInstance.Buffs.Add(mazeBuff);

            battleInstance.AvatarInfo = avatarList;
						// 这里的逻辑：只要不是那堆肉鸽模式，就都算作“大世界”挂载掉落插件
			
            // call battle start
            Player.RogueManager!.GetRogueInstance()?.OnBattleStart(battleInstance);
            Player.ChallengeManager!.ChallengeInstance?.OnBattleStart(battleInstance);
            Player.QuestManager!.OnBattleStart(battleInstance);

            Player.BattleInstance = battleInstance;

            InvokeOnPlayerEnterBattle(Player, battleInstance);

            return battleInstance;
        }

        return null;
    }

    public async ValueTask StartStage(int eventId)
    {
        if (Player.BattleInstance != null)
        {
            await Player.SendPacket(new PacketSceneEnterStageScRsp(Player.BattleInstance));
            return;
        }

        GameData.StageConfigData.TryGetValue(eventId, out var stageConfig);
        if (stageConfig == null)
        {
            GameData.StageConfigData.TryGetValue(eventId * 10 + Player.Data.WorldLevel, out stageConfig);
            if (stageConfig == null)
            {
                await Player.SendPacket(new PacketSceneEnterStageScRsp());
                return;
            }
        }

        if (NextBattleStageConfig != null)
        {
            stageConfig = NextBattleStageConfig;
            NextBattleStageConfig = null;
        }

        BattleInstance battleInstance = new(Player, Player.LineupManager!.GetCurLineup()!, [stageConfig])
        {
            WorldLevel = Player.Data.WorldLevel,
            EventId = eventId
        };

        var avatarList = Player.LineupManager!.GetCurLineup()!.BaseAvatars!.Select(item =>
                Player.SceneInstance!.AvatarInfo.Values.FirstOrDefault(x => x.AvatarInfo.AvatarId == item.BaseAvatarId))
            .OfType<AvatarSceneInfo>().ToList();

        battleInstance.AvatarInfo = avatarList;
		Player.BoxingClubManager?.ChallengeInstance?.OnBattleStart(battleInstance);
        // call battle start
        Player.RogueManager!.GetRogueInstance()?.OnBattleStart(battleInstance);
        Player.ChallengeManager!.ChallengeInstance?.OnBattleStart(battleInstance);
        Player.QuestManager!.OnBattleStart(battleInstance);

        Player.BattleInstance = battleInstance;

        InvokeOnPlayerEnterBattle(Player, battleInstance);

        await Player.SendPacket(new PacketSceneEnterStageScRsp(battleInstance));
        Player.SceneInstance?.OnEnterStage();
    }
    public async ValueTask<BattleInstance?> StartCocoonStage(int cocoonId, int wave, int worldLevel)
{
    if (Player.BattleInstance != null) return null;

    // 1. 获取基础配置
    GameData.CocoonConfigData.TryGetValue(cocoonId * 100 + worldLevel, out var config);
    if (config == null) return null;

    wave = Math.Max(wave, 1);
    var cost = config.StaminaCost * wave;
    if (Player.Data.Stamina < cost) return null;

    // 2. 准备战斗波次
    List<StageConfigExcel> stageConfigExcels = [];
    for (var i = 0; i < wave; i++)
    {
        var stageId = config.StageIDList.RandomElement();
        GameData.StageConfigData.TryGetValue(stageId, out var stageConfig);
        if (stageConfig == null) continue;
        stageConfigExcels.Add(stageConfig);
    }
    if (stageConfigExcels.Count == 0) return null;

    // 3. 获取当前最新的阵容（如果是副本，LineupManager 应该已经 SetExtraLineup 包含了助战信息）
    var currentLineup = Player.LineupManager!.GetCurLineup();
    if (currentLineup == null) return null;

    // 4. 初始化战斗实例
    BattleInstance battleInstance = new(Player, currentLineup, stageConfigExcels)
    {
        StaminaCost = cost,
        WorldLevel = config.WorldLevel,
        CocoonWave = wave,
        MappingInfoId = config.MappingInfoID
    };

    // 处理特殊的下一场战斗配置（如剧情强制触发）
    if (NextBattleStageConfig != null)
    {
        battleInstance = new BattleInstance(Player, currentLineup, [NextBattleStageConfig])
        {
            WorldLevel = Player.Data.WorldLevel
        };
        NextBattleStageConfig = null;
    }

    // 5. 【核心修改】加载战斗角色列表
    // 不要直接从 Player.SceneInstance!.AvatarInfo 找，因为那里没有助战实体。
    // 我们调用 BattleInstance 里的 GetBattleAvatars，它会自动处理 Formal, Trial, 和 Assist。
    var avatarsFromLineup = battleInstance.GetBattleAvatars();
    var avatarList = new List<AvatarSceneInfo>();

    foreach (var avatarData in avatarsFromLineup)
    {
        // 为每一个战斗角色（包括借来的助战）创建一个临时的场景信息对象
        var sceneInfo = new AvatarSceneInfo(avatarData.AvatarInfo, avatarData.AvatarType, Player)
        {
            // 为战斗实体分配临时的 EntityId
            EntityId = ++Player.SceneInstance!.LastEntityId 
        };
        avatarList.Add(sceneInfo);
    }

    // 将加载好的（包含助战的）角色列表赋值给战斗实例
    battleInstance.AvatarInfo = avatarList;

    // 6. 启动流程
    Player.BattleInstance = battleInstance;
    Player.QuestManager!.OnBattleStart(battleInstance);

    InvokeOnPlayerEnterBattle(Player, battleInstance);
    
    await ValueTask.CompletedTask;
    return battleInstance;
}
   

    public (Retcode, BattleInstance?) StartBattleCollege(int collegeId)
    {
        if (Player.BattleInstance != null) return (Retcode.RetInBattleNow, null);

        GameData.BattleCollegeConfigData.TryGetValue(collegeId, out var config);
        if (config == null) return (Retcode.RetFail, null);

        var stageId = config.StageID;

        GameData.StageConfigData.TryGetValue(stageId, out var stageConfig);
        if (stageConfig == null) return (Retcode.RetStageConfigNotExist, null);

        BattleInstance battleInstance = new(Player, Player.LineupManager!.GetCurLineup()!, [stageConfig])
        {
            WorldLevel = Player.Data.WorldLevel,
            CollegeConfigExcel = config,
            AvatarInfo = []
        };

        // call battle start
        Player.RogueManager!.GetRogueInstance()?.OnBattleStart(battleInstance);
        Player.ChallengeManager!.ChallengeInstance?.OnBattleStart(battleInstance);
        Player.QuestManager!.OnBattleStart(battleInstance);

        Player.BattleInstance = battleInstance;

        return (Retcode.RetSucc, battleInstance);
    }

    public BattleInstance? StartGridFightBattle(GridFightInstance inst)
    {
        if (Player.BattleInstance != null) return null;

        var levelComponent = inst.GetComponent<GridFightLevelComponent>();

        var curSection = levelComponent.CurrentSection;

        var stageConfigId = curSection.Excel.StageID;
        GameData.StageConfigData.TryGetValue((int)stageConfigId, out var stageConfig);
        if (stageConfig == null) return null;

        BattleInstance battleInstance = new(Player, Player.LineupManager!.GetCurLineup()!, [stageConfig])
        {
            WorldLevel = Player.Data.WorldLevel,
            AvatarInfo = [],
            GridFightOptions = new BattleGridFightOptions(curSection, inst, Player)
        };

        battleInstance.OnBattleEnd += inst.EndBattle;
        Player.BattleInstance = battleInstance;

        Player.QuestManager!.OnBattleStart(battleInstance);

        InvokeOnPlayerEnterBattle(Player, battleInstance);

        return battleInstance;
    }



   public async ValueTask EndBattle(PVEBattleResultCsReq req)
{
    InvokeOnPlayerQuitBattle(Player, req);

    var battle = Player.BattleInstance;
    if (battle == null)
    {
        await Player.SendPacket(new PacketPVEBattleResultScRsp());
        return;
    }

    battle.BattleEndStatus = req.EndStatus;
    battle.BattleResult = req;

    var updateStatus = true;
    var teleportToAnchor = false;
    var minimumHp = 0;

    // --- 1. 异步结算掉落 (不要手动清空 dropItems) ---
    if (req.EndStatus == BattleEndStatus.BattleEndWin)
    {
        // 这里会自动填充 battle.MonsterDropItems 和 RaidRewardItems
        await Player.DropManager!.ProcessBattleRewards(battle, req);

        if (battle.StaminaCost > 0) await Player.SpendStamina(battle.StaminaCost);
    }
    else if (req.EndStatus == BattleEndStatus.BattleEndLose)
    {
        minimumHp = 2000;
        teleportToAnchor = true;
    }
    else 
    {
        teleportToAnchor = battle.CocoonWave <= 0;
        updateStatus = false;
    }

    // --- 2. 更新角色状态 ---
    if (updateStatus)
    {
        var lineup = Player.LineupManager!.GetCurLineup()!;
        foreach (var avatar in req.Stt.BattleAvatarList)
        {
            // 强制将左侧转为父类，这样 ?? 运算符就能匹配右侧了
			BaseAvatarInfo? avatarInstance = (BaseAvatarInfo?)Player.AvatarManager!.GetFormalAvatar((int)avatar.Id) ?? 
                                 Player.AvatarManager!.GetTrialAvatar((int)avatar.Id);
            
            if (avatarInstance != null)
            {
                var prop = avatar.AvatarStatus;
                var curHp = (int)Math.Max(Math.Round(prop.LeftHp / prop.MaxHp * 10000), minimumHp);
                var curSp = (int)prop.LeftSp * 100;
                avatarInstance.SetCurHp(curHp, lineup.LineupType != 0);
                avatarInstance.SetCurSp(curSp, lineup.LineupType != 0);
            }
        }
        await Player.SendPacket(new PacketSyncLineupNotify(lineup));
    }

    // --- 3. 处理传送逻辑 ---
    if (teleportToAnchor && Player.BoxingClubManager?.ChallengeInstance == null)
    {	Console.WriteLine($"[Battle] 开始传送");
        var anchorProp = Player.SceneInstance?.GetNearestSpring(long.MaxValue);
        if (anchorProp != null)
        {
            var anchor = Player.SceneInstance?.FloorInfo?.GetAnchorInfo(anchorProp.PropInfo.AnchorGroupID, anchorProp.PropInfo.AnchorID);
            if (anchor != null) await Player.MoveTo(anchor.ToPositionProto());
        }
    }

    // --- 4. 【关键：先发结算包】 ---
    // 先告诉客户端战斗赢了，展示掉落图标
    Console.WriteLine($"[Battle] 发送战斗结算包 PVEBattleResultScRsp");
    await Player.SendPacket(new PacketPVEBattleResultScRsp(req, Player, battle));

    // --- 5. 【后触发通关逻辑】 ---
    // 肉鸽 1004 的通关动画和结算界面应该在战斗包之后
    battle.OnBattleEnd += Player.MissionManager!.OnBattleFinish;
    await battle.TriggerOnBattleEnd();

    if (Player.ActivityManager!.TrialActivityInstance != null && req.EndStatus == BattleEndStatus.BattleEndWin)
        await Player.ActivityManager.TrialActivityInstance.EndActivity(TrialActivityStatus.Finish);
	// 只需要这一行：把结算丢给实例自己去处理
	if (Player.BoxingClubManager?.ChallengeInstance != null)
	{
    await Player.BoxingClubManager.ChallengeInstance.OnBattleEnd(req);
	}
    // 最后才销毁实例
    Player.BattleInstance = null;
	// 3. 【重要】拦截大世界刷新
    // 如果是超级联赛，禁止执行默认的场景通知
    if (Player.BoxingClubManager?.ChallengeInstance != null) {
        Console.WriteLine("[Battle] 超级联赛进行中，跳过大世界场景刷新逻辑。");
        return; 
    }

    // 只有非活动战斗才跑下面这些
    Console.WriteLine($"[Battle] <<< 战斗流程彻底结束");
}
}
