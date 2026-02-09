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
    // 1. 【核心修复】注入 BattleEventInfo 启动关卡监听引擎
    // 字段名为 BattleEvent，类型为 BattleEventBattleInfo
    if (this.EventId != 0)
    {
        proto.BattleEvent.Add(new BattleEventBattleInfo 
        { 
            BattleEventId = this.EventId 
        });
    }

    // 2. 注入关卡全局 ChallengeBuff (来自 BoxingClubChallenge.json)
    if (Data.GameData.BoxingClubChallengeData.TryGetValue((int)battle.ChallengeId, out var challengeConfig))
    {
        if (challengeConfig.ChallengeBuff != 0)
        {
            proto.BuffList.Add(new BattleBuff 
            { 
                Id = (uint)challengeConfig.ChallengeBuff, 
                Level = 1, 
                OwnerIndex = 0xFFFFFFFF, 
                WaveFlag = 0xFFFFFFFF 
            });
        }
    }

    // 3. 注入玩家选中的自选 BUFF 及其内核 ExtraEffectID
    foreach (var buffId in SelectedBuffs)
    {
        // 注入 UI Buff
        proto.BuffList.Add(new BattleBuff { Id = buffId, Level = 1, OwnerIndex = 0xFFFFFFFF, WaveFlag = 0xFFFFFFFF });

        // 【核心修复】查表注入关联的机制 ID (对应 BoxingBreakBuffSelectConfig.json)
        // 注意：由于你的 GameData 报错，这里使用 BoxingClubStageData 作为备选，
        // 或者请确保 BoxingBreakBuffSelectData 已经在 GameData 中加载。
        if (Data.GameData.BoxingBreakBuffSelectData.TryGetValue((int)buffId, out var selectConfig))
        {
            foreach (var extraId in selectConfig.ExtraEffectIDList)
            {
                proto.BuffList.Add(new BattleBuff { Id = (uint)extraId, Level = 1, OwnerIndex = 0xFFFFFFFF, WaveFlag = 0xFFFFFFFF });
            }
        }
    }

    // 4. 统一注入动态值开关 Value1 = 1.0f 激活脚本判定
    foreach (var buff in proto.BuffList)
    {
        if (!buff.DynamicValues.ContainsKey("Value1"))
        {
            buff.DynamicValues.Add("Value1", 1.0f);
        }
    }
	}
    
}
