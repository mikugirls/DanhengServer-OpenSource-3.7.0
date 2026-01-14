using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Config.Scene;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Enums.Mission;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Scene.Component;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Scene;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using EggLink.DanhengServer.Enums.Scene;
namespace EggLink.DanhengServer.GameServer.Game.Scene.Entity;

public class EntityMonster(
    SceneInstance scene,
    Position pos,
    Position rot,
    int groupId,
    int instId,
    NPCMonsterDataExcel excel,
    MonsterInfo info) : BaseGameEntity, IGameModifier
{
    public Position Position { get; set; } = pos;
    public Position Rotation { get; set; } = rot;
    public int InstId { get; set; } = instId;
    public SceneInstance Scene { get; set; } = scene;
    public NPCMonsterDataExcel MonsterData { get; set; } = excel;
    public MonsterInfo Info { get; set; } = info;
    public SceneBuff? TempBuff { get; set; }
    public bool IsAlive { get; private set; } = true;

    public int EventId { get; set; } = info.EventID;
    public int CustomStageId { get; set; } = 0;

    public int RogueMonsterId { get; set; } = 0;
    public int CustomLevel { get; set; } = 0;
    public int HardLevelGroup { get; set; } = 0;
    public override int EntityId { get; set; } = 0;
    public override int GroupId { get; set; } = groupId;

    public List<string> Modifiers { get; set; } = [];

    public async ValueTask AddModifier(string modifierName)
    {
        if (Modifiers.Contains(modifierName)) return;

        GameData.AdventureModifierData.TryGetValue(modifierName, out var modifier);
        GameData.AdventureAbilityConfigListData.TryGetValue(MonsterData.ID, out var ability);
        if (modifier == null || ability == null) return;

        await Scene.Player.TaskManager!.AbilityLevelTask.TriggerTasks(ability, modifier.OnCreate, this, [],
            new SceneCastSkillCsReq());

        Modifiers.Add(modifierName);
    }

    public async ValueTask RemoveModifier(string modifierName)
    {
        if (!Modifiers.Contains(modifierName)) return;

        GameData.AdventureModifierData.TryGetValue(modifierName, out var modifier);
        GameData.AdventureAbilityConfigListData.TryGetValue(MonsterData.ID, out var ability);
        if (modifier == null || ability == null) return;

        await Scene.Player.TaskManager!.AbilityLevelTask.TriggerTasks(ability, modifier.OnDestroy, this, [],
            new SceneCastSkillCsReq());

        Modifiers.Remove(modifierName);
    }

    public override async ValueTask AddBuff(SceneBuff buff)
    {
        if (!GameData.MazeBuffData.TryGetValue(buff.BuffId * 10 + buff.BuffLevel, out var buffExcel)) return;

        await AddModifier(buffExcel.ModifierName);
        var oldBuff = BuffList.Find(x => x.BuffId == buff.BuffId);
        if (oldBuff != null) BuffList.Remove(oldBuff);
        BuffList.Add(buff);
        await Scene.Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(this, buff));
    }

    public override async ValueTask ApplyBuff(BattleInstance instance)
    {
        if (TempBuff != null)
        {
            instance.Buffs.Add(new MazeBuff(TempBuff));
            TempBuff = null;
        }

        if (BuffList.Count == 0) return;

        foreach (var buff in BuffList)
        {
            if (buff.IsExpired()) continue;
            instance.Buffs.Add(new MazeBuff(buff));
        }

        await Scene.Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(this, BuffList));

        BuffList.Clear();
    }

    public override SceneEntityInfo ToProto()
    {
        var proto = new SceneEntityInfo
        {
            EntityId = (uint)EntityId,
            GroupId = (uint)GroupId,
            InstId = (uint)InstId,
            Motion = new MotionInfo
            {
                Pos = Position.ToProto(),
                Rot = Rotation.ToProto()
            },
            NpcMonster = new SceneNpcMonsterInfo
            {
                EventId = (uint)EventId,
                MonsterId = (uint)MonsterData.ID,
                WorldLevel = (uint)Scene.Player.Data.WorldLevel
            }
        };

        if (RogueMonsterId > 0)
            proto.NpcMonster.ExtraInfo = new NpcMonsterExtraInfo
            {
                RogueGameInfo = new NpcMonsterRogueInfo
                {
                    RogueMonsterId = (uint)RogueMonsterId,
                    Level = (uint)CustomLevel,
                    HardLevelGroup = (uint)HardLevelGroup
                }
            };

        return proto;
    }

    public async ValueTask RemoveBuff(int buffId)
    {
        if (!GameData.MazeBuffData.TryGetValue(buffId * 10 + 1, out var buffExcel)) return;

        var buff = BuffList.Find(x => x.BuffId == buffId);
        if (buff == null) return;

        BuffList.Remove(buff);
        await Scene.Player.SendPacket(new PacketSyncEntityBuffChangeListScNotify(this, [buff]));

        await RemoveModifier(buffExcel.ModifierName);
    }

    public int GetStageId()
    {
        if (CustomStageId > 0) return CustomStageId;
        return Info.EventID;
    }
    public async ValueTask<List<ItemData>> Kill(bool sendPacket = true)
    {
        IsAlive = false;

        // 1. 处理掉落逻辑
        GameData.MonsterDropData.TryGetValue(MonsterData.ID * 10 + Scene.Player.Data.WorldLevel, out var dropData);
        if (dropData == null) return [];
        var dropItems = dropData.CalculateDrop();
        await Scene.Player.InventoryManager!.AddItems(dropItems, sendPacket);

        // --- 核心修复：黑名单判定 + 类型判定 ---
        // 只有在非模拟宇宙模式下，才尝试解锁同组宝箱，彻底解决“门进不去”的 Bug
        if (Scene.GameModeType != GameModeTypeEnum.RogueExplore &&   // 5
            Scene.GameModeType != GameModeTypeEnum.RogueChallenge && // 6
            Scene.GameModeType != GameModeTypeEnum.RogueAeonRoom &&  // 13
            Scene.GameModeType != GameModeTypeEnum.ChessRogue &&      // 16
            Scene.GameModeType != GameModeTypeEnum.TournRogue &&      // 17
            Scene.GameModeType != GameModeTypeEnum.MagicRogue)       // 20
        {
            // 获取同组的所有物件
            var relatedProps = Scene.Entities.Values
                .OfType<EntityProp>()
                .Where(p => p.GroupId == this.GroupId);

            foreach (var prop in relatedProps)
            {
                // 双重保险：即便是大世界，如果检测到是肉鸽属性的门，也跳过
                if (prop is RogueProp) continue;

                // 执行解锁逻辑，确保大世界、城镇、活动挑战正常
                await prop.SetState(PropStateEnum.ChestClosed);
            }
        }
        // ---------------------------------------

        // 2. 任务处理
        await Scene.Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.KillMonster, this);
        
        // 3. 移除实体
        await Scene.RemoveEntity(this);
        
        return dropItems;
    }
   
}
