using EggLink.DanhengServer.Database.Avatar;
using EggLink.DanhengServer.Database.Lineup;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;
using LineupInfo = EggLink.DanhengServer.Database.Lineup.LineupInfo;

namespace EggLink.DanhengServer.GameServer.Game.Battle.Custom;

public class BattleBoxingClubOptions(List<AvatarSceneInfo> customAvatars, List<uint> selectedBuffs, PlayerInstance player)
{
    public List<AvatarSceneInfo> CustomAvatars { get; set; } = customAvatars;
    public List<uint> SelectedBuffs { get; set; } = selectedBuffs;
    public PlayerInstance Player { get; set; } = player;

    public void HandleProto(SceneBattleInfo proto, BattleInstance battle)
    {
        // 1. 模仿他：创建一个临时的搏击俱乐部编队上下文
        var tempLineup = new LineupInfo
        {
            BaseAvatars = CustomAvatars.Select(x => new LineupAvatarInfo
            {
                BaseAvatarId = x.AvatarInfo.BaseAvatarId,
                SpecialAvatarId = x.AvatarInfo is SpecialAvatarInfo s ? s.SpecialAvatarId : 0
            }).ToList(),
            LineupType = (int)ExtraLineupType.LineupBoxingClub
        };

        // 2. 模仿他：格式化角色并注入协议
        foreach (var avatarScene in CustomAvatars)
        {
            // 确保满血满能
            avatarScene.AvatarInfo.SetCurHp(10000, true);
            avatarScene.AvatarInfo.SetCurSp(10000, true);

            var battleAvatar = avatarScene.AvatarInfo.ToBattleProto(
                new PlayerDataCollection(Player.Data, Player.InventoryManager!.Data, tempLineup),
                avatarScene.AvatarType);
            
            // 按照选人顺序设置索引 0-3
            battleAvatar.Index = (uint)CustomAvatars.IndexOf(avatarScene);
            proto.BattleAvatarList.Add(battleAvatar);
        }

        // 3. 注入搏击俱乐部的增益 (Resonance)
        foreach (var buffId in SelectedBuffs)
        {
            proto.BuffList.Add(new BattleBuff
            {
                Id = buffId,
                Level = 1,
                OwnerIndex = 0xFFFFFFFF // 全队共享
            });
        }
    }
}
