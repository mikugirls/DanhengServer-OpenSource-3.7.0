using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Scene; // 必须添加，AvatarSceneInfo 在这里
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.GameServer.Game.Battle.Custom;

// 确保构造函数只定义了这两个参数
// 修改构造函数，增加 eventId 参数
public class BattleBoxingClubOptions(List<uint> selectedBuffs, PlayerInstance player, uint eventId)
{
    public List<uint> SelectedBuffs { get; set; } = selectedBuffs;
    public PlayerInstance Player { get; set; } = player;
    public uint EventId { get; set; } = eventId; // 保存传入的 EventID
	public void HandleProto(SceneBattleInfo proto, BattleInstance battle)
{
    // 1. 【修复 EVENTID】启动超级联赛的关卡监听引擎
    // 对应 proto 中的 battle_event 字段，其 C# 属性名为 BattleEvent
    if (this.EventId != 0)
    {
        proto.BattleEvent.Add(new BattleEventBattleInfo 
        { 
            BattleEventId = this.EventId 
        });
    }

    // 2. 注入玩家选中的 BUFF (UI 显示 + 内核递归)
    foreach (var buffId in SelectedBuffs)
    {
        // 注入 UI 壳子 ID
        var mainBuff = new BattleBuff 
        { 
            Id = buffId, 
            Level = 1, 
            OwnerIndex = 0xFFFFFFFF, 
            WaveFlag = 0xFFFFFFFF 
        };
        proto.BuffList.Add(mainBuff);

        // 【内核关联修复】根据 BoxingBreakBuffSelectConfig 注入 ExtraEffectID (7000xxxx 系列)
        // 注意：请确保 GameData.BoxingBreakBuffSelectData 已在 GameData.cs 中定义并加载
        if (Data.GameData.BoxingBreakBuffSelectData.TryGetValue((int)buffId, out var selectConfig))
        {
            foreach (var extraId in selectConfig.ExtraEffectIDList)
            {
                proto.BuffList.Add(new BattleBuff 
                { 
                    Id = (uint)extraId, 
                    Level = 1, 
                    OwnerIndex = 0xFFFFFFFF, 
                    WaveFlag = 0xFFFFFFFF 
                });
            }
        }
    }

    // 3. 【激活逻辑】为所有注入的 Buff 补全动态数值开关
    // 许多 LevelAbility 脚本检查的是 Value1 是否为 1.0f
    foreach (var buff in proto.BuffList)
    {
        if (!buff.DynamicValues.ContainsKey("Value1"))
        {
            buff.DynamicValues.Add("Value1", 1.0f);
        }
    }
}
    
}
