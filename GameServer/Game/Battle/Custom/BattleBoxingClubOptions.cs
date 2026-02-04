using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Scene; // 必须添加，AvatarSceneInfo 在这里
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.GameServer.Game.Battle.Custom;

public class BattleBoxingClubOptions(List<uint> selectedBuffs, PlayerInstance player)
{
    public List<uint> SelectedBuffs { get; set; } = selectedBuffs;
    public PlayerInstance Player { get; set; } = player;

    public void HandleProto(SceneBattleInfo proto, BattleInstance battle)
    {
        // 角色列表 (BattleAvatarList) 不再在这里处理！
        // 因为已经在 EnterBoxingClubStage 里切换了 ExtraLineupType。
        // BattleInstance.ToProto 的默认逻辑会自动调用 GetBattleAvatars() 
        // 从槽位 19 (LineupBoxingClub) 里抓取正确的数据。

        // 我们这里只注入搏击俱乐部专属的 Buff
        foreach (var buffId in SelectedBuffs)
        {
            proto.BuffList.Add(new BattleBuff
            {
                Id = buffId,
                Level = 1,
                OwnerIndex = 0xFFFFFFFF 
            });
        }
    }
}
