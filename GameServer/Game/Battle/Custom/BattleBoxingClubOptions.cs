using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Scene; // 必须添加，AvatarSceneInfo 在这里
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.GameServer.Game.Battle.Custom;

// 确保构造函数只定义了这两个参数
public class BattleBoxingClubOptions(List<uint> selectedBuffs, PlayerInstance player)
{
    public List<uint> SelectedBuffs { get; set; } = selectedBuffs;
    public PlayerInstance Player { get; set; } = player;

    public void HandleProto(SceneBattleInfo proto, BattleInstance battle)
    {
        // 这里只处理 Buff 注入
        foreach (var buffId in SelectedBuffs)
        {
            proto.BuffList.Add(new BattleBuff
            {
                Id = buffId,
                Level = 1,
                OwnerIndex = 0xFFFFFFFF 
            });
        }
        // 3. 注入动态值开关
    	// 很多监听器逻辑需要 Value1 = 1.0f 才能激活 OnListenCharacterCreate
    	foreach (var buff in proto.BuffList) {
        buff.DynamicValues.Add("Value1", 1.0f);
    	}
    }
}
