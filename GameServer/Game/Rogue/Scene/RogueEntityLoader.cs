using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Config.Scene;
using EggLink.DanhengServer.Enums.Scene;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Rogue.Scene.Entity;
using EggLink.DanhengServer.GameServer.Game.Scene;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;

namespace EggLink.DanhengServer.GameServer.Game.Rogue.Scene;

public class RogueEntityLoader(SceneInstance scene, PlayerInstance player) : SceneEntityLoader(scene)
{
    public List<int> NextRoomIds = [];
    public PlayerInstance Player = player;
    public List<int> RogueDoorPropIds = [1000, 1021, 1022, 1023];

    public override async ValueTask LoadEntity()
    {
        if (Scene.IsLoaded) return;

        var instance = Player.RogueManager?.GetRogueInstance();
        if (instance is RogueInstance rogue)
        {
            var excel = rogue.CurRoom?.Excel;
            if (excel == null) return;

            foreach (var group in excel.GroupWithContent)
            {
                Scene.FloorInfo!.Groups.TryGetValue(group.Key, out var groupData);
                if (groupData == null) continue;
                await LoadGroup(groupData);
            }
        }

        Scene.IsLoaded = true;
    }

    public override async ValueTask<List<BaseGameEntity>?> LoadGroup(GroupInfo info, bool forceLoad = false)
    {
        var entityList = new List<BaseGameEntity>();
        foreach (var npc in info.NPCList)
            try
            {
                if (await LoadNpc(npc, info) is EntityNpc entity) entityList.Add(entity);
            }
            catch
            {
            }

        foreach (var monster in info.MonsterList)
            try
            {
                if (await LoadMonster(monster, info) is EntityMonster entity) entityList.Add(entity);
            }
            catch
            {
            }

        foreach (var prop in info.PropList)
            try
            {
                if (await LoadProp(prop, info) is EntityProp entity) entityList.Add(entity);
            }
            catch
            {
            }

        return entityList;
    }

    public override async ValueTask<EntityNpc?> LoadNpc(NpcInfo info, GroupInfo group, bool sendPacket = false)
    {
        if (info.IsClientOnly || info.IsDelete) return null;
        if (!GameData.NpcDataData.ContainsKey(info.NPCID)) return null;

        RogueNpc npc = new(Scene, group, info);
        if (info.NPCID == 3013)
        {
            // generate event
            var instance = await Player.RogueManager!.GetRogueInstance()!.GenerateEvent(npc);
            if (instance != null)
            {
                npc.RogueEvent = instance;
                npc.RogueNpcId = instance.EventId;
                npc.UniqueId = instance.EventUniqueId;
            }
        }

        await Scene.AddEntity(npc, sendPacket);

        return npc;
    }

   public override async ValueTask<EntityMonster?> LoadMonster(MonsterInfo info, GroupInfo group, bool sendPacket = false)
{
    if (info.IsClientOnly || info.IsDelete) return null;
    
    var instance = Player.RogueManager?.GetRogueInstance();
    if (instance is RogueInstance rogueInstance)
    {
        var room = rogueInstance.CurRoom;
        if (room == null) return null;

        // 【修复 1】: 处理随机生成的房间找不到 Content 的情况
        if (!room.Excel.GroupWithContent.TryGetValue(group.Id, out var content))
        {
            // 如果找不到，尝试取第一个可用的 Content，或者给一个保底的 ID
            content = room.Excel.GroupWithContent.Values.FirstOrDefault();
            if (content == 0) return null; // 彻底没配置才跳过
        }

        // 获取怪物配置
		int i = room.Excel.Variation > 0 ? room.Excel.Variation : 1;
        GameData.RogueMonsterData.TryGetValue((int)(content * 10 + i), out var rogueMonster);
        if (rogueMonster == null) return null;

        GameData.NpcMonsterDataData.TryGetValue(rogueMonster.NpcMonsterID, out var excel);
        if (excel == null) return null;

        // 【修复 2】: 强制对齐 GroupId
        // 使用当前正在加载的 group.Id (这个 ID 是地图中门所在的真实 ID)
        // 这样打完怪后，DropManager 就能通过 monster.GroupId 找到同一个组里的门了
        EntityMonster entity = new(Scene, info.ToPositionProto(), info.ToRotationProto(), group.Id, info.ID, excel, info)
        {
            EventId = rogueMonster.EventID,
            CustomStageId = rogueMonster.EventID
        };

        await Scene.AddEntity(entity, sendPacket);
        return entity;
    }

    return null;
}

public override async ValueTask<EntityProp?> LoadProp(PropInfo info, GroupInfo group, bool sendPacket = false)
{
    // 1. 获取 Rogue 实例并进行空检查，彻底消除 CS8602 警告
    var rogueInstance = Player.RogueManager?.RogueInstance;
    if (rogueInstance == null) return null;

    var room = rogueInstance.CurRoom;
    if (room == null) return null;

    GameData.MazePropData.TryGetValue(info.PropID, out var propExcel);
    if (propExcel == null) return null;

    var prop = new RogueProp(Scene, propExcel, group, info);

    // 2. 处理模拟宇宙传送门逻辑
    if (RogueDoorPropIds.Contains(prop.PropInfo.PropID))
    {
        var nextSiteIds = room.NextSiteIds;
        if (nextSiteIds == null || nextSiteIds.Count == 0)
        {
            // 最终出口 (Boss 战胜利后的传送门)
            prop.CustomPropId = 1000;
        }
        else
        {
            var index = NextRoomIds.Count;
            index = Math.Min(index, nextSiteIds.Count - 1);
            
            // 安全访问房间列表
            if (rogueInstance.RogueRooms.TryGetValue(nextSiteIds[index], out var nextRoom))
            {
                prop.NextSiteId = nextSiteIds[index];
                prop.NextRoomId = nextRoom.Excel?.RogueRoomID ?? 0;
                NextRoomIds.Add(prop.NextRoomId);

                // 获取下一间房的类型
                var nextRoomType = nextRoom.Excel?.RogueRoomType ?? 1;
                
                // --- 官服样式映射修正 (基于你的反馈) ---
                prop.CustomPropId = nextRoomType switch
                {
                    1 or 2 => 1021,            // 普通战斗 (1) 和 强敌 (2) 样式相同
                    3 or 4 or 9 => 1022,       // 事件 (3)、遭遇 (4) 和 冒险 (9) 统一为事件门样式
                    6 => 1023,                 // 精英房 (6) 使用独立样式
                    7 => 1024,                 // 最终首领 (7) 使用独立样式
                    5 or 8 => 1022,            // 休整 (5) 和 交易 (8) 使用事件样式
                    _ => 1021
                };
            }
        }
			
        // 3. 修正门的状态初始化逻辑
        var curRoomType = room.Excel?.RogueRoomType ?? 0;
        
        // 官服规则：非战斗类房间，门直接开启
        // 3(事件), 5(休整), 8(交易), 9(冒险),4(遭遇有BUG)
        if (curRoomType == 3 || curRoomType == 5 || curRoomType == 8 || curRoomType == 9|| curRoomType == 4)
        {
            await prop.SetState(PropStateEnum.Open);
        }
        else
        {
            // 战斗类房间 (1, 2, 4, 6, 7) 初始关闭，等待战斗胜利
            await prop.SetState(PropStateEnum.Closed);
        }
    }
    else
    {
        // 非传送门物体，使用 Excel 默认状态
        await prop.SetState(info.State);
    }

    await Scene.AddEntity(prop, sendPacket);
    return prop;
}
}
