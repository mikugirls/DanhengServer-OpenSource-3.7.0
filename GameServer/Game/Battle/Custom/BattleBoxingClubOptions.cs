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
        // 1. 【核心修复】注入 BattleEventID
        // 只有注入了这个 ID，Level_MazeChallengeBuff_Ability 里的监听器（如刷怪、回能）才会启动
        if (this.EventId != 0)
        {
            proto.BattleEventList.Add(new BattleEventInfo
            {
                EventId = this.EventId
            });
        }

        // 2. 注入玩家选中的 BUFF (包含 UI 显示和内核 ID 递归)
        foreach (var buffId in SelectedBuffs)
        {
            // 注入主 Buff
            proto.BuffList.Add(new BattleBuff
            {
                Id = buffId,
                Level = 1,
                OwnerIndex = 0xFFFFFFFF,
                WaveFlag = 0xFFFFFFFF
            });

            // 【递归注入】根据 BoxingBreakBuffSelectConfig 注入 ExtraEffectID (7000xxxx 系列)
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

        // 3. 统一注入动态值开关 (Value1 = 1.0f)
        // 激活 LevelAbility 中的 Predicate 判定
        foreach (var buff in proto.BuffList)
        {
            if (!buff.DynamicValues.ContainsKey("Value1"))
            {
                buff.DynamicValues.Add("Value1", 1.0f);
            }
        }
    }
}
